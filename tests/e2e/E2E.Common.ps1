<#
  Shared helpers of the acceptance, installation and operations tests (tasks 8.2-8.5).
  Dot-sourced by Invoke-AcceptanceTest.ps1, Invoke-InstallTests.ps1 and Invoke-OperationsTests.ps1.
  Same conventions as Invoke-ServiceE2E.ps1 / Invoke-SetupE2E.ps1: one PASS/FAIL line per check,
  summary "N PASS, M FAIL" at the end, everything removed again in finally.
#>

$ProgDir = 'C:\Program Files\AppStart'
$CurDir = Join-Path $ProgDir 'current'
$SetupExe = Join-Path $CurDir 'AppStart.Setup.exe'
$LauncherExe = Join-Path $CurDir 'AppStart.Launcher.exe'
$PackagedTestApp = Join-Path $CurDir 'TestApp\AppStart.TestApp.exe'
$AppStartData = Join-Path $env:ProgramData 'AppStart'
$ConfigPath = Join-Path $AppStartData 'apps.json'
$TestDataDir = 'C:\ProgramData\AppStartTest'
$TestRegKey = 'HKLM:\SOFTWARE\AppStartTest'
$TestRegKeyCli = 'HKLM\SOFTWARE\AppStartTest'
$TestAppName = 'AppStart TestApp'
$StartMenuDir = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'AppStart'
# Scratch folder readable for operators (probe script) with a writable subfolder for their results.
$WorkDir = Join-Path $env:ProgramData 'AppStartE2E'
$WorkOut = Join-Path $WorkDir 'out'

$results = [System.Collections.Generic.List[string]]::new()

function Check([string] $name, [bool] $ok, [string] $detail = '') {
    $line = "{0}  {1}  {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail
    $results.Add($line); Write-Host $line
}

# Not counted as PASS or FAIL: a check that cannot be done reliably on this machine (documented in README).
function Skip([string] $name, [string] $detail = '') {
    $line = "SKIP  $name  $detail"
    $results.Add($line); Write-Host $line
}

function Write-Summary {
    Write-Host "`n================ Ergebnis ================"
    $results | ForEach-Object { Write-Host $_ }
    $skips = @($results | Where-Object { $_ -like 'SKIP*' }).Count
    $line = "{0} PASS, {1} FAIL" -f @($results | Where-Object { $_ -like 'PASS*' }).Count, @($results | Where-Object { $_ -like 'FAIL*' }).Count
    if ($skips) { $line += ", $skips SKIP" }
    Write-Host $line
}

function New-RandomPassword {
    # Fresh per run, never stored in the repository. Complexity: upper, lower, digit, symbol.
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x') + 'aA1!'
}

function New-OperatorCredential([string] $user, [string] $password) {
    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    return New-Object System.Management.Automation.PSCredential("$env:COMPUTERNAME\$user", $secure)
}

