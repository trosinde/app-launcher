#Requires -RunAsAdministrator
<#
  Installation tests (task 8.5). Only on the TEST VM, start from a snapshot without AppStart.
    1. msiexec error 1618 (another installation running), simulated with the Global\_MSIExecute mutex
    2. silent installation as SYSTEM (one-time scheduled task, like software distribution)
    3. setup through the CLI, snapshot of apps.json, accounts, group, shortcuts and ACLs
    4. upgrade with -MsiNextPath over the configured installation: everything identical, service in the
       new version, verify 0, start as appstart-user still works
    5. optional with -BootstrapperPath: 1618 through the bootstrapper, wizard cancel, /quiet install
       (MSI extracted to C:\ProgramData\AppStart\Installer and deleted again, log in ...\Logs)
  The TestApp is released from a copy outside the AppStart package (C:\Program Files\AppStartTestApp),
  like a real fat client: an AppStart update must not change the application EXE.
  Output: <StageDir>\install-run.txt, MSI logs install-*.log
#>
param(
    [Parameter(Mandatory)] [string] $MsiPath,
    [Parameter(Mandatory)] [string] $MsiNextPath,
    [string] $BootstrapperPath,
    [string] $StageDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path $MsiPath).Path
$MsiNextPath = (Resolve-Path $MsiNextPath).Path
if ($BootstrapperPath) { $BootstrapperPath = (Resolve-Path $BootstrapperPath).Path }
Start-Transcript -Path (Join-Path $StageDir 'install-run.txt') -Force | Out-Null
. (Join-Path $PSScriptRoot 'E2E.Common.ps1')

$extTestAppDir = 'C:\Program Files\AppStartTestApp'
$extTestApp = Join-Path $extTestAppDir 'AppStart.TestApp.exe'
$installerDir = Join-Path $AppStartData 'Installer'
$logDir = Join-Path $AppStartData 'Logs'
$bootCopy = 'C:\Users\Public\Downloads\AppStart-Setup.exe'
$taskName = 'AppStartE2E-MsiSystem'

function Invoke-WithMsiMutex([scriptblock] $body) {
    # While this mutex is held, Windows Installer treats another installation as running (1618).
    $created = $false
    $m = New-Object System.Threading.Mutex($true, 'Global\_MSIExecute', [ref]$created)
    try {
        if (-not $created) { if (-not $m.WaitOne(0)) { throw 'Global\_MSIExecute is taken, is another installation in progress?' } }
        & $body
    } finally {
        try { $m.ReleaseMutex() } catch { }
        $m.Dispose()
    }
}

