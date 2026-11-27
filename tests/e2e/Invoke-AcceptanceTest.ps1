#Requires -RunAsAdministrator
<#
  Acceptance test with the TestApp (task 8.3, concept "Acceptance test with the test application", 7 steps)
  plus abuse attempts of a real operator (task 8.2). Only on the TEST VM.

  MSI install -> add TestApp (data folder C:\ProgramData\AppStartTest, key HKLM\SOFTWARE\AppStartTest)
  -> add-user appstart-user (random password per run) -> the 7 steps, each as the REAL operator:
  processes of appstart-user are started with Start-Process -Credential (CreateProcessWithLogonW),
  i.e. a genuine standard user token with membership in "AppStart Users" and without admin rights.
  Everything is removed again. Output: <StageDir>\acceptance-run.txt and screenshots acceptance-*.png
#>
param(
    [Parameter(Mandatory)] [string] $MsiPath,
    [string] $StageDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path $MsiPath).Path
Start-Transcript -Path (Join-Path $StageDir 'acceptance-run.txt') -Force | Out-Null
. (Join-Path $PSScriptRoot 'E2E.Common.ps1')

$exeBackup = $null
try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), Session $((Get-Process -Id $PID).SessionId), MSI $MsiPath"
    if (Get-AppStartProduct) { throw 'AppStart is already installed. Run the test only on a fresh test VM (snapshot).' }
    Initialize-WorkDir
    New-TestAppPrerequisites

    # ------------------------------------------------------------ preparation
    $code = Install-AppStartMsi $MsiPath (Join-Path $StageDir 'acceptance-msi.log')
    Check 'Preparation: MSI installed' ($code -eq 0) "exit $code"
    $r = Invoke-Setup @('add', '--exe', $PackagedTestApp, '--data-dir', $TestDataDir, '--reg-key', $TestRegKeyCli, '--name', $TestAppName)
    Check 'Preparation: TestApp approved (add)' ($r.Exit -eq 0) ''
    $password = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
    Check 'Preparation: appstart-user created (add-user)' ($r.Exit -eq 0 -and (Get-LocalUser appstart-user -ErrorAction SilentlyContinue)) ''
    $op = New-OperatorCredential 'appstart-user' $password
    $appId = Get-TestAppId $PackagedTestApp
    $lnk = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$TestAppName.lnk"
    if (-not (Test-Path $lnk)) { $lnk = (@(Get-LauncherShortcuts) | Where-Object { $_.Arguments -eq "--app $appId" } | Select-Object -First 1).Path }
    Check 'Preparation: AppId and shortcut present' ($appId -and $lnk -and (Test-Path $lnk)) "id=$appId lnk=$lnk"

    $who = Invoke-AsOperator $op 'whoami'
    Check 'Operator token genuine: not elevated, not in Administrators, in AppStart Users' ($who -like '*elevated=False*' -and $who -like '*adminsInToken=False*' -and $who -like '*appstartUsers=True*') $who

    # ------------------------------------------------------------ step 1: start through the shortcut
    Start-Sleep -Seconds 3
    $t = Get-Date
    Start-ShortcutAs $op $lnk | Out-Null
    $apps = @(Wait-TestApp $t)
    $mySession = (Get-Process -Id $PID).SessionId
    Check 'Step 1: start through shortcut as appstart-user -> TestApp runs as appstart-app in the operator session' ($apps.Count -ge 1 -and $apps[0].Session -eq $mySession) "owner=$($apps[0].Owner) session=$($apps[0].Session)"
    if (-not $apps) {
        $dialogs = @(Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | ForEach-Object { Get-ProcessWindows $_.Id } | ForEach-Object { (Get-WindowTexts $_) -join ' | ' })
        if ($dialogs) { Write-Host "  Launcher message: $($dialogs -join ' / ')" }
    }
    $win = if ($apps) { Wait-ProcessWindow $apps[0].Id 20 } else { $null }
    if ($win) {
        Start-Sleep -Seconds 2   # checks of the TestApp run at start
        $texts = Get-WindowTextControls $win
        $joined = $texts -join ' | '
        Check 'Step 1: all indicators green (account appstart-app, admin yes, registry OK, files OK)' (
            $joined -like '*appstart-app | OK | Admin rights | Yes | OK | Read/write registry | HKLM\SOFTWARE\AppStartTest | OK | Read/write files | C:\ProgramData\AppStartTest | OK*' -and
            $texts -notcontains 'Error' -and $texts -notcontains 'Not via AppStart') $joined
        Save-WindowShot $win (Join-Path $StageDir 'acceptance-step1.png')

        # ---------------------------------------------------------- step 5: "Start command prompt"
        $btn = $win.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'Start command prompt')))
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 2
        $texts = Get-WindowTextControls $win
        $cmd = @(Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object { (Get-ProcessOwner $_) -eq 'appstart-app' })
        Check 'Step 5: "Start command prompt" fails (start blocked by job object)' ($texts -contains 'Start blocked by job object' -and $cmd.Count -eq 0) "cmd as appstart-app: $($cmd.Count)"
        Save-WindowShot $win (Join-Path $StageDir 'acceptance-step5.png')
        Close-UiWindow $win
    } else {
        Check 'Step 1: TestApp window found' $false
        Check 'Step 5: "Start command prompt" fails' $false 'no TestApp window'
    }
    Stop-TestApps

    # ------------------------------------------------------------ steps 2 and 3: registry and data folder
    $r = Invoke-AsOperator $op 'reg-read'
    Check 'Step 2: appstart-user opens HKLM\SOFTWARE\AppStartTest -> access denied' ($r -eq 'open=DENIED') $r
    $r = Invoke-AsOperator $op 'data-read'
    Check 'Step 3: appstart-user opens C:\ProgramData\AppStartTest -> access denied' ($r -eq 'list=DENIED write=DENIED') $r

    # ------------------------------------------------------------ step 4: TestApp EXE directly
    $r = Invoke-AsOperator $op 'start-direct' $PackagedTestApp
    Check 'Step 4: TestApp EXE directly as appstart-user -> aborted (740 ERROR_ELEVATION_REQUIRED)' ($r -eq 'start=E740') $r
    # Manifest bypassed (RunAsInvoker): the TestApp itself must stop with its message.
    $t = Get-Date
    $probe = Start-OperatorProbe $op 'start-invoker' $PackagedTestApp
    $own = $null; $deadline = (Get-Date).AddSeconds(60)
    while (-not $own -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500; $own = Get-TestAppProcesses $t | Where-Object { $_.Owner -eq 'appstart-user' } | Select-Object -First 1 }
    $msg = ''
    if ($own) {
        $w = Wait-ProcessWindow $own.Id 30
        if ($w) { $msg = (Get-WindowTexts $w) -join ' | '; Save-WindowShot $w (Join-Path $StageDir 'acceptance-step4.png'); Close-UiWindow $w }
    }
    $r = Complete-OperatorProbe $probe
    Check 'Step 4: TestApp without admin rights (RunAsInvoker) exits with a message, exit code 1' ($msg -like '*needs administrator rights*' -and $r -eq 'exit=1') "$r :: $msg"
    Stop-TestApps

    # ------------------------------------------------------------ step 6: one byte of the EXE changed
    Start-Sleep -Seconds 3
    $exeBackup = Join-Path $WorkDir 'AppStart.TestApp.exe.orig'
    Copy-Item $PackagedTestApp $exeBackup -Force
    Set-OneByteChanged $PackagedTestApp
    Check 'Step 6: one byte of the TestApp EXE changed' ((Get-FileHash $PackagedTestApp).Hash -ne (Get-FileHash $exeBackup).Hash) ''
    $t = Get-Date
    $l = Start-LauncherAs $op "--app $appId" 'acceptance-step6.png'
    Start-Sleep -Seconds 2
    $started = @(Get-TestAppProcesses $t)
    $ev = Get-AppStartEvents 1201 $t
    Check 'Step 6: start through launcher denied (exit code 3, "Launch denied", event ID)' ($l.Exit -eq 3 -and $l.Texts -like '*Launch denied*' -and $l.Texts -like '*Event ID*' -and $started.Count -eq 0) "exit=$($l.Exit) :: $($l.Texts)"
    Check 'Step 6: event 1201 (DeniedIntegrity) logged' ($ev.Count -ge 1) "$($ev.Count) Ereignis(se)"
    $r = Invoke-Setup @('verify')
    Check 'Step 6: verify reports the modified EXE (exit code not 0)' ($r.Exit -ne 0) "exit $($r.Exit)"
    Copy-Item $exeBackup $PackagedTestApp -Force
    Check 'Step 6: original EXE restored' ((Get-FileHash $PackagedTestApp).Hash -eq (Get-FileHash $exeBackup).Hash) ''

    # ------------------------------------------------------------ step 7: verify
    $report = Join-Path $StageDir 'acceptance-verify.html'
    $r = Invoke-Setup @('verify', '--report', $report)
    Check 'Step 7: verify -> exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
    $r = Invoke-Setup @('verify')
    Check 'Step 7: appstart-user in Administrators -> verify exit code not 0, names appstart-user' ($r.Exit -ne 0 -and $r.Text -like '*appstart-user*') "exit $($r.Exit)"
    Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
    $regSddl = Get-Sddl $TestRegKey
    $acl = Get-Acl $TestRegKey
    $acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')), 'ReadKey', 'ContainerInherit', 'None', 'Allow')))
    Set-Acl $TestRegKey $acl
    $r = Invoke-Setup @('verify')
    Check 'Step 7: registry key readable by users -> verify exit code not 0' ($r.Exit -ne 0) "exit $($r.Exit)"
    $acl = Get-Acl $TestRegKey; $acl.PurgeAccessRules((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545'))); Set-Acl $TestRegKey $acl
    Check 'Step 7: registry key permissions restored' ((Get-Sddl $TestRegKey) -eq $regSddl) ''
    $r = Invoke-Setup @('verify')
    Check 'Step 7: after correction exit code 0 again' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ abuse attempts of the operator (8.2)
    $r = Invoke-AsOperator $op 'config'
    Check 'Misuse: read/overwrite/replace/delete/rename apps.json, new file -> all denied' (
        $r -match '^read=DENIED openWrite=DENIED replace=DENIED delete=DENIED rename=(DENIED|MISSING) newFile=DENIED$') $r   # MISSING: folder not even listable
    $r = Invoke-AsOperator $op 'pipe'
    Check 'Misuse: operator may connect but not create an own pipe instance' ($r -eq 'connect=ALLOWED createInstance=DENIED') $r
    $r = Invoke-AsOperator $op 'progfiles'
    $allowed = @([regex]::Matches($r, '=(ALLOWED|MISSING|E\d+|ERROR:\S+)'))   # paths contain spaces: match results, not tokens
    Check 'Misuse: nothing in C:\Program Files\AppStart can be written, replaced or renamed' ($allowed.Count -eq 0) $r
    $r = Invoke-AsOperator $op 'start-direct' $SetupExe
    Check 'Misuse: AppStart.Setup.exe verify as operator only with admin rights (740)' ($r -eq 'start=E740') $r

    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op '--app unknown-app' 'acceptance-unknown.png'
    Check 'Misuse: unknown AppId -> "Launch denied", exit code 3, event 1200' ($l.Exit -eq 3 -and $l.Texts -like '*Launch denied*' -and (Get-AppStartEvents 1200 $t).Count -ge 1) "exit=$($l.Exit)"
    Start-Sleep -Seconds 3
    $l = Start-LauncherAs $op '' 'acceptance-noapp.png'
    Check 'Misuse: launcher without --app -> "Invalid shortcut", exit code 1' ($l.Exit -eq 1 -and $l.Texts -like '*Invalid shortcut*') "exit=$($l.Exit)"
    $l = Start-LauncherAs $op "--app $appId C:\Windows\System32\cmd.exe" ''
    Check 'Misuse: launcher with extra argument -> "Invalid shortcut", exit code 1' ($l.Exit -eq 1 -and $l.Texts -like '*Invalid shortcut*') "exit=$($l.Exit)"

    # Still everything fine after the attempts
    $r = Invoke-Setup @('verify') -Quiet
    Check 'After all attempts: verify exit code 0, apps.json present and unchanged' ($r.Exit -eq 0 -and (Test-Path $ConfigPath)) "exit $($r.Exit)"
}
catch {
    Check 'Test run without exception' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    if ($exeBackup -and (Test-Path $exeBackup) -and (Test-Path $PackagedTestApp)) { Copy-Item $exeBackup $PackagedTestApp -Force -ErrorAction SilentlyContinue }
    Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user' -ErrorAction SilentlyContinue
    Invoke-E2ECleanup
    Write-Summary
    Stop-Transcript | Out-Null
}