function New-LocalTestUser([string] $user, [string] $password) {
    # Plain standard user (Users only), e.g. the second operator. Not created by AppStart.Setup.
    New-LocalUser -Name $user -Password (ConvertTo-SecureString $password -AsPlainText -Force) -PasswordNeverExpires `
        -Description 'AppStart e2e test user' | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $user
}

function Initialize-WorkDir {
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $WorkOut | Out-Null
    # Root: SYSTEM/Admins full, Users read (probe script). out\: Users modify (result files).
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetSecurityDescriptorSddlForm('O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)')
    Set-Acl -Path $WorkDir -AclObject $acl
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetSecurityDescriptorSddlForm('O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;BU)')
    Set-Acl -Path $WorkOut -AclObject $acl
    Copy-Item (Join-Path $PSScriptRoot 'Invoke-OperatorProbe.ps1') $WorkDir -Force
}

function Start-OperatorProbe([pscredential] $Credential, [string] $Action, [string] $Argument = '') {
    # Starts the probe as the operator (own logon, standard user token). Returns @{ Process; Out }.
    $outFile = Join-Path $WorkOut ("{0}-{1}.txt" -f $Action, [guid]::NewGuid().ToString('N').Substring(0, 8))
    $argList = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$(Join-Path $WorkDir 'Invoke-OperatorProbe.ps1')`" -Action $Action -ResultFile `"$outFile`""
    if ($Argument) { $argList += " -Argument `"$Argument`"" }
    $p = Start-Process powershell.exe -Credential $Credential -LoadUserProfile -WorkingDirectory $WorkDir `
        -WindowStyle Hidden -ArgumentList $argList -PassThru
    $null = $p.Handle
    return @{ Process = $p; Out = $outFile }
}

function Complete-OperatorProbe($probe, [int] $timeoutSec = 150) {
    if (-not $probe.Process.WaitForExit($timeoutSec * 1000)) { try { $probe.Process.Kill() } catch { }; return '<timeout>' }
    if (Test-Path $probe.Out) { return (Get-Content $probe.Out -Raw -Encoding UTF8).Trim() }
    return "<kein Ergebnis, exit $($probe.Process.ExitCode)>"
}

function Invoke-AsOperator([pscredential] $Credential, [string] $Action, [string] $Argument = '') {
    $probe = Start-OperatorProbe $Credential $Action $Argument
    $r = Complete-OperatorProbe $probe
    Write-Host "  [$($Credential.UserName)] $Action -> $r"
    return $r
}

function Invoke-Setup([string[]] $Arguments, [string] $StdIn = $null, [switch] $Quiet) {
    # Runs elevated already. Password goes through a pipe on stdin, never into a file or the transcript.
    $psi = New-Object System.Diagnostics.ProcessStartInfo($SetupExe)
    $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8; $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.RedirectStandardInput = [bool]$StdIn
    $psi.WorkingDirectory = $CurDir
    $p = [System.Diagnostics.Process]::Start($psi)
    $o = $p.StandardOutput.ReadToEndAsync(); $e = $p.StandardError.ReadToEndAsync()
    if ($StdIn) { $p.StandardInput.WriteLine($StdIn); $p.StandardInput.Close() }
    if (-not $p.WaitForExit(300000)) { try { $p.Kill() } catch { } }
    $p.WaitForExit()
    $text = ($o.Result + $e.Result).Trim()
    if (-not $Quiet) {
        Write-Host "  > AppStart.Setup.exe $($Arguments -join ' ')  (exit $($p.ExitCode))"
        if ($text) { $text -split "`r?`n" | ForEach-Object { Write-Host "    $_" } }
    }
    return @{ Exit = $p.ExitCode; Text = $text }
}

function Get-AppStartProduct {
    Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Get-ItemProperty | Where-Object { $_.DisplayName -eq 'AppStart' -and $_.PSChildName -match '^\{' }
}

function Invoke-Msiexec([string] $arguments, [int] $timeoutSec = 600) {
    # Returns the exit code, -1 on timeout (process killed).
    $p = Start-Process msiexec.exe -ArgumentList $arguments -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { }; return -1 }
    return $p.ExitCode
}

function Wait-MsiIdle([int] $timeoutSec = 300) {
    # Windows Installer holds Global\_MSIExecute while an installation runs (also one left over from a
    # killed client). Waits until it is gone so that the next msiexec does not end with 1618.
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $m = $null
    while ([System.Threading.Mutex]::TryOpenExisting('Global\_MSIExecute', [ref]$m) -and (Get-Date) -lt $deadline) {
        $m.Dispose(); Start-Sleep -Seconds 2
    }
}

function Install-AppStartMsi([string] $msi, [string] $log) {
    Wait-MsiIdle
    $code = Invoke-Msiexec "/i `"$msi`" /qn /l*v `"$log`""
    Start-Sleep -Seconds 3
    return $code
}

function Uninstall-AppStartMsi {
    $codes = @()
    Wait-MsiIdle 120
    foreach ($product in @(Get-AppStartProduct)) { $codes += Invoke-Msiexec "/x $($product.PSChildName) /qn" }
    Start-Sleep -Seconds 2
    return $codes
}

function Get-MsiProperty([string] $msi, [string] $property) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($msi, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property='$property'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $value = if ($rec) { $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, 1) } else { $null }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($db)
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
    return $value
}

function New-TestAppPrerequisites {
    # State the application had before AppStart: data folder and registry key with a secret.
    New-Item -ItemType Directory -Force $TestDataDir | Out-Null
    New-Item -Force $TestRegKey | Out-Null
    Set-ItemProperty $TestRegKey -Name Password -Value 'AppStartTest-Passwort-0815'
}

function Get-TestAppId([string] $exe) {
    $cfg = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return (@($cfg.apps) | Where-Object { $_.exe -eq $exe } | Select-Object -First 1).id
}