function Get-Snapshot {
    # Everything an upgrade must leave untouched (spec installation, "Update keeps the setup").
    $s = [ordered]@{}
    $s['apps.json SHA-256'] = (Get-FileHash $ConfigPath).Hash
    $s['apps.json SDDL'] = Get-Sddl $ConfigPath
    $s['ProgramData\AppStart SDDL'] = Get-Sddl $AppStartData
    $s['Data folder SDDL'] = Get-Sddl $TestDataDir
    $s['Registry key SDDL'] = Get-Sddl $TestRegKey
    $s['TestApp SHA-256'] = (Get-FileHash $extTestApp).Hash
    foreach ($u in 'appstart-app', 'appstart-user') {
        $lu = Get-LocalUser $u -ErrorAction SilentlyContinue
        $s["Account $u"] = if ($lu) { "$($lu.SID) enabled=$($lu.Enabled) pwdLastSet=$($lu.PasswordLastSet.ToString('s'))" } else { '<missing>' }
    }
    $g = Get-LocalGroup 'AppStart Users' -ErrorAction SilentlyContinue
    $s['Group AppStart Users'] = if ($g) { "$($g.SID) members=" + ((@(Get-LocalGroupMember -SID $g.SID.Value | ForEach-Object { $_.SID.Value }) | Sort-Object) -join ',') } else { '<missing>' }
    # net localgroup instead of Get-LocalGroupMember: the latter fails on orphaned SIDs in Administrators
    $s['Administrators contains appstart-app'] = [string][bool](@(net localgroup (New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')).Translate([System.Security.Principal.NTAccount]).Value.Split('\')[-1]) -contains 'appstart-app')
    foreach ($l in @(Get-LauncherShortcuts | Sort-Object Path)) {
        $s["Shortcut $($l.Path)"] = "$($l.Target) $($l.Arguments) icon=$($l.Icon) wd=$($l.WorkDir) sddl=$(Get-Sddl $l.Path)"
    }
    return $s
}

function Test-OperatorStart([pscredential] $cred, [string] $appId, [string] $label) {
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $cred "--app $appId" ''
    $apps = @(Wait-TestApp $t)
    Check $label ($l.Exit -eq 0 -and $apps.Count -ge 1) "exit=$($l.Exit) owner=$($apps[0].Owner) $($l.Texts)"
    Stop-TestApps
}

function Test-ChildCmd([pscredential] $cred, [string] $appId, [string] $label, [string] $expectedRow, [int] $expectedCmd) {
    # TestApp as the operator, "Start command prompt": row text and running cmd.exe of appstart-app.
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $cred "--app $appId" ''
    $apps = @(Wait-TestApp $t)
    $win = if ($apps) { Wait-ProcessWindow $apps[0].Id 20 } else { $null }
    if (-not $win) { Check $label $false "exit=$($l.Exit) no TestApp window"; Stop-TestApps; return }
    Start-Sleep -Seconds 2
    $app = @{ Window = $win }
    Invoke-TestAppButton $app 'Start command prompt'
    $row = Get-TestAppRow $app 'Child process (cmd.exe)'
    $cmdCount = @(Get-OperatorCmd).Count
    Check $label ($row -like $expectedRow -and $cmdCount -eq $expectedCmd) "$row, cmd.exe as appstart-app: $cmdCount"
    Stop-OperatorCmd
    Stop-TestApps
}

function Set-ConfigV1AllowChildren([bool] $allow) {
    # As an admin edits version 1 by hand: content written in place, the ACL of apps.json stays.
    $cfg = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($a in @($cfg.apps)) { $a | Add-Member -NotePropertyName allowChildProcesses -NotePropertyValue $allow -Force }
    [IO.File]::WriteAllText($ConfigPath, ($cfg | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
}

function Get-ConfigApp([string] $appId) {
    $cfg = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return @{ Version = $cfg.version; App = (@($cfg.apps) | Where-Object { $_.id -eq $appId } | Select-Object -First 1) }
}

function Get-MsiexecCommandLines {
    return @(Get-CimInstance Win32_Process -Filter "Name='msiexec.exe'" | ForEach-Object { $_.CommandLine } | Where-Object { $_ -match '\.msi' })
}

try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), MSI $MsiPath -> $MsiNextPath"
    if (Get-AppStartProduct) { throw 'AppStart is already installed. Run the test only on a fresh test VM (snapshot).' }
    Initialize-WorkDir
    New-TestAppPrerequisites
    $v1 = Get-MsiProperty $MsiPath 'ProductVersion'
    $v2 = Get-MsiProperty $MsiNextPath 'ProductVersion'
    $newer = try { [version]$v2 -gt [version]$v1 } catch { $v2 -ne $v1 }
    Check 'MSI versions: second MSI is newer' $newer "$v1 -> $v2"
    # Local copies: SYSTEM must be able to read the MSI (no mapped drive, no user profile).
    $msiLocal = Join-Path $WorkDir 'AppStart.msi'
    Copy-Item $MsiPath $msiLocal -Force

    # ------------------------------------------------------------ 1. msiexec 1618
    $log1618 = Join-Path $StageDir 'install-1618.log'
    $code = Invoke-WithMsiMutex { Invoke-Msiexec "/i `"$msiLocal`" /qn /l*v `"$log1618`"" 180 }
    if ($code -eq 1618) {
        Check 'msiexec during a running installation: exit code 1618, nothing installed' (-not (Get-AppStartProduct) -and -not (Get-Service AppStart -ErrorAction SilentlyContinue)) "exit $code"
    } else {
        Skip 'msiexec 1618 with held _MSIExecute mutex not reproducible' "exit $code (see README, check manually)"
        if (Get-AppStartProduct) { Uninstall-AppStartMsi | Out-Null }
    }

    # ------------------------------------------------------------ 2. silent install as SYSTEM
    $logSystem = Join-Path $WorkDir 'install-system.log'
    $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\msiexec.exe" -Argument "/i `"$msiLocal`" /qn /l*v `"$logSystem`""
    $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
    Wait-MsiIdle
    $mySession = (Get-Process -Id $PID).SessionId
    $windowsBefore = @(Get-Process | Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object Id)
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddMinutes(10)
    do { Start-Sleep -Seconds 2; $info = Get-ScheduledTaskInfo -TaskName $taskName; $state = (Get-ScheduledTask -TaskName $taskName).State }
    while (($state -eq 'Running' -or $info.LastTaskResult -eq 267011 -or $info.LastTaskResult -eq 267009) -and (Get-Date) -lt $deadline)
    $newWindows = @(Get-Process | Where-Object { $_.SessionId -eq $mySession -and $_.MainWindowHandle -ne 0 -and $windowsBefore -notcontains $_.Id -and $_.ProcessName -match 'AppStart|msiexec' } | ForEach-Object ProcessName)
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Copy-Item $logSystem (Join-Path $StageDir 'install-system.log') -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    $svc = Get-CimInstance Win32_Service -Filter "Name='AppStart'"
    Check 'Silent installation as SYSTEM: exit code 0' ($info.LastTaskResult -eq 0) "LastTaskResult=$($info.LastTaskResult)"
    Check 'Silent installation as SYSTEM: installed in the program folder' ((Test-Path $SetupExe) -and (Test-Path $LauncherExe)) $CurDir
    Check 'Silent installation as SYSTEM: service running, automatic, LocalSystem' ($svc -and $svc.State -eq 'Running' -and $svc.StartMode -eq 'Auto' -and $svc.StartName -eq 'LocalSystem') "$($svc.State) $($svc.StartMode) $($svc.StartName)"
    Check 'Silent installation as SYSTEM: no user interface in the user session' ($newWindows.Count -eq 0) ($newWindows -join ',')
    Check 'Silent installation as SYSTEM: Start menu "AppStart – Setup" created' (Test-Path "$StartMenuDir\AppStart – Setup.lnk") ''

    # ------------------------------------------------------------ 3. setup through the CLI
    New-Item -ItemType Directory -Force $extTestAppDir | Out-Null
    Copy-Item (Join-Path $CurDir 'TestApp\*') $extTestAppDir -Recurse -Force
    $r = Invoke-Setup @('add', '--exe', $extTestApp, '--data-dir', $TestDataDir, '--reg-key', $TestRegKeyCli, '--name', $TestAppName)
    Check 'Setup: TestApp (outside the package) approved' ($r.Exit -eq 0) ''
    $password = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
    Check 'Setup: appstart-user created' ($r.Exit -eq 0) ''
    $op = New-OperatorCredential 'appstart-user' $password
    $appId = Get-TestAppId $extTestApp
    $r = Invoke-Setup @('verify')
    Check 'Setup: verify exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Test-OperatorStart $op $appId 'Setup: start as appstart-user works'

    $before = Get-Snapshot
    $packagedHashBefore = (Get-FileHash $PackagedTestApp).Hash
    $svcExeBefore = (Get-Item (Join-Path $CurDir 'AppStart.Service.exe')).VersionInfo.ProductVersion
    $before.GetEnumerator() | ForEach-Object { Write-Host "  vorher  $($_.Key): $($_.Value)" }

    # ------------------------------------------------------------ 4. upgrade over the configured installation
    $tUpgrade = Get-Date
    $code = Install-AppStartMsi $MsiNextPath (Join-Path $StageDir 'install-upgrade.log')
    Check 'Update: MSI of the newer version installed' ($code -eq 0 -or $code -eq 3010) "exit $code"
    Start-Sleep -Seconds 3
    $products = @(Get-AppStartProduct)
    Check 'Update: exactly one product, version of the new MSI' ($products.Count -eq 1 -and $products[0].DisplayVersion -eq $v2) (($products | ForEach-Object { "$($_.PSChildName) $($_.DisplayVersion)" }) -join '; ')
    $svc = Get-CimInstance Win32_Service -Filter "Name='AppStart'"
    $svcProc = if ($svc -and $svc.ProcessId) { Get-Process -Id $svc.ProcessId -ErrorAction SilentlyContinue } else { $null }
    $svcExeAfter = (Get-Item (Join-Path $CurDir 'AppStart.Service.exe')).VersionInfo.ProductVersion
    Check 'Update: service runs in the new version (restarted, new file)' ($svc.State -eq 'Running' -and $svcProc -and $svcProc.StartTime -gt $tUpgrade -and $svcExeAfter -ne $svcExeBefore -and $svc.StartMode -eq 'Auto' -and $svc.StartName -eq 'LocalSystem') "$svcExeBefore -> $svcExeAfter, Pfad $($svc.PathName)"

    $after = Get-Snapshot
    $diff = @(foreach ($k in @($before.Keys) + @($after.Keys | Where-Object { -not $before.Contains($_) })) {
        if ($before[$k] -ne $after[$k]) { "$k : '$($before[$k])' -> '$($after[$k])'" }
    })
    $diff | ForEach-Object { Write-Host "  DEVIATION $_" }
    Check 'Update: apps.json, accounts, group, shortcuts and permissions unchanged' ($diff.Count -eq 0) "$($diff.Count) deviation(s)"
    Check 'Update: Start menu entries of AppStart present again' ((Test-Path "$StartMenuDir\AppStart – Setup.lnk") -and (Test-Path "$StartMenuDir\AppStart – Verify setup.lnk")) ''
    $r = Invoke-Setup @('verify')
    Check 'Update: verify exit code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Test-OperatorStart $op $appId 'Update: start as appstart-user still works'
    # Information: would a TestApp released from inside the package survive the update?
    $packagedHashAfter = (Get-FileHash $PackagedTestApp).Hash
    Write-Host "  INFO TestApp in the package after update $(if ($packagedHashAfter -eq $packagedHashBefore) { 'unchanged' } else { 'CHANGED: a TestApp approved from the package needs rehash after every update' })"

    # ------------------------------------------------------------ 4b. apps.json version 1 after the update
    # add-allowed-child-processes 7.2: app-launch "Configuration version 1", installation "Update of an existing
    # installation" (false -> blocked, true -> all, protection unchanged). Only when the first MSI
    # still writes version 1.
    $c = Get-ConfigApp $appId
    if ($c.Version -eq 1) {
        $sddlV1 = Get-Sddl $ConfigPath
        Test-ChildCmd $op $appId 'Version 1, allowChildProcesses false: service starts as blocked, cmd.exe blocked' 'Start blocked by job object*' 0
        $r = Invoke-Setup @('rehash', '--app', $appId)
        $c = Get-ConfigApp $appId
        Check 'Migration false: first change writes version 2 with childProcesses blocked, without allowChildProcesses' (
            $r.Exit -eq 0 -and $c.Version -eq 2 -and $c.App.childProcesses -eq 'blocked' -and -not ($c.App.PSObject.Properties.Name -contains 'allowChildProcesses')) "exit $($r.Exit), version $($c.Version), $($c.App.childProcesses)"
        Check 'Migration false: permissions of apps.json unchanged' ((Get-Sddl $ConfigPath) -eq $sddlV1) ''
        Test-ChildCmd $op $appId 'Migration false: cmd.exe still blocked' 'Start blocked by job object*' 0

        # Back to version 1 with allowChildProcesses true (hand edit of an existing installation).
        $cfg = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $cfg.version = 1
        foreach ($a in @($cfg.apps)) { $a.PSObject.Properties.Remove('childProcesses'); $a.PSObject.Properties.Remove('allowedChildren') }
        [IO.File]::WriteAllText($ConfigPath, ($cfg | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
        Set-ConfigV1AllowChildren $true
        Test-ChildCmd $op $appId 'Version 1, allowChildProcesses true: service starts as all, cmd.exe runs' 'Started*' 1
        $r = Invoke-Setup @('rehash', '--app', $appId)
        $c = Get-ConfigApp $appId
        Check 'Migration true: first change writes version 2 with childProcesses all' (
            $r.Exit -eq 0 -and $c.Version -eq 2 -and $c.App.childProcesses -eq 'all' -and -not ($c.App.PSObject.Properties.Name -contains 'allowChildProcesses')) "exit $($r.Exit), version $($c.Version), $($c.App.childProcesses)"
        Check 'Migration true: permissions of apps.json unchanged' ((Get-Sddl $ConfigPath) -eq $sddlV1) ''
        Test-ChildCmd $op $appId 'Migration true: cmd.exe still runs (all)' 'Started*' 1

        $r = Invoke-Setup @('set-children', '--app', $appId, '--mode', 'blocked')
        Check 'After the migration: back to blocked, verify exit code 0' ($r.Exit -eq 0 -and (Get-ConfigApp $appId).App.childProcesses -eq 'blocked') "exit $($r.Exit)"
    } else {
        Skip 'apps.json version 1 after update' "first MSI writes version $($c.Version)"
    }

    # ------------------------------------------------------------ 5. bootstrapper (optional)
    if ($BootstrapperPath) {
        Invoke-Setup @('uninstall') | Out-Null
        Uninstall-AppStartMsi | Out-Null
        Check 'Bootstrapper preparation: AppStart completely removed' (-not (Get-AppStartProduct) -and -not (Get-Service AppStart -ErrorAction SilentlyContinue)) ''
        # From a folder operators can write (spec: "Start from a writable folder")
        Copy-Item $BootstrapperPath $bootCopy -Force

        # 5a. 1618 through the bootstrapper: error, log, no wizard
        Wait-MsiIdle
        $t = Get-Date
        $res = Invoke-WithMsiMutex {
            $p = Start-Process $bootCopy -ArgumentList '/quiet' -WorkingDirectory (Split-Path $bootCopy) -PassThru
            Wait-ProcessHandlingDialogs $p 240 'install-boot-1618.png'
        }
        $logs = @(Get-ChildItem $logDir -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $t })
        $wizard = @(Get-Process AppStart.Setup -ErrorAction SilentlyContinue)
        if ($res.Exit -eq 0 -and (Get-AppStartProduct)) {
            Skip 'Bootstrapper during a running installation (1618) not reproducible' 'mutex has no effect, see README'
            Uninstall-AppStartMsi | Out-Null
        } else {
            Check 'Bootstrapper during a running installation: exit code not 0, nothing installed' ($res.Exit -ne 0 -and -not $res.TimedOut -and -not (Get-AppStartProduct)) "exit=$($res.Exit) timeout=$($res.TimedOut) :: $($res.Texts)"
            Check 'Bootstrapper during a running installation: MSI log in C:\ProgramData\AppStart\Logs' ($logs.Count -ge 1) (($logs | ForEach-Object Name) -join ', ')
            Check 'Bootstrapper during a running installation: wizard not started' ($wizard.Count -eq 0) ''
        }
        Get-Process AppStart.Setup -ErrorAction SilentlyContinue | Stop-Process -Force

        # 5b. interactive start, cancel in the wizard
        $p = Start-Process $bootCopy -WorkingDirectory (Split-Path $bootCopy) -PassThru
        $wizardProc = $null; $deadline = (Get-Date).AddMinutes(5)
        while (-not $wizardProc -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1; $wizardProc = Get-Process AppStart.Setup -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
        if ($wizardProc) {
            $wizardPath = $wizardProc.Path
            Check 'Bootstrapper interactive: wizard starts from C:\Program Files\AppStart' ($wizardPath -like "$ProgDir\*") $wizardPath
            # Cancel: close the window, confirm a possible question
            for ($i = 0; $i -lt 3 -and -not $wizardProc.HasExited; $i++) {
                foreach ($w in Get-ProcessWindows $wizardProc.Id) {
                    $yes = @('Yes', 'Ja') | ForEach-Object { $w.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $_))) } | Where-Object { $_ } | Select-Object -First 1 # MessageBox buttons follow the Windows language
                    if ($yes) { try { $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } } else { Close-UiWindow $w }
                }
                $wizardProc.WaitForExit(5000) | Out-Null
            }
            if ($wizardProc.HasExited) {
                $p.WaitForExit(30000) | Out-Null
                $r = Invoke-Setup @('verify')
                Check 'Cancel in the wizard: AppStart stays installed, service running' ((Get-AppStartProduct) -and (Get-Service AppStart).Status -eq 'Running') ''
                Check 'Cancel in the wizard: wizard can be started again from the Start menu' (Test-Path "$StartMenuDir\AppStart – Setup.lnk") ''
                Check 'Cancel in the wizard: verify reports missing setup (exit code not 0)' ($r.Exit -ne 0) "exit $($r.Exit)"
            } else {
                Skip 'Cancel in the wizard not possible automatically' 'wizard could not be closed via UI Automation, see MANUAL-TESTS.md'
            }
        } else {
            Check 'Bootstrapper interactive: wizard appears' $false 'no window of AppStart.Setup after 5 minutes'
        }
        Get-Process AppStart.Setup -ErrorAction SilentlyContinue | Stop-Process -Force
        if (-not $p.HasExited) { $p.Kill() }
        Uninstall-AppStartMsi | Out-Null

        # 5c. /quiet
        Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue
        $t = Get-Date
        $p = Start-Process $bootCopy -ArgumentList '/quiet' -WorkingDirectory (Split-Path $bootCopy) -PassThru
        $null = $p.Handle
        $seenMsi = [System.Collections.Generic.HashSet[string]]::new()
        $seenCmd = [System.Collections.Generic.HashSet[string]]::new()
        $deadline = (Get-Date).AddMinutes(10)
        while (-not $p.HasExited -and (Get-Date) -lt $deadline) {
            Get-ChildItem $installerDir -Filter '*.msi' -ErrorAction SilentlyContinue | ForEach-Object { [void]$seenMsi.Add($_.FullName) }
            Get-MsiexecCommandLines | ForEach-Object { [void]$seenCmd.Add($_) }
            Start-Sleep -Milliseconds 200
        }
        if (-not $p.HasExited) { $p.Kill() }
        $p.WaitForExit(10000) | Out-Null
        Start-Sleep -Seconds 2
        $seenCmd | ForEach-Object { Write-Host "  msiexec: $_" }
        $svc = Get-CimInstance Win32_Service -Filter "Name='AppStart'"
        Check 'Bootstrapper /quiet: exit code 0, installed, service running' ($p.ExitCode -eq 0 -and (Get-AppStartProduct) -and $svc.State -eq 'Running') "exit $($p.ExitCode)"
        $usedInstallerDir = @($seenMsi).Count -gt 0 -or @($seenCmd | Where-Object { $_ -like "*$installerDir*" }).Count -gt 0
        Check 'Bootstrapper /quiet: MSI extracted to C:\ProgramData\AppStart\Installer' $usedInstallerDir ((@($seenMsi) + @($seenCmd)) -join ' | ')
        $unsafe = @($seenCmd | Where-Object { $_ -like '*\Downloads\*' -or $_ -like "*$env:TEMP*" -or $_ -like '*\AppData\Local\Temp\*' })
        Check 'Bootstrapper /quiet: no MSI from Downloads or %TEMP%' ($unsafe.Count -eq 0) ($unsafe -join ' | ')
        Check 'Bootstrapper /quiet: extracted MSI deleted afterwards' (@(Get-ChildItem $installerDir -Filter '*.msi' -ErrorAction SilentlyContinue).Count -eq 0) ''
        $logs = @(Get-ChildItem $logDir -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $t })
        Check 'Bootstrapper /quiet: Log in C:\ProgramData\AppStart\Logs' ($logs.Count -ge 1) (($logs | ForEach-Object Name) -join ', ')
        Check 'Bootstrapper /quiet: no wizard started' (@(Get-Process AppStart.Setup -ErrorAction SilentlyContinue).Count -eq 0) ''
        if ($logs) {
            $acl = (Get-Acl $logDir).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) | Where-Object {
                $_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -in 'S-1-5-32-545', 'S-1-5-11', 'S-1-1-0' -and ([int64]$_.FileSystemRights -band 0x500D0046) }
            Check 'Bootstrapper: Logs folder not writable by users' (-not $acl) ''
        }
    }
}
catch {
    Check 'Test run without exception' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
    Invoke-E2ECleanup -extraPaths @($extTestAppDir, $bootCopy)
    Write-Summary
    Stop-Transcript | Out-Null
}
