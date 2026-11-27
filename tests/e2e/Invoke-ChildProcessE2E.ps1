#Requires -RunAsAdministrator
<#
  E2E of add-allowed-child-processes (task 7.1): every scenario of specs/app-launch, specs/installation and
  specs/verification of the change, in the real service context. TEST VM ONLY; everything is removed again.

  Installation: with -BootstrapperPath through AppStart-Setup.exe /quiet /config as SYSTEM (one-shot
  scheduled task, like a software distribution), else MSI + apply --config. Then the TestApp as the REAL
  operator (appstart-user, Start-Process -Credential) in the modes blocked, allowlist, all and in learn mode.
  Output: <StageDir>\child-run.txt and screenshots child-*.png
#>
param(
    [Parameter(Mandatory)] [string] $MsiPath,
    [string] $BootstrapperPath,
    [string] $StageDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path $MsiPath).Path
Start-Transcript -Path (Join-Path $StageDir 'child-run.txt') -Force | Out-Null
. (Join-Path $PSScriptRoot 'E2E.Common.ps1')

$AppId = 'appstart-testapp'
$Helper = Join-Path $CurDir 'TestApp\AppStart.TestHelper.exe'
$LearnPath = Join-Path $AppStartData 'learn.json'
$Cmd = Join-Path $env:SystemRoot 'System32\cmd.exe'

function Write-Answer([string] $path, [string] $extra = '') {
    $answer = @"
{
  "version": 1,
  "apps": [ {
    "exe": "$($PackagedTestApp -replace '\\', '\\')",
    "id": "$AppId", "name": "$TestAppName",
    "dataDir": "$($TestDataDir -replace '\\', '\\')",
    "registryKey": "$($TestRegKeyCli -replace '\\', '\\')"$extra
  } ]
}
"@
    [IO.File]::WriteAllText($path, $answer)
}