function Get-LauncherShortcuts {
    # All .lnk files on the public desktop and in the common Start menu that point to the launcher.
    $shell = New-Object -ComObject WScript.Shell
    $roots = [Environment]::GetFolderPath('CommonDesktopDirectory'), [Environment]::GetFolderPath('CommonPrograms')
    foreach ($f in Get-ChildItem $roots -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue) {
        $l = $shell.CreateShortcut($f.FullName)
        if ($l.TargetPath -like '*\AppStart.Launcher.exe') {
            [pscustomobject]@{ Path = $f.FullName; Target = $l.TargetPath; Arguments = $l.Arguments; Icon = $l.IconLocation; WorkDir = $l.WorkingDirectory }
        }
    }
}

function Get-ProcessOwner($cimProcess) {
    try { return (Invoke-CimMethod -InputObject $cimProcess -MethodName GetOwner).User } catch { return '' }
}

function Get-TestAppProcesses([datetime] $since = [datetime]::MinValue) {
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='AppStart.TestApp.exe'")) {
        if ($p.CreationDate -ge $since) {
            [pscustomobject]@{ Id = [int]$p.ProcessId; Owner = (Get-ProcessOwner $p); Session = [int]$p.SessionId; Path = $p.ExecutablePath }
        }
    }
}

function Wait-TestApp([datetime] $since, [int] $count = 1, [int] $timeoutSec = 25, [string] $owner = 'appstart-app') {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $found = @(Get-TestAppProcesses $since | Where-Object { $_.Owner -eq $owner })
        if ($found.Count -ge $count) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $found
}

function Stop-TestApps {
    Get-Process AppStart.TestApp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# ------------------------------------------------------------------ UI Automation

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$UIA = [System.Windows.Automation.AutomationElement]

function Get-ProcessWindows([int] $processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ProcessIdProperty, $processId)
    return @($UIA::RootElement.FindAll('Children', $cond))
}

function Wait-ProcessWindow([int] $processId, [int] $timeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $w = Get-ProcessWindows $processId | Select-Object -First 1
        if ($w) { Start-Sleep -Milliseconds 700; return $w }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-WindowTexts($window) {
    return @($window.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique)
}

function Get-WindowTextControls($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    return @($window.FindAll('Descendants', $cond) | ForEach-Object { $_.Current.Name })
}

function Invoke-TestAppButton($app, [string] $name) {
    # $app: @{ Window = <TestApp window> }
    $btn = $app.Window.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)))
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3
}

function Get-TestAppRow($app, [string] $label) {
    # Rows of the TestApp are label, value, badge in this order. Returns "value | badge".
    $texts = @(Get-WindowTextControls $app.Window)
    $i = [Array]::IndexOf($texts, $label)
    if ($i -lt 0 -or $i + 2 -ge $texts.Count) { return '' }
    return "$($texts[$i + 1]) | $($texts[$i + 2])"
}

function Get-OperatorCmd {
    # cmd.exe started from an application, i.e. running as appstart-app.
    return @(Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object { (Get-ProcessOwner $_) -eq 'appstart-app' })
}

function Stop-OperatorCmd { Get-OperatorCmd | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }

function Save-WindowShot($window, [string] $file) {
    try {
        $r = $window.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { return }
        $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
        $bmp.Save($file); $g.Dispose(); $bmp.Dispose()
    } catch { Write-Host "  (Bildschirmfoto $file nicht möglich: $($_.Exception.Message))" }
}

if (-not ('AppStartE2E.User32' -as [type])) {
    Add-Type -Namespace 'AppStartE2E' -Name 'User32' -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);'
}

function Close-UiWindow($window) {
    # The launcher's TaskDialog has only an OK button and no TDF_ALLOW_DIALOG_CANCELLATION: it ignores
    # WM_CLOSE (WindowPattern.Close), like Alt+F4. And it belongs to another user (the operator), so
    # pressing OK through UI Automation does not reach it from the admin test. TDM_CLICK_BUTTON(IDOK)
    # posted to the dialog does (a higher integrity level may post to a lower one).
    try {
        if ($window.Current.ClassName -eq '#32770') {
            [void][AppStartE2E.User32]::PostMessage([IntPtr]$window.Current.NativeWindowHandle, 0x0466, [IntPtr]1, [IntPtr]::Zero)
            Start-Sleep -Milliseconds 300
        }
    } catch { }
    try {
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'OK')))
        $ok = $window.FindFirst('Descendants', $cond)
        if ($ok) { $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return }
    } catch { }
    try { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
}

