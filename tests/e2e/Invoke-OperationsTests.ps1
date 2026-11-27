#Requires -RunAsAdministrator
<#
  Operations tests (task 8.4, automatable part). Only on the TEST VM.
    1. Second operator appstart-op2 outside "AppStart Users" -> "No permission", then added with
       add-user --member
    2. Two operators start the TestApp: one after the other (both run) and at the same moment
    3. Service process killed -> SCM restarts it (recovery actions) -> start works again
    4. Application update: EXE replaced by a changed copy -> refused; after rehash --app it starts again
    5. setup uninstall + MSI uninstall -> original state restored
  Both operators run through Start-Process -Credential in THIS session. Real second sessions, Remote
  Desktop, lock screen and reboot are manual: see MANUAL-TESTS.md.
  Output: <StageDir>\operations-run.txt
#>
param(
    [Parameter(Mandatory)] [string] $MsiPath,
    [string] $StageDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path $MsiPath).Path
Start-Transcript -Path (Join-Path $StageDir 'operations-run.txt') -Force | Out-Null
. (Join-Path $PSScriptRoot 'E2E.Common.ps1')

$op2Name = 'appstart-op2'

function Get-OriginalState {
    $s = [ordered]@{}
    $s['Local accounts'] = (@(Get-LocalUser | Where-Object { $_.Name -ne $op2Name } | ForEach-Object Name) | Sort-Object) -join ','
    $s['Local groups'] = (@(Get-LocalGroup | ForEach-Object Name) | Sort-Object) -join ','
    $s['Service AppStart'] = [string][bool](Get-Service AppStart -ErrorAction SilentlyContinue)
    $s['Program folder'] = [string](Test-Path $ProgDir)
    $s['apps.json'] = [string](Test-Path $ConfigPath)
    $s['Start menu AppStart'] = [string](Test-Path $StartMenuDir)
    $s['Launcher shortcuts'] = (@(Get-LauncherShortcuts | ForEach-Object Path) | Sort-Object) -join ','
    $s['Data folder SDDL'] = (Get-Sddl $TestDataDir) -replace 'AI', ''
    $s['Registry key SDDL'] = (Get-Sddl $TestRegKey) -replace 'AI', ''
    $s['Registry value'] = (Get-ItemProperty $TestRegKey).Password
    $s['Product'] = [string][bool](Get-AppStartProduct)
    return $s
}

function Start-Pair([pscredential] $a, [pscredential] $b, [string] $appId, [int] $delayMs) {
    # Starts both launchers, the second after $delayMs; returns the two results.
    $pa = Start-Process $LauncherExe -Credential $a -ArgumentList "--app $appId" -WorkingDirectory $CurDir -PassThru
    if ($delayMs) { Start-Sleep -Milliseconds $delayMs }
    $pb = Start-Process $LauncherExe -Credential $b -ArgumentList "--app $appId" -WorkingDirectory $CurDir -PassThru
    $null = $pa.Handle; $null = $pb.Handle
    # Dialogs of both are read in turn
    $ra = Resolve-LauncherExit (Wait-ProcessHandlingDialogs $pa 30 '')
    $rb = Resolve-LauncherExit (Wait-ProcessHandlingDialogs $pb 30 '')
    Write-Host "  op1 exit=$($ra.Exit) $($ra.Texts) | op2 exit=$($rb.Exit) $($rb.Texts)"
    return @($ra, $rb)
}

