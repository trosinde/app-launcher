#Requires -RunAsAdministrator
<#
  End-to-end test of AppStart.Service on a Windows test machine (sections 4 and pipe-security).
  Installs the service from <StageDir>, runs the scenarios, then removes everything again.
    StageDir must contain: Service\ (published AppStart.Service), AppStart.TestApp.exe,
    Prepare-TestVm.ps1, Remove-SpikeSetup.ps1
  A second, NON-elevated process can run the "outside group" check while this script waits:
    it waits for <StageDir>\service-ready.txt and answers in <StageDir>\nonadmin-result.txt.
  Output: <StageDir>\e2e-run.txt and screenshot e2e-testapp.png
#>
param(
    [string] $StageDir = $PSScriptRoot,
    # Optional: install through the real MSI (Velopack hook registers the service) instead of sc.exe.
    [string] $MsiPath
)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $StageDir 'e2e-run.txt') -Force | Out-Null
Remove-Item (Join-Path $StageDir 'service-ready.txt'), (Join-Path $StageDir 'nonadmin-result.txt') -ErrorAction SilentlyContinue

$prog = if ($MsiPath) { 'C:\Program Files\AppStart\current' } else { 'C:\Program Files\AppStart' }
$data = Join-Path $env:ProgramData 'AppStart'
$config = Join-Path $data 'apps.json'
$results = [System.Collections.Generic.List[string]]::new()

function Check([string] $name, [bool] $ok, [string] $detail = '') {
    $line = "{0}  {1}  {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail
    $results.Add($line); Write-Host $line
}

function Send-Request([string] $message) {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'AppStart', [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::None, [System.Security.Principal.TokenImpersonationLevel]::Identification)
    try {
        $client.Connect(3000)
        $client.ReadMode = [System.IO.Pipes.PipeTransmissionMode]::Message
        $bytes = [System.Text.Encoding]::ASCII.GetBytes($message)
        $client.Write($bytes, 0, $bytes.Length); $client.Flush()
        $buf = New-Object byte[] 64
        $task = $client.ReadAsync($buf, 0, 64)
        if (-not $task.Wait(8000)) { return '<timeout>' }
        if ($task.Result -eq 0) { return '<closed>' }
        return [System.Text.Encoding]::ASCII.GetString($buf, 0, $task.Result)
    } finally { $client.Dispose() }
}

function Write-Config([string] $sha) {
    $json = @{ version = 1; apps = @(@{
        id = 'testapp'; name = 'AppStart TestApp'; exe = "$prog\TestApp\AppStart.TestApp.exe"; workingDir = "$prog\TestApp"
        check = 'sha256'; signerThumbprint = $null; sha256 = $sha; allowChildProcesses = $false
        dataDir = 'C:\ProgramData\AppStartTest'; registryKey = 'HKLM\SOFTWARE\AppStartTest' }) } | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText($config, $json)
}

function Protect([string] $path, [bool] $dir) {
    # Only SYSTEM and Administrators, owner Administrators, no inheritance (concept: apps.json).
    $sddl = if ($dir) { 'O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)' } else { 'O:BAG:SYD:PAI(A;;FA;;;SY)(A;;FA;;;BA)' }
    $acl = if ($dir) { New-Object System.Security.AccessControl.DirectorySecurity } else { New-Object System.Security.AccessControl.FileSecurity }
    $acl.SetSecurityDescriptorSddlForm($sddl)
    Set-Acl -Path $path -AclObject $acl
}

function New-Shortcut([string] $name, [string] $appId) {
    # Like AppStart.Setup will do: target = launcher, argument --app, icon from the application EXE.
    $lnk = Join-Path $StageDir "$name.lnk"
    $sh = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    $sh.TargetPath = "$prog\AppStart.Launcher.exe"; $sh.Arguments = "--app $appId"
    $sh.IconLocation = "$prog\TestApp\AppStart.TestApp.exe,0"; $sh.WorkingDirectory = "$prog\TestApp"; $sh.Save()
    return $lnk
}