function Wait-ProcessHandlingDialogs([System.Diagnostics.Process] $process, [int] $timeoutSec = 30, [string] $shot = '') {
    # Waits for the process; every window it shows is read, photographed (first one) and closed.
    # Returns @{ Exit; Title; Texts; TimedOut }.
    $null = $process.Handle   # Windows PowerShell keeps the exit code only if the handle was opened while running
    $res = @{ Exit = $null; Title = ''; Texts = ''; TimedOut = $false }
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
        $w = Get-ProcessWindows $process.Id | Select-Object -First 1
        if ($w) {
            Start-Sleep -Milliseconds 600
            if (-not $res.Title) { $res.Title = $w.Current.Name; if ($shot) { Save-WindowShot $w (Join-Path $StageDir $shot) } }
            $res.Texts = (@($res.Texts) + (Get-WindowTexts $w) | Where-Object { $_ } | Select-Object -Unique) -join ' | '
            Close-UiWindow $w
        }
        Start-Sleep -Milliseconds 300
    }
    if (-not $process.HasExited) { $res.TimedOut = $true; try { $process.Kill() } catch { } }
    $process.WaitForExit(10000) | Out-Null
    $res.Exit = $process.ExitCode
    return $res
}

function Resolve-LauncherExit($r) {
    # Start-Process -Credential sometimes cannot read the exit code (-1/empty). Then derive it from what the
    # launcher showed (LauncherLogic exit codes; no dialog = success) and mark it in the log.
    if ($r.Exit -is [int] -and $r.Exit -ge 0) { return $r }
    $derived = switch -Wildcard ([string]$r.Texts) {
        '*Keine Berechtigung*' { 4 } '*Start verweigert*' { 3 } '*Start läuft bereits*' { 3 }
        '*nicht erreichbar*' { 2 } '*Ungültige Verknüpfung*' { 1 } '*Unerwartete Antwort*' { 5 } default { if ($r.Texts) { $null } else { 0 } }
    }
    if ($null -ne $derived -and -not $r.TimedOut) { Write-Host "  (Exit-Code nicht lesbar: '$($r.Exit)', aus der Anzeige abgeleitet: $derived)"; $r.Exit = $derived }
    return $r
}

function Start-LauncherAs([pscredential] $Credential, [string] $Arguments, [string] $shot = '') {
    # The launcher as the operator (standard user token). Returns @{ Exit; Title; Texts; TimedOut }.
    $sp = @{ FilePath = $LauncherExe; Credential = $Credential; WorkingDirectory = $CurDir; PassThru = $true }
    if ($Arguments) { $sp.ArgumentList = $Arguments }
    $p = Start-Process @sp
    $r = Wait-ProcessHandlingDialogs $p 30 $shot
    $r = Resolve-LauncherExit $r
    Write-Host "  [$($Credential.UserName)] Launcher $Arguments -> exit $($r.Exit) '$($r.Title)' $($r.Texts)"
    return $r
}

function Start-ShortcutAs([pscredential] $Credential, [string] $lnk) {
    # Really through the .lnk: cmd "start" uses ShellExecute on the shortcut as the operator.
    Start-Process cmd.exe -Credential $Credential -WorkingDirectory $env:SystemRoot -WindowStyle Hidden `
        -ArgumentList "/d /c start `"`" `"$lnk`"" -PassThru
}

function Get-AppStartEvents([int] $id, [datetime] $since) {
    return @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'AppStart'; Id = $id; StartTime = $since } -ErrorAction SilentlyContinue)
}

function Set-OneByteChanged([string] $path) {
    # Changes one byte in the DOS stub text ("This program cannot be run..."): the EXE stays runnable,
    # the SHA-256 changes. Falls back to offset 0x4E (start of that text in every PE file).
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $marker = [System.Text.Encoding]::ASCII.GetBytes('This program')
    $offset = 0x4E
    for ($i = 0; $i -lt [Math]::Min(1024, $bytes.Length - $marker.Length); $i++) {
        $match = $true
        for ($j = 0; $j -lt $marker.Length; $j++) { if ($bytes[$i + $j] -ne $marker[$j]) { $match = $false; break } }
        if ($match) { $offset = $i; break }
    }
    $bytes[$offset] = $bytes[$offset] -bxor 0x20
    [System.IO.File]::WriteAllBytes($path, $bytes)
}

function Get-Sddl([string] $path) { return (Get-Acl $path).Sddl }