function Invoke-AsSystem([string] $exe, [string] $arguments, [int] $timeoutSec = 600) {
    # One-shot scheduled task as SYSTEM (like a software distribution). Returns the exit code, -1 on timeout.
    $name = 'AppStartE2E-System'
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    $action = New-ScheduledTaskAction -Execute $exe -Argument $arguments
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $name -Action $action -Principal $principal | Out-Null
    Start-ScheduledTask -TaskName $name
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    Start-Sleep -Seconds 3
    while ((Get-ScheduledTask -TaskName $name).State -eq 'Running' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
    $code = if ((Get-ScheduledTask -TaskName $name).State -eq 'Running') { -1 } else { [int](Get-ScheduledTaskInfo -TaskName $name).LastTaskResult }
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    return $code
}

function Get-Config { return Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json }

function Get-TestAppConfig { return @((Get-Config).apps) | Where-Object { $_.id -eq $AppId } | Select-Object -First 1 }

function Open-TestApp([pscredential] $op) {
    # Through the shortcut as the operator; returns @{ Process; Window } or $null.
    Start-Sleep -Seconds 3   # SessionGate cooldown
    $t = Get-Date
    Start-ShortcutAs $op $script:Lnk | Out-Null
    $apps = @(Wait-TestApp $t)
    if (-not $apps) { return $null }
    $win = Wait-ProcessWindow $apps[0].Id 20
    Start-Sleep -Seconds 2
    return @{ Id = $apps[0].Id; Window = $win; Since = $t }
}

function Close-TestApp($app) {
    if ($app -and $app.Window) { try { $app.Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { } }
    Start-Sleep -Seconds 1
    Stop-TestApps
}

function Find-Events([int] $id, [datetime] $since, [string] $like) {
    return @(Get-AppStartEvents $id $since | Where-Object { $_.Message -like $like })
}

function Write-LearnState([datetime] $startedUtc, [datetime] $expiresUtc) {
    $json = @"
{ "version": 1, "apps": [ { "appId": "$AppId", "startedUtc": "$($startedUtc.ToString('yyyy-MM-ddTHH:mm:ssZ'))", "expiresUtc": "$($expiresUtc.ToString('yyyy-MM-ddTHH:mm:ssZ'))" } ] }
"@
    [IO.File]::WriteAllText($LearnPath, $json)   # admin writes into the protected file: ACL stays
}

$helperBackup = $null
try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), MSI $MsiPath, Bootstrapper $BootstrapperPath"
    if (Get-AppStartProduct) { throw 'AppStart is already installed. Run the test only on a fresh test VM (snapshot).' }
    Initialize-WorkDir
    New-TestAppPrerequisites
    $answer = Join-Path $StageDir 'appstart-answer.json'
    Write-Answer $answer

    # ------------------------------------------------------------ installation in one step (spec installation)
    if ($BootstrapperPath) {
        $code = Invoke-AsSystem $BootstrapperPath "/quiet /config `"$answer`""
        $applyLog = @(Get-ChildItem (Join-Path $AppStartData 'Logs') -Filter 'apply-*.log' -ErrorAction SilentlyContinue)
        Check 'Software distribution in one step: AppStart-Setup.exe /quiet /config as SYSTEM -> exit code 0, set up, log in Logs' (
            $code -in 0, 3010 -and (Get-TestAppConfig) -and $applyLog.Count -ge 1) "exit $code, apply-Logs $($applyLog.Count)"
        $bad = Join-Path $StageDir 'appstart-answer-bad.json'
        Write-Answer $bad ', "password": "geheim"'
        $code = Invoke-AsSystem $BootstrapperPath "/quiet /config `"$bad`""
        Check 'Setup fails: invalid answer file -> bootstrapper exit code 11' ($code -eq 11) "exit $code"
    } else {
        $code = Install-AppStartMsi $MsiPath (Join-Path $StageDir 'child-msi.log')
        Check 'Preparation: MSI installed' ($code -eq 0) "exit $code"
        $r = Invoke-Setup @('apply', '--config', $answer)
        Check 'First setup: apply --config -> exit code 0' ($r.Exit -eq 0 -and (Get-TestAppConfig)) "exit $($r.Exit)"
        Skip 'Bootstrapper /quiet /config as SYSTEM' 'without -BootstrapperPath'
    }

    $before = (Get-FileHash $ConfigPath).Hash
    $r = Invoke-Setup @('apply', '--config', $answer)
    Check 'Second run: same answer file -> nothing changed, exit code 0' ($r.Exit -eq 0 -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"
    $bad = Join-Path $StageDir 'appstart-answer-missing.json'
    [IO.File]::WriteAllText($bad, '{ "version": 1, "apps": [ { "exe": "C:\\nicht\\da\\App.exe" } ] }')
    $r = Invoke-Setup @('apply', '--config', $bad)
    Check 'Invalid file: path does not exist -> nothing changed, output names the field, exit code 2' (
        $r.Exit -eq 2 -and $r.Text -like '*apps`[0`].exe*' -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"

    $password = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
    $op = New-OperatorCredential 'appstart-user' $password
    $script:Lnk = (@(Get-LauncherShortcuts) | Where-Object { $_.Arguments -eq "--app $AppId" } | Select-Object -First 1).Path
    Check 'Preparation: operator account and shortcut' ($r.Exit -eq 0 -and $script:Lnk) "lnk=$($script:Lnk)"

    # ------------------------------------------------------------ mode blocked (default)
    $app = Open-TestApp $op
    if ($app) {
        Invoke-TestAppButton $app 'Start allowed helper program'
        $row = Get-TestAppRow $app 'Helper program (TestHelper)'
        Check 'Mode blocked: helper program blocked by job object' ($row -like 'Start blocked by job object*') $row
        Invoke-TestAppButton $app 'Start command prompt'
        $row = Get-TestAppRow $app 'Child process (cmd.exe)'
        Check 'Escape via file dialog: mode blocked -> cmd.exe blocked' ($row -like 'Start blocked by job object*' -and (Get-OperatorCmd).Count -eq 0) $row
    } else { Check 'Mode blocked: TestApp started' $false }
    Close-TestApp $app

    # ------------------------------------------------------------ command line (spec installation)
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Cmd)
    Check 'Shell without confirmation: allow-child cmd.exe -> exit code 2, apps.json unchanged' ($r.Exit -eq 2 -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"
    $public = 'C:\Users\Public\AppStartE2E-Helper.exe'
    Copy-Item $Helper $public -Force
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $public)
    Check 'Writable folder: helper program in C:\Users\Public -> exit code 1, apps.json unchanged' ($r.Exit -eq 1 -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"
    Remove-Item $public -Force
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Helper)
    $cfg = Get-TestAppConfig
    Check 'Allow helper program via command line: exit code 0, allowlist with TestHelper, verify ran' (
        $r.Exit -eq 0 -and $cfg.childProcesses -eq 'allowlist' -and @($cfg.allowedChildren).exe -contains $Helper -and $r.Text -like '*Automatic verification*') "exit $($r.Exit)"

    # ------------------------------------------------------------ mode allowlist (spec app-launch "Allowlist for child processes")
    $app = Open-TestApp $op
    if ($app) {
        $t = Get-Date
        Invoke-TestAppButton $app 'Start allowed helper program'
        $row = Get-TestAppRow $app 'Helper program (TestHelper)'
        Check 'Allowed helper program: TestHelper runs as appstart-app, info event 1101' (
            $row -like 'Started and ended*appstart-app*' -and (Find-Events 1101 $t "*$Helper*").Count -ge 1) $row
        Check 'Grandchild process: cmd.exe from the TestHelper is ended, event 1206' (
            $row -like '*Grandchild process cmd.exe: ended by AppStart*' -and (Find-Events 1206 $t '*cmd.exe*').Count -ge 1) $row
        Save-WindowShot $app.Window (Join-Path $StageDir 'child-allowlist.png')
        $t = Get-Date
        Invoke-TestAppButton $app 'Start command prompt'
        $row = Get-TestAppRow $app 'Child process (cmd.exe)'
        $ev = Find-Events 1206 $t '*cmd.exe*'
        Check 'Process not allowed: cmd.exe ended immediately, warning event 1206 with user, AppId, path, reason' (
            $row -like 'Ended immediately by AppStart*' -and $ev.Count -ge 1 -and $ev[0].Message -like '*appstart-user*' -and $ev[0].Message -like "*$AppId*" -and $ev[0].Message -like '*Reason:*' -and (Get-OperatorCmd).Count -eq 0) $row
    } else { Check 'Mode allowlist: TestApp started' $false }
    Close-TestApp $app

    # ------------------------------------------------------------ changed helper (app-launch "Modified file", verification "Helper program modified")
    $helperBackup = Join-Path $WorkDir 'AppStart.TestHelper.exe.orig'
    Copy-Item $Helper $helperBackup -Force
    Set-OneByteChanged $Helper
    $app = Open-TestApp $op
    if ($app) {
        $t = Get-Date
        Invoke-TestAppButton $app 'Start allowed helper program'
        $row = Get-TestAppRow $app 'Helper program (TestHelper)'
        Check 'Modified file: TestHelper ended, event names "Checksum does not match"' (
            $row -like 'Ended immediately by AppStart*' -and (Find-Events 1206 $t '*Checksum does not match*').Count -ge 1) $row
    } else { Check 'Modified file: TestApp started' $false }
    Close-TestApp $app
    $r = Invoke-Setup @('verify')
    Check 'Helper program modified: verify reports deviation with path and rehash hint' (
        $r.Exit -ne 0 -and $r.Text -like "*rehash --app $AppId --child*$Helper*") "exit $($r.Exit)"
    Copy-Item $helperBackup $Helper -Force
    $r = Invoke-Setup @('verify') -Quiet
    Check 'Original back: verify exit code 0 again' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ app ends before the helper, service stops
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Cmd, '--allow-shell') -Quiet
    Check 'Preparation: cmd.exe allowed with --allow-shell' ($r.Exit -eq 0) "exit $($r.Exit)"
    $app = Open-TestApp $op
    if ($app) {
        Invoke-TestAppButton $app 'Start command prompt'
        # Not $cmd: PowerShell variable names ignore case, that would overwrite $Cmd (the path).
        $cmdProcs = @(Get-OperatorCmd)
        Close-TestApp $app
        Start-Sleep -Seconds 3
        $still = @(Get-OperatorCmd | Where-Object { $cmdProcs.ProcessId -contains $_.ProcessId })
        Check 'Application exits before the helper program: allowed cmd.exe keeps running after the TestApp ends' ($cmdProcs.Count -ge 1 -and $still.Count -ge 1) "cmd before $($cmdProcs.Count), after $($still.Count)"
        Stop-Service AppStart
        Start-Sleep -Seconds 3
        $after = @(Get-OperatorCmd | Where-Object { $cmdProcs.ProcessId -contains $_.ProcessId })
        Check 'Service stops: Windows ends all processes of the job object (KILL_ON_JOB_CLOSE)' ($still.Count -ge 1 -and $after.Count -eq 0) "after stop $($after.Count)"
        Start-Service AppStart
        Start-Sleep -Seconds 5
    } else { Check 'Application exits before the helper program: TestApp started' $false }
    Stop-OperatorCmd
    $r = Invoke-Setup @('deny-child', '--app', $AppId, '--exe', $Cmd)
    Check 'Preparation: cmd.exe removed again' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ mode all (verification "All other programs allowed")
    $r = Invoke-Setup @('set-children', '--app', $AppId, '--mode', 'all') -Quiet
    $v = Invoke-Setup @('verify')
    Check 'All other programs allowed: verify passed, note with AppId' (
        $r.Exit -eq 0 -and $v.Exit -eq 0 -and $v.Text -like "*All other programs allowed ($AppId)*") "exit $($v.Exit)"
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Helper) -Quiet
    Check 'Preparation: allowlist with TestHelper again' ($r.Exit -eq 0 -and (Get-TestAppConfig).childProcesses -eq 'allowlist') "exit $($r.Exit)"

    # ------------------------------------------------------------ learn mode (spec app-launch "Learning mode")
    $t = Get-Date
    $r = Invoke-Setup @('learn', 'start', '--app', $AppId, '--minutes', '5')
    Check 'Start learning mode: exit code 0, event 1400' ($r.Exit -eq 0 -and (Get-AppStartEvents 1400 $t).Count -ge 1) "exit $($r.Exit)"
    $v = Invoke-Setup @('verify')
    Check 'Learning mode forgotten: verify reports deviation with expiry time and learn stop' ($v.Exit -ne 0 -and $v.Text -like "*learning mode active until*learn stop --app $AppId*") "exit $($v.Exit)"
    $app = Open-TestApp $op
    if ($app) {
        $t = Get-Date
        Invoke-TestAppButton $app 'Start command prompt'
        $row = Get-TestAppRow $app 'Child process (cmd.exe)'
        $show = Invoke-Setup @('learn', 'show', '--app', $AppId)
        Check 'Recording: cmd.exe runs in learning mode and is in the recording with check values, event 1102' (
            $row -like 'Started*' -and $show.Text -like '*cmd.exe – shell or script host*' -and (Get-AppStartEvents 1102 $t).Count -ge 1) $row
        Stop-OperatorCmd
        Invoke-TestAppButton $app 'Start allowed helper program'
    } else { Check 'Learning mode: TestApp started' $false }
    Close-TestApp $app
    $r = Invoke-Setup @('learn', 'apply', '--app', $AppId, '--all')
    $cfg = Get-TestAppConfig
    Check 'learn apply --all: shell not applied, learning mode ended' (
        $r.Exit -eq 0 -and @($cfg.allowedChildren).exe -notcontains $Cmd -and (Invoke-Setup @('verify') -Quiet).Exit -eq 0) "exit $($r.Exit)"

    # "Expiry": learn mode ends in a running instance (duration 5 min, 40 s left when the app starts).
    $now = (Get-Date).ToUniversalTime()
    Write-LearnState $now.AddSeconds(-260) $now.AddSeconds(40)
    $app = Open-TestApp $op
    if ($app) {
        Invoke-TestAppButton $app 'Start command prompt'
        $first = Get-TestAppRow $app 'Child process (cmd.exe)'
        Stop-OperatorCmd
        while ((Get-Date).ToUniversalTime() -lt $now.AddSeconds(45)) { Start-Sleep -Seconds 1 }
        $t = Get-Date
        Invoke-TestAppButton $app 'Start command prompt'
        $second = Get-TestAppRow $app 'Child process (cmd.exe)'
        Check 'Expiry: after expiry the service ends cmd.exe in the running instance, event 1401' (
            $first -like 'Started*' -and $second -like 'Ended immediately by AppStart*' -and (Get-AppStartEvents 1401 $t).Count -ge 1) "before: $first / after: $second"
    } else { Check 'Expiry: TestApp started' $false }
    Close-TestApp $app
    Stop-OperatorCmd

    # "Tampered control file": learn.json writable for Users -> ignored with error event, allowlist applies.
    $now = (Get-Date).ToUniversalTime()
    Write-LearnState $now $now.AddMinutes(30)
    icacls $LearnPath /grant '*S-1-5-32-545:(M)' | Out-Null
    $app = Open-TestApp $op
    if ($app) {
        $t = $app.Since
        Invoke-TestAppButton $app 'Start command prompt'
        $row = Get-TestAppRow $app 'Child process (cmd.exe)'
        Check 'Tampered control file: learning mode ignored, error event 1207, cmd.exe ended' (
            (Get-AppStartEvents 1207 $t).Count -ge 1 -and $row -like 'Ended immediately by AppStart*') $row
    } else { Check 'Tampered control file: TestApp started' $false }
    Close-TestApp $app
    Stop-OperatorCmd
    $r = Invoke-Setup @('learn', 'stop', '--app', $AppId) -Quiet
    $v = Invoke-Setup @('verify') -Quiet
    Check 'Cleanup: learn stop protects learn.json again, verify exit code 0' ($r.Exit -eq 0 -and $v.Exit -eq 0) "verify $($v.Exit)"
}
catch {
    Check 'Test run without exception' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    Stop-OperatorCmd
    if ($helperBackup -and (Test-Path $helperBackup) -and (Test-Path $Helper)) { Copy-Item $helperBackup $Helper -Force -ErrorAction SilentlyContinue }
    Remove-Item 'C:\Users\Public\AppStartE2E-Helper.exe' -Force -ErrorAction SilentlyContinue
    Invoke-E2ECleanup
    Write-Summary
    Stop-Transcript | Out-Null
}