function Invoke-Launcher([string] $lnk, [string] $shot) {
    # Starts the shortcut, returns @{ Exit; Title; Texts }. Captures and closes a dialog if one appears.
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
    $UIA = [System.Windows.Automation.AutomationElement]
    $before = @(Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-Process $lnk
    $proc = $null; for ($i = 0; $i -lt 20 -and -not $proc; $i++) { Start-Sleep -Milliseconds 250; $proc = Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } | Select-Object -First 1 }
    $result = @{ Exit = $null; Title = ''; Texts = '' }
    if (-not $proc) { $result.Exit = 'gone'; return $result }   # finished before we saw it: success path
    $null = $proc.Handle   # Windows PowerShell keeps the exit code only if the handle was opened while running
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ProcessIdProperty, $proc.Id)
    $w = $null; for ($i = 0; $i -lt 40 -and -not $w -and -not $proc.HasExited; $i++) { Start-Sleep -Milliseconds 250; $w = $UIA::RootElement.FindFirst('Children', $cond) }
    if ($w) {
        Start-Sleep -Milliseconds 500
        $result.Title = $w.Current.Name
        $result.Texts = ($w.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique) -join ' | '
        $r = $w.Current.BoundingRectangle; $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $StageDir $shot)); $g.Dispose(); $bmp.Dispose()
        $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    }
    $proc.WaitForExit(10000) | Out-Null
    $result.Exit = $proc.ExitCode
    return $result
}