# ------------------------------------------------------------------ cleanup

function Remove-DenyLogonEntries([string] $sid) {
    # Same as spikes/Remove-SpikeSetup.ps1: no orphaned SID may remain in the user rights policy.
    $tmp = Join-Path $env:TEMP 'appstart-e2e-secpol'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    secedit /export /cfg "$tmp\cur.inf" /areas USER_RIGHTS | Out-Null
    $lines = Get-Content "$tmp\cur.inf" | ForEach-Object {
        if ($_ -match '^(SeDenyInteractiveLogonRight|SeDenyRemoteInteractiveLogonRight) = (.*)$') {
            $rest = ($Matches[2] -split ',') | Where-Object { $_.Trim() -ne "*$sid" }
            if ($rest) { "$($Matches[1]) = $($rest -join ',')" } else { "$($Matches[1]) =" }
        } else { $_ }
    }
    $lines | Set-Content "$tmp\new.inf" -Encoding Unicode
    secedit /configure /db "$tmp\secedit.sdb" /cfg "$tmp\new.inf" /areas USER_RIGHTS | Out-Null
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

function Invoke-E2ECleanup([string[]] $extraUsers = @(), [string[]] $extraPaths = @()) {
    Write-Host "`n================ Cleanup ================"
    $ErrorActionPreference = 'Continue'
    Get-Process AppStart.TestApp, AppStart.Launcher, AppStart.Setup -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-ScheduledTask -TaskName 'AppStartE2E-*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
    if ((Test-Path $SetupExe) -and ((Test-Path $ConfigPath) -or (Get-LocalUser appstart-app -ErrorAction SilentlyContinue))) {
        Invoke-Setup @('uninstall') | Out-Null
    }
    Uninstall-AppStartMsi | Out-Null
    Stop-Service AppStart -ErrorAction SilentlyContinue
    sc.exe delete AppStart 2>$null | Out-Null
    Start-Sleep -Seconds 1
    # Fallback if setup uninstall did not run: accounts, rights, group, profiles.
    foreach ($u in @('appstart-app', 'appstart-user') + $extraUsers) {
        $local = Get-LocalUser $u -ErrorAction SilentlyContinue
        if ($local) {
            if ($u -eq 'appstart-app') { Remove-DenyLogonEntries $local.SID.Value }
            Remove-LocalUser $u -ErrorAction SilentlyContinue
        }
    }
    if (Get-LocalGroup 'AppStart Users' -ErrorAction SilentlyContinue) { Remove-LocalGroup 'AppStart Users' }
    $names = @('appstart-app', 'appstart-user') + $extraUsers
    for ($i = 0; $i -lt 3; $i++) {
        Get-CimInstance Win32_UserProfile | Where-Object { $names -contains (Split-Path $_.LocalPath -Leaf) -or $names -contains ((Split-Path $_.LocalPath -Leaf) -replace '\.[^.]+$', '') } |
            Remove-CimInstance -ErrorAction SilentlyContinue
        if (-not (Get-CimInstance Win32_UserProfile | Where-Object { $names -contains (Split-Path $_.LocalPath -Leaf) })) { break }
        Start-Sleep -Seconds 3
    }
    foreach ($l in @(Get-LauncherShortcuts)) { Remove-Item $l.Path -Force -ErrorAction SilentlyContinue }
    Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$TestAppName.lnk") -Force -ErrorAction SilentlyContinue
    Remove-Item $TestRegKey -Recurse -ErrorAction SilentlyContinue
    foreach ($path in @($TestDataDir, $AppStartData, $ProgDir, $StartMenuDir, $WorkDir) + $extraPaths) {
        if ($path) { Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if ([System.Diagnostics.EventLog]::SourceExists('AppStart')) { [System.Diagnostics.EventLog]::DeleteEventSource('AppStart') }
    $left = @(Get-LocalUser -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.Name } | ForEach-Object Name)
    Write-Host ("Rest: Dienst={0} Produkt={1} Konten={2} Gruppe={3} ProgramData={4} Programme={5} TestDaten={6}/{7}" -f
        [bool](Get-Service AppStart -ErrorAction SilentlyContinue), [bool](Get-AppStartProduct), ($left -join ','),
        [bool](Get-LocalGroup 'AppStart Users' -ErrorAction SilentlyContinue), (Test-Path $AppStartData), (Test-Path $ProgDir),
        (Test-Path $TestDataDir), (Test-Path $TestRegKey))
}