try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), Session $((Get-Process -Id $PID).SessionId), MSI $MsiPath"
    if (Get-AppStartProduct) { throw 'AppStart is already installed. Run the test only on a fresh test VM (snapshot).' }
    Initialize-WorkDir
    New-TestAppPrerequisites
    $original = Get-OriginalState

    $code = Install-AppStartMsi $MsiPath (Join-Path $StageDir 'operations-msi.log')
    Check 'Preparation: MSI installed' ($code -eq 0) "exit $code"
    $r = Invoke-Setup @('add', '--exe', $PackagedTestApp, '--data-dir', $TestDataDir, '--reg-key', $TestRegKeyCli, '--name', $TestAppName)
    Check 'Preparation: TestApp approved' ($r.Exit -eq 0) ''
    $pw1 = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $pw1
    Check 'Preparation: appstart-user created' ($r.Exit -eq 0) ''
    $op1 = New-OperatorCredential 'appstart-user' $pw1
    $pw2 = New-RandomPassword
    New-LocalTestUser $op2Name $pw2
    $op2 = New-OperatorCredential $op2Name $pw2
    $appId = Get-TestAppId $PackagedTestApp
    $mySession = (Get-Process -Id $PID).SessionId

    # ------------------------------------------------------------ 1. user outside the group, then added
    $l = Start-LauncherAs $op2 "--app $appId" 'operations-nopermission.png'
    Check 'User outside AppStart Users: "No permission", exit code 4, no start' ($l.Exit -eq 4 -and $l.Texts -like '*No permission*' -and @(Get-TestAppProcesses).Count -eq 0) "exit=$($l.Exit)"
    $r = Invoke-Setup @('add-user', '--member', "$env:COMPUTERNAME\$op2Name")
    $isMember = [bool](Get-LocalGroupMember -Group 'AppStart Users' -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*\$op2Name" })
    Check 'add-user --member: second operator in AppStart Users' ($r.Exit -eq 0 -and $isMember) ''
    $r = Invoke-Setup @('verify') -Quiet
    Check 'verify with two operators: exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ 2. two operators
    Start-Sleep -Seconds 3
    $t = Get-Date
    $res = Start-Pair $op1 $op2 $appId 6000   # first start (S4U, profile) takes ~1 s, then 2 s cooldown per session
    $apps = @(Wait-TestApp $t 2)
    # Exit codes of processes started via -Credential are not always readable (-1/empty): the TestApps count.
    Check 'Two operators one after the other (6 s apart): both starts OK, two TestApps as appstart-app' ($res[0].Exit -in 0, -1, $null, '' -and $res[1].Exit -in 0, -1, $null, '' -and $apps.Count -eq 2) "exit=$($res[0].Exit)/$($res[1].Exit) apps=$($apps.Count)"
    Check 'Two operators: both TestApps in the session of the requester' (@($apps | Where-Object { $_.Session -ne $mySession }).Count -eq 0 -and $apps.Count -eq 2) (($apps | ForEach-Object { "pid $($_.Id) session $($_.Session)" }) -join ', ')
    Stop-TestApps

    Start-Sleep -Seconds 3
    $t = Get-Date
    $res = Start-Pair $op1 $op2 $appId 0
    $apps = @(Wait-TestApp $t 1)
    Start-Sleep -Seconds 2; $apps = @(Get-TestAppProcesses $t | Where-Object { $_.Owner -eq 'appstart-app' })
    # Judged by what each launcher showed: success = no dialog, busy = "Start already in progress" (exit codes of
    # -Credential processes are not always readable).
    $okCount = @($res | Where-Object { -not $_.Texts -and -not $_.TimedOut }).Count
    $busy = @($res | Where-Object { $_.Texts -like '*Start already in progress*' }).Count
    # Same session (Start-Process -Credential): the per-session gate lets one through and answers "busy" to the other.
    # With two real sessions both would start: manual test M3.
    Check 'Two operators at the same time in the same session: one starts, the other gets "Start already in progress"' ($okCount -eq 1 -and $busy -eq 1 -and $apps.Count -eq 1) "ok=$okCount busy=$busy apps=$($apps.Count)"
    Check 'Event 1203 (DeniedBusy) logged' ((Get-AppStartEvents 1203 $t).Count -ge 1) ''
    Stop-TestApps

    # ------------------------------------------------------------ 3. service restart by the SCM
    $svc = Get-CimInstance Win32_Service -Filter "Name='AppStart'"
    $oldPid = [int]$svc.ProcessId
    $t = Get-Date
    Stop-Process -Id $oldPid -Force
    $newPid = 0; $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        $svc = Get-CimInstance Win32_Service -Filter "Name='AppStart'"
        if ($svc.State -eq 'Running' -and $svc.ProcessId -and [int]$svc.ProcessId -ne $oldPid) { $newPid = [int]$svc.ProcessId; break }
    }
    $restartSec = [int]((Get-Date) - $t).TotalSeconds
    Check 'Service process ended -> SCM restarts the service (recovery)' ($newPid -ne 0) "old $oldPid, new $newPid after ~$restartSec s"
    $scmEvent = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Service Control Manager'; Id = 7031; StartTime = $t } -ErrorAction SilentlyContinue)
    Check 'System event 7031 (service ended unexpectedly, restart) present' ($scmEvent.Count -ge 1) ''
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op1 "--app $appId" ''
    $apps = @(Wait-TestApp $t)
    Check 'After restart of the service: start as appstart-user works' ($l.Exit -eq 0 -and $apps.Count -ge 1) "exit=$($l.Exit)"
    Stop-TestApps

    # ------------------------------------------------------------ 4. application update + rehash
    $changed = Join-Path $WorkDir 'AppStart.TestApp.update.exe'
    Copy-Item $PackagedTestApp $changed -Force
    Set-OneByteChanged $changed
    Copy-Item $changed $PackagedTestApp -Force   # the "update": EXE replaced by a different build
    Check 'Update of the application: EXE replaced by a modified copy' ((Get-FileHash $PackagedTestApp).Hash -eq (Get-FileHash $changed).Hash) ''
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op1 "--app $appId" 'operations-update-denied.png'
    Check 'Update without rehash: launch denied (exit code 3), event 1201' ($l.Exit -in 3, -1 -and $l.Texts -like '*Launch denied*' -and (Get-AppStartEvents 1201 $t).Count -ge 1 -and @(Get-TestAppProcesses $t).Count -eq 0) "exit=$($l.Exit)"
    $r = Invoke-Setup @('rehash', '--app', $appId)
    Check 'rehash --app: exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op1 "--app $appId" ''
    $apps = @(Wait-TestApp $t)
    Check 'After rehash: start of the updated application works' ($l.Exit -eq 0 -and $apps.Count -ge 1) "exit=$($l.Exit)"
    Stop-TestApps
    $r = Invoke-Setup @('verify') -Quiet
    Check 'After rehash: verify exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ 5. uninstall
    $r = Invoke-Setup @('uninstall')
    Check 'uninstall (CLI): exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Remove-LocalUser $op2Name   # test user, not created by AppStart (excluded from the comparison)
    $codes = @(Uninstall-AppStartMsi)
    Check 'MSI uninstallation: exit code 0' ($codes.Count -ge 1 -and @($codes | Where-Object { $_ -ne 0 }).Count -eq 0) ($codes -join ',')
    $now = Get-OriginalState
    $diff = @(foreach ($k in $original.Keys) { if ($original[$k] -ne $now[$k]) { "$k : '$($original[$k])' -> '$($now[$k])'" } })
    $diff | ForEach-Object { Write-Host "  DEVIATION $_" }
    Check 'Uninstallation: original state (accounts, group, service, program folder, shortcuts, permissions)' ($diff.Count -eq 0) "$($diff.Count) deviation(s)"
    # Information only, not part of the spec scenario
    $profiles = @(Get-CimInstance Win32_UserProfile | Where-Object { (Split-Path $_.LocalPath -Leaf) -like 'appstart-*' } | ForEach-Object LocalPath)
    Write-Host "  INFO remaining profiles: $(if ($profiles) { $profiles -join ', ' } else { 'none' })"
    Write-Host "  INFO event source AppStart after uninstallation: $([System.Diagnostics.EventLog]::SourceExists('AppStart'))"
    Write-Host "  INFO C:\ProgramData\AppStart after uninstallation: $(if (Test-Path $AppStartData) { (Get-ChildItem $AppStartData -Recurse | ForEach-Object { $_.FullName.Substring($AppStartData.Length) }) -join ', ' } else { 'removed' })"
}
catch {
    Check 'Test run without exception' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    Invoke-E2ECleanup -extraUsers @($op2Name)
    Write-Summary
    Stop-Transcript | Out-Null
}