try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), session $((Get-Process -Id $PID).SessionId)"
    & (Join-Path $StageDir 'Prepare-TestVm.ps1') | Out-Null

    if ($MsiPath) {
        # --- install through the MSI: the Velopack install hook must register and start the service
        $p = Start-Process msiexec.exe -ArgumentList "/i `"$MsiPath`" /qn /l*v `"$(Join-Path $StageDir 'msi-install.log')`"" -Wait -PassThru
        Check 'MSI installed per machine' ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
        Start-Sleep -Seconds 2
        $svc = Get-CimInstance Win32_Service -Filter "Name='AppStart'"
        Check 'Hook: service registered, starts automatically, LocalSystem' ($svc -and $svc.StartMode -eq 'Auto' -and $svc.StartName -eq 'LocalSystem') "$($svc.StartMode) $($svc.StartName) $($svc.PathName)"
        $users = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
        $rules = (Get-Acl 'C:\Program Files\AppStart').GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])
        $writable = $rules | Where-Object {
            $_.AccessControlType -eq 'Allow' -and $_.IdentityReference -eq $users -and
            # WriteData|AppendData|DeleteChild|Delete|WRITE_DAC|WRITE_OWNER|GENERIC_ALL|GENERIC_WRITE (read bits do not count)
            ([int64]$_.FileSystemRights -band 0x500D0046)
        }
        Check 'Program folder not writable by users' (-not $writable) ''
        Check 'TestApp in the package' (Test-Path "$prog\TestApp\AppStart.TestApp.exe") ''
        Stop-Service AppStart   # restarted below once apps.json exists
    } else {
        # --- install like the MSI hook would
        New-Item -ItemType Directory -Force "$prog\TestApp" | Out-Null
        Copy-Item (Join-Path $StageDir 'Service\*') $prog -Recurse -Force
        Copy-Item (Join-Path $StageDir 'AppStart.TestApp.exe') "$prog\TestApp" -Force
    }
    New-Item -ItemType Directory -Force $data | Out-Null
    Protect $data $true
    $sha = (Get-FileHash "$prog\TestApp\AppStart.TestApp.exe" -Algorithm SHA256).Hash
    Write-Config $sha
    Protect $config $false
    if (-not $MsiPath) {
        sc.exe create AppStart binPath= "`"$prog\AppStart.Service.exe`"" start= demand obj= LocalSystem | Out-Null
    }
    $since = Get-Date
    Start-Service AppStart
    Start-Sleep -Seconds 2
    Check 'Service runs as LocalSystem' ((Get-CimInstance Win32_Service -Filter "Name='AppStart'").State -eq 'Running') (Get-CimInstance Win32_Service -Filter "Name='AppStart'").StartName

    # --- non-elevated client (outside the group) runs now in the other process
    Set-Content (Join-Path $StageDir 'service-ready.txt') 'ready'
    $deadline = (Get-Date).AddSeconds(45)
    while (-not (Test-Path (Join-Path $StageDir 'nonadmin-result.txt')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    $na = if (Test-Path (Join-Path $StageDir 'nonadmin-result.txt')) { (Get-Content (Join-Path $StageDir 'nonadmin-result.txt') -Raw).Trim() } else { '<no result>' }
    Check 'User outside the group is rejected' ($na -like 'DENIED-ACCESS*') $na
    Check 'Operator cannot create an own pipe instance' ($na -like '*CREATE-DENIED*') $na

    # --- start the TestApp through the service
    $r = Send-Request 'START testapp'
    Check 'START testapp -> OK' ($r -eq 'OK') $r
    $r2 = Send-Request 'START testapp'   # immediately: same session is in its cooldown
    Check 'Immediate second request -> busy' ($r2 -like 'DENIED busy *') $r2
    Start-Sleep -Seconds 4
    $p = Get-CimInstance Win32_Process -Filter "Name='AppStart.TestApp.exe'" | Select-Object -First 1
    $owner = if ($p) { (Invoke-CimMethod -InputObject $p -MethodName GetOwner).User } else { '' }
    Check 'TestApp runs as appstart-app in session 1' ($owner -eq 'appstart-app' -and $p.SessionId -eq (Get-Process -Id $PID).SessionId) "owner=$owner session=$($p.SessionId)"


    # UI: cmd.exe must be blocked by the job object
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
    $UIA = [System.Windows.Automation.AutomationElement]
    $w = $UIA::RootElement.FindFirst('Children', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'AppStart TestApp')))
    if ($w) {
        $btn = $w.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'Start command prompt')))
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Seconds 1
        $texts = ($w.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) -join ' | '
        Check 'Child process cmd.exe blocked' ($texts -like '*Start blocked by job object*') ''
        Check 'TestApp: account appstart-app, admin, registry and files OK' ($texts -like '*appstart-app | OK | Admin rights | Yes | OK | Read/write registry | HKLM\SOFTWARE\AppStartTest | OK | Read/write files | C:\ProgramData\AppStartTest | OK*') ''
        $rect = $w.Current.BoundingRectangle; $bmp = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $StageDir 'e2e-testapp.png')); $g.Dispose(); $bmp.Dispose()
        $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    } else { Check 'TestApp window found' $false }

    Start-Sleep -Seconds 3
    $r = Send-Request 'START other-app'
    Check 'Unknown AppId -> unknown-app' ($r -like 'DENIED unknown-app *') $r

    # A client that connects and leaves without a request must not take the service down.
    $c = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'AppStart', [System.IO.Pipes.PipeDirection]::InOut)
    $c.Connect(3000); $c.Dispose()
    $c = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'AppStart', [System.IO.Pipes.PipeDirection]::InOut)
    $c.Connect(3000); Start-Sleep -Seconds 3; $c.Dispose()   # read timeout on the server side
    Start-Sleep -Seconds 1
    Check 'Service keeps running after aborted connections' ((Get-Service AppStart).Status -eq 'Running') ''

    Start-Sleep -Seconds 3
    $r = Send-Request 'START testapp C:\Windows\System32\cmd.exe'
    Check 'Invalid request is discarded without response' ($r -eq '<closed>') $r

    # --- launcher through a real shortcut (title = shortcut name)
    Get-Process AppStart.TestApp -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3
    $lnk = New-Shortcut 'AppStart TestApp' 'testapp'
    $l = Invoke-Launcher $lnk 'e2e-launcher-ok.png'
    Start-Sleep -Seconds 4
    $started = Get-CimInstance Win32_Process -Filter "Name='AppStart.TestApp.exe'" | Select-Object -First 1
    Check 'Launcher via shortcut: start without window' (($l.Exit -eq 0 -or $l.Exit -eq 'gone') -and -not $l.Title -and $started) "exit=$($l.Exit) dialog='$($l.Title)'"
    Get-Process AppStart.TestApp -ErrorAction SilentlyContinue | Stop-Process -Force

    Start-Sleep -Seconds 3
    Write-Config ('0' * 64); Protect $config $false
    $l = Invoke-Launcher $lnk 'e2e-launcher-denied.png'
    Check 'Launcher: "Launch denied" with event ID, title = shortcut' ($l.Exit -eq 3 -and $l.Title -eq 'AppStart TestApp' -and $l.Texts -like '*Launch denied*' -and $l.Texts -like '*Event ID*') "exit=$($l.Exit) title='$($l.Title)' :: $($l.Texts)"

    Start-Sleep -Seconds 3
    $r = Send-Request 'START testapp'
    Check 'Wrong checksum -> integrity' ($r -like 'DENIED integrity *') $r
    Check 'Response without path or checksum' ($r -notmatch '\\|[0-9A-F]{64}') $r

    Start-Sleep -Seconds 3
    Write-Config $sha; Protect $config $false
    $acl = Get-Acl $config
    $users = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($users, 'Write', 'Allow')))
    Set-Acl $config $acl
    $r = Send-Request 'START testapp'
    Check 'apps.json writable by users -> config' ($r -like 'DENIED config *') $r
    Protect $config $false

    # --- fake pipe: an existing \\.\pipe\AppStart must stop the service from starting
    Stop-Service AppStart; Start-Sleep -Seconds 1
    $fake = New-Object System.IO.Pipes.NamedPipeServerStream('AppStart', [System.IO.Pipes.PipeDirection]::InOut, 1)
    try {
        Start-Service AppStart -ErrorAction SilentlyContinue; Start-Sleep -Seconds 3
        $state = (Get-Service AppStart).Status
        Check 'Spoofed pipe: service does not start' ($state -ne 'Running') "$state"
    } finally { $fake.Dispose() }

    # --- event log
    Start-Sleep -Seconds 1
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'AppStart'; StartTime = $since } -ErrorAction SilentlyContinue
    $ids = ($events | ForEach-Object Id | Sort-Object -Unique) -join ','
    Check 'Event Log contains 1000,1100,1200,1201,1202,1203,1204,1300' (@(1000, 1100, 1200, 1201, 1202, 1203, 1204, 1300 | Where-Object { $ids -notmatch "\b$_\b" }).Count -eq 0) $ids
    Write-Host "`n--- Events (newest first) ---"
    $events | Select-Object -First 14 | ForEach-Object { Write-Host ("{0} {1} {2}" -f $_.TimeCreated.ToString('HH:mm:ss'), $_.Id, ($_.Message -replace '\s+', ' ').Substring(0, [Math]::Min(220, ($_.Message -replace '\s+', ' ').Length))) }
}
catch {
    Check 'Test run without exception' $false $_.Exception.Message
}
finally {
    Write-Host "`n================ Cleanup ================"
    Get-Process AppStart.TestApp -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($MsiPath) {
        $product = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' | Get-ItemProperty |
            Where-Object { $_.DisplayName -eq 'AppStart' -and $_.PSChildName -match '^\{' } | Select-Object -First 1
        if ($product) {
            $p = Start-Process msiexec.exe -ArgumentList "/x $($product.PSChildName) /qn" -Wait -PassThru
            Start-Sleep -Seconds 2
            Check 'MSI uninstallation removes service and program folder' ($p.ExitCode -eq 0 -and -not (Get-Service AppStart -ErrorAction SilentlyContinue) -and -not (Test-Path 'C:\Program Files\AppStart\current')) "exit $($p.ExitCode)"
        } else { Check 'MSI product found' $false }
    }
    Stop-Service AppStart -ErrorAction SilentlyContinue
    sc.exe delete AppStart 2>$null | Out-Null
    Start-Sleep -Seconds 1
    Remove-Item 'C:\Program Files\AppStart' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item 'HKLM:\SOFTWARE\AppStartTest' -Recurse -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:ProgramData 'AppStartTest') -Recurse -Force -ErrorAction SilentlyContinue
    if ([System.Diagnostics.EventLog]::SourceExists('AppStart')) { [System.Diagnostics.EventLog]::DeleteEventSource('AppStart') }
    & (Join-Path $StageDir 'Remove-SpikeSetup.ps1') | Out-Null
    Write-Host "Service: $([bool](Get-Service AppStart -ErrorAction SilentlyContinue)), folders: $(Test-Path 'C:\Program Files\AppStart')/$(Test-Path $data), account: $([bool](Get-LocalUser appstart-app -ErrorAction SilentlyContinue))"
    Write-Host "`n================ Result ================"
    $results | ForEach-Object { Write-Host $_ }
    Write-Host ("{0} PASS, {1} FAIL" -f @($results | Where-Object { $_ -like 'PASS*' }).Count, @($results | Where-Object { $_ -like 'FAIL*' }).Count)
    Stop-Transcript | Out-Null
}
