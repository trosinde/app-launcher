#Requires -RunAsAdministrator
<#
  Screenshots of the user manual from the REAL software (task 10.2, docs/manual/images/README.md).
  Only on the TEST VM (fresh snapshot, 100 % scaling, German Windows). Installs AppStart through the
  bootstrapper, sets it up through the wizard, photographs every screen and removes everything again.

    .\Capture-ManualScreenshots.ps1 -BootstrapperPath <AppStart-Setup.exe> -OutputDir <folder>

  Each image is captured on its own: if one fails it is reported as SKIP with the reason and the
  script goes on. At the end a list of all images (OK / SKIP). Passwords are random per run and never
  appear in an image, on the console or in a file (password boxes only show dots).
#>
param(
    [Parameter(Mandatory)] [string] $BootstrapperPath,
    [Parameter(Mandatory)] [string] $OutputDir
)
$ErrorActionPreference = 'Stop'
$BootstrapperPath = (Resolve-Path $BootstrapperPath).Path
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path
$StageDir = $OutputDir   # used by helpers of E2E.Common.ps1

# Physical pixels for UI Automation rectangles and CopyFromScreen: set before any UI work.
if (-not ('AppStartShotNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AppStartShotNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
}
'@
}
[void][AppStartShotNative]::SetProcessDPIAware()

. (Join-Path $PSScriptRoot 'E2E.Common.ps1')
Add-Type -AssemblyName System.Windows.Forms

$CopyDir = 'C:\Program Files\AppStartTestApp'
$CopyExe = Join-Path $CopyDir 'AppStart.TestApp.exe'
$ShotWork = Join-Path $env:ProgramData 'AppStartShots'
$ViewDir = Join-Path $env:ProgramData 'Microsoft\Event Viewer\Views'
$ViewFile = Join-Path $ViewDir 'AppStartShots.xml'
$PublicLnk = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$TestAppName.lnk"

$AllShots = @('startmenue.png', 'bootstrapper.png', 'setup-1.png', 'setup-2.png', 'setup-3-laeuft.png', 'setup-3.png',
    'testapp.png', 'lernmodus.png', 'lernergebnis.png', 'hilfsprogramme.png', 'setup-weitere-programme.png', 'pruefansicht-ok.png', 'pruefansicht-abweichung.png', 'verify-konsole.png',
    'launcher-nicht-erreichbar.png', 'launcher-verweigert.png', 'ereignisanzeige.png')
$Shots = [ordered]@{}
foreach ($s in $AllShots) { $Shots[$s] = 'SKIP  not reached' }

# ------------------------------------------------------------------ helpers

function Set-ShotSkip([string[]] $files, [string] $reason) {
    foreach ($f in $files) {
        if ($Shots[$f] -notlike 'OK*') { $Shots[$f] = "SKIP  $reason"; Write-Host "  SKIP $f  $reason" }
    }
}

function Get-Hwnd($window) {
    try { return [IntPtr]$window.Current.NativeWindowHandle } catch { return [IntPtr]::Zero }
}

function Show-Front($window) {
    # Restore and bring to the foreground. The Alt key press lifts the foreground lock of Windows.
    $h = Get-Hwnd $window
    if ($h -eq [IntPtr]::Zero) { return }
    if ([AppStartShotNative]::IsIconic($h)) { [void][AppStartShotNative]::ShowWindow($h, 9) }
    [AppStartShotNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [AppStartShotNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void][AppStartShotNative]::BringWindowToTop($h)
    [void][AppStartShotNative]::SetForegroundWindow($h)
    try { $window.SetFocus() } catch { }
}

function Save-Shot($window, [string] $file, [string] $note = '') {
    # Window rectangle without the invisible resize border (DWM extended frame bounds), else the
    # UI Automation rectangle. Cursor moved away so that no hover effect shows.
    $path = Join-Path $OutputDir $file
    try {
        Show-Front $window
        [void][AppStartShotNative]::SetCursorPos(0, 0)
        Start-Sleep -Milliseconds 700
        $x = 0; $y = 0; $w = 0; $hgt = 0
        $rect = New-Object AppStartShotNative+RECT
        $h = Get-Hwnd $window
        if ($h -ne [IntPtr]::Zero -and [AppStartShotNative]::DwmGetWindowAttribute($h, 9, [ref]$rect, 16) -eq 0 -and $rect.Right -gt $rect.Left) {
            $x = $rect.Left; $y = $rect.Top; $w = $rect.Right - $rect.Left; $hgt = $rect.Bottom - $rect.Top
        } else {
            $r = $window.Current.BoundingRectangle
            $x = [int]$r.X; $y = [int]$r.Y; $w = [int]$r.Width; $hgt = [int]$r.Height
        }
        if ($w -le 0 -or $hgt -le 0) { throw 'Window has no size' }
        $bmp = New-Object System.Drawing.Bitmap($w, $hgt)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        $Shots[$file] = "OK    $path" + $(if ($note) { "  ($note)" } else { '' })
        Write-Host "  OK   $file"
        return $true
    } catch {
        Set-ShotSkip @($file) "Screenshot not possible: $($_.Exception.Message)"
        return $false
    }
}

function Find-ByName($root, [string] $name, $controlType = $null) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    if ($controlType) {
        $cond = New-Object System.Windows.Automation.AndCondition($cond,
            (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $controlType)))
    }
    return $root.FindFirst('Descendants', $cond)
}

function Wait-Element($root, [string] $name, [int] $timeoutSec = 20, $controlType = $null) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $e = Find-ByName $root $name $controlType
        if ($e) { return $e }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Invoke-Button($root, [string] $name, [int] $timeoutSec = 20) {
    # Waits until the button exists and is enabled, then invokes it.
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $b = Find-ByName $root $name ([System.Windows.Automation.ControlType]::Button)
        if ($b -and $b.Current.IsEnabled) {
            $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            return
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "Button \"$name\" not found or not enabled"
}

function Wait-TopWindow([scriptblock] $match, [int] $timeoutSec = 20) {
    # First top-level window for which $match returns true ($_ = AutomationElement).
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $w = @($UIA::RootElement.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition)) | Where-Object $match | Select-Object -First 1
        if ($w) { Start-Sleep -Milliseconds 700; return $w }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Wait-NewProcess([string] $name, [int[]] $before, [int] $timeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $p = Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } | Select-Object -First 1
        if ($p) { $null = $p.Handle; return $p }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Close-Process($process, [int] $timeoutSec = 15) {
    if (-not $process) { return }
    foreach ($w in Get-ProcessWindows $process.Id) { Close-UiWindow $w }
    if (-not $process.WaitForExit($timeoutSec * 1000)) { try { $process.Kill() } catch { } }
}

function Find-ById($root, [string] $automationId) {
    return $root.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $automationId)))
}

function Wait-DialogWith([int] $processId, [string] $name, [int] $timeoutSec = 20) {
    # Window of the process (top level or owned dialog below another window) that contains an element $name.
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $winCond = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    do {
        foreach ($top in @(Get-ProcessWindows $processId)) {
            foreach ($w in @($top) + @($top.FindAll('Descendants', $winCond))) {
                if (Find-ByName $w $name) { Start-Sleep -Milliseconds 700; return $w }
            }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Capture-LauncherDialog([pscredential] $op, [string] $lnk, [string] $file, [string] $expect) {
    # Starts the shortcut as the operator, photographs the launcher's TaskDialog and closes it.
    $before = @(Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-ShortcutAs $op $lnk | Out-Null
    $p = Wait-NewProcess 'AppStart.Launcher' $before 20
    if (-not $p) { Set-ShotSkip @($file) 'Launcher process not started'; return }
    $w = Wait-ProcessWindow $p.Id 30
    if (-not $w) { Set-ShotSkip @($file) "no message from the launcher (exit $(if ($p.HasExited) { $p.ExitCode } else { 'running' }))"; Close-Process $p; return }
    Start-Sleep -Milliseconds 500
    $texts = (Get-WindowTexts $w) -join ' | '
    Write-Host "  Launcher message: '$($w.Current.Name)' $texts"
    if ($texts -like "*$expect*") { [void](Save-Shot $w $file) }
    else { Set-ShotSkip @($file) "Message does not contain \"$expect\": $texts" }
    Close-Process $p
}

# ------------------------------------------------------------------ run

$consoleHwnd = [AppStartShotNative]::GetConsoleWindow()
$password = $null
$op = $null
$exeBackup = $null
$serviceStopped = $false
$adminDeviation = $false
try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), Bootstrapper $BootstrapperPath, Ausgabe $OutputDir"
    if (Get-AppStartProduct) { throw 'AppStart is already installed. Run only on a fresh test VM (snapshot).' }
    Remove-Item $ShotWork -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $ShotWork | Out-Null
    # Own console out of the way: CopyFromScreen photographs whatever is on top.
    if ($consoleHwnd -ne [IntPtr]::Zero) { [void][AppStartShotNative]::ShowWindow($consoleHwnd, 6) }

    # ================================================================ 1. bootstrapper
    Write-Host "`n-- Bootstrapper"
    $t0 = Get-Date
    $boot = Start-Process $BootstrapperPath -WorkingDirectory (Split-Path $BootstrapperPath) -PassThru
    $null = $boot.Handle
    $wizard = $null
    $progressSeen = $false
    $deadline = (Get-Date).AddMinutes(10)
    while (-not $wizard -and (Get-Date) -lt $deadline) {
        foreach ($w in @(Get-ProcessWindows $boot.Id)) {
            $texts = (Get-WindowTexts $w) -join ' | '
            if ($texts -like '*Installing AppStart*') {
                if (-not $progressSeen) {
                    $progressSeen = $true
                    Start-Sleep -Milliseconds 1200   # marquee bar visibly moving
                    [void](Save-Shot $w 'bootstrapper.png')
                }
            } elseif ($texts) {
                # Any other message (3010 restart, already installed, untrusted, failure): log and close.
                Write-Host "  Bootstrapper message: $texts"
                Close-UiWindow $w
            }
        }
        $wizard = Get-Process AppStart.Setup -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $t0 -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if (-not $wizard -and $boot.HasExited) { break }
        Start-Sleep -Milliseconds 150
    }
    if (-not $progressSeen) { Set-ShotSkip @('bootstrapper.png') 'Progress dialog not seen (installation too fast or message instead of progress)' }
    if ($wizard) {
        # This wizard loaded an empty configuration; it is reopened after the release of the TestApp.
        $null = $wizard.Handle
        for ($i = 0; $i -lt 3 -and -not $wizard.HasExited; $i++) {
            foreach ($w in Get-ProcessWindows $wizard.Id) {
                $yes = @('Yes', 'Ja') | ForEach-Object { Find-ByName $w $_ ([System.Windows.Automation.ControlType]::Button) } | Where-Object { $_ } | Select-Object -First 1 # MessageBox buttons follow the Windows language
                if ($yes) { try { $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } } else { Close-UiWindow $w }
            }
            $wizard.WaitForExit(5000) | Out-Null
        }
        if (-not $wizard.HasExited) { $wizard.Kill() }
    }
    if (-not $boot.WaitForExit(30000)) {
        foreach ($w in @(Get-ProcessWindows $boot.Id)) { Write-Host "  Bootstrapper message: $((Get-WindowTexts $w) -join ' | ')"; Close-UiWindow $w }
        if (-not $boot.WaitForExit(10000)) { $boot.Kill() }
    }
    if (-not (Get-AppStartProduct) -or -not (Test-Path $SetupExe)) { throw 'AppStart was not installed by the bootstrapper.' }
    Write-Host "  installiert, Bootstrapper exit $($boot.ExitCode)"

    # ================================================================ 2. release TestApp copy, wizard
    Write-Host "`n-- TestApp freigeben"
    New-TestAppPrerequisites
    New-Item -ItemType Directory -Force $CopyDir | Out-Null
    Copy-Item (Join-Path (Split-Path $PackagedTestApp) '*') $CopyDir -Recurse -Force
    $r = Invoke-Setup @('add', '--exe', $CopyExe, '--data-dir', $TestDataDir, '--reg-key', $TestRegKeyCli, '--name', $TestAppName)
    if ($r.Exit -ne 0) { throw "add fehlgeschlagen (exit $($r.Exit))" }
    $appId = Get-TestAppId $CopyExe
    Write-Host "  AppId $appId"

    Write-Host "`n-- Assistent"
    $password = New-RandomPassword
    $wizardFiles = @('setup-1.png', 'setup-2.png', 'setup-3-laeuft.png', 'setup-3.png')
    $wiz = $null
    try {
        $wiz = Start-Process $SetupExe -WorkingDirectory $CurDir -PassThru
        $win = Wait-ProcessWindow $wiz.Id 60
        if (-not $win) { throw 'Window "AppStart Setup" does not appear' }
        if (-not (Wait-Element $win "Protection for $TestAppName" 60)) { throw 'TestApp does not appear in step 1' }
        $item = Find-ByName $win $TestAppName ([System.Windows.Automation.ControlType]::ListItem)
        if ($item) { try { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { } }
        $cb = Find-ByName $win $TestAppName ([System.Windows.Automation.ControlType]::CheckBox)
        if ($cb) {
            $tp = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($tp.Current.ToggleState -ne 'On') { $tp.Toggle() }
        }
        Start-Sleep -Milliseconds 500
        [void](Save-Shot $win 'setup-1.png' 'one application (SHA-256); second signed application not automated')

        # ---- step 2
        Invoke-Button $win 'Next'
        $opBox = Wait-Element $win 'Create operator account appstart-user' 20 ([System.Windows.Automation.ControlType]::CheckBox)
        if (-not $opBox) { throw 'Step 2: check box "Create operator account appstart-user" missing' }
        $tp = $opBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($tp.Current.ToggleState -ne 'On') { $tp.Toggle() }
        Start-Sleep -Milliseconds 500
        $pwCond = New-Object System.Windows.Automation.PropertyCondition($UIA::IsPasswordProperty, $true)
        $pwBoxes = @($win.FindAll('Descendants', $pwCond))
        if ($pwBoxes.Count -lt 2) { throw "Step 2: $($pwBoxes.Count) password field(s) instead of 2" }
        foreach ($box in $pwBoxes[0..1]) {
            $set = $false
            try { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($password); $set = $true } catch { }
            if (-not $set) {
                # Fallback: type it (password has only letters, digits and "!"; no SendKeys special characters)
                $box.SetFocus(); Start-Sleep -Milliseconds 200
                [System.Windows.Forms.SendKeys]::SendWait('^a'); [System.Windows.Forms.SendKeys]::SendWait($password)
            }
            Start-Sleep -Milliseconds 200
        }
        if (Find-ByName $win 'The passwords do not match.') { throw 'Step 2: passwords were not taken over' }
        # Example member chip for the image only; removed again before "Next" (account does not exist).
        $memberAdded = $false
        $memberBox = Find-ByName $win 'Add user' ([System.Windows.Automation.ControlType]::Edit)
        if ($memberBox) {
            try {
                $memberBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('DOMAIN\bediener01')
                Start-Sleep -Milliseconds 300
                Invoke-Button $win 'Add' 5
                $memberAdded = [bool](Wait-Element $win 'DOMAIN\bediener01' 5 ([System.Windows.Automation.ControlType]::Text))
            } catch { Write-Host "  Member example not possible: $($_.Exception.Message)" }
        }
        [void](Save-Shot $win 'setup-2.png' $(if ($memberAdded) { 'with example DOMAIN\bediener01' } else { 'without other member' }))
        if ($memberAdded) {
            Invoke-Button $win 'Remove member' 5
            Start-Sleep -Milliseconds 500
            if (Find-ByName $win 'DOMAIN\bediener01' ([System.Windows.Automation.ControlType]::Text)) { throw 'Example member DOMAIN\bediener01 could not be removed' }
        } elseif ($memberBox) {
            try { $memberBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('') } catch { }
        }

        # ---- step 3: running, then done
        Invoke-Button $win 'Next'
        $runningShot = $false
        $deadline = (Get-Date).AddMinutes(5)
        $fertig = $null
        while ((Get-Date) -lt $deadline) {
            $fertig = Find-ByName $win 'Finish' ([System.Windows.Automation.ControlType]::Button)
            if ($fertig -and $fertig.Current.IsEnabled) { break }
            # First shot as soon as "Setup running" shows a progress line other than the start text.
            if (-not $runningShot -and (Find-ByName $win 'Setup running') -and -not (Find-ByName $win 'Starting setup…')) {
                $grab = $Shots['setup-3-laeuft.png']
                $runningShot = Save-Shot $win 'setup-3-laeuft.png'
                # If it finished meanwhile the image would show the result: discard it.
                if ($runningShot -and -not (Find-ByName $win 'Setup running')) {
                    Remove-Item (Join-Path $OutputDir 'setup-3-laeuft.png') -ErrorAction SilentlyContinue
                    $Shots['setup-3-laeuft.png'] = $grab; $runningShot = $true
                    Set-ShotSkip @('setup-3-laeuft.png') 'Setup finished during the capture'
                }
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $runningShot) { Set-ShotSkip @('setup-3-laeuft.png') 'Setup too fast, intermediate state not caught' }
        if (-not ($fertig -and $fertig.Current.IsEnabled)) { throw 'Step 3: setup not finished after 5 minutes' }
        Start-Sleep -Milliseconds 800
        $texts = (Get-WindowTexts $win) -join ' | '
        $passed = $texts -like '*Verification passed*'
        if (-not $passed) { Write-Host "  Caution, verification failed: $texts" }
        [void](Save-Shot $win 'setup-3.png' $(if ($passed) { '' } else { 'CAUTION: verification failed, check the image' }))
        Invoke-Button $win 'Finish'
        if (-not $wiz.WaitForExit(20000)) { Close-Process $wiz }
    } catch {
        Set-ShotSkip $wizardFiles "Assistent: $($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        if ($wiz -and -not $wiz.HasExited) { try { $wiz.Kill() } catch { } }
    }

    # Later images need the operator; if the wizard did not create it, the CLI does.
    if (-not (Get-LocalUser appstart-user -ErrorAction SilentlyContinue)) {
        Write-Host '  appstart-user missing after the wizard: add-user via the command line'
        $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
        if ($r.Exit -ne 0) { throw 'appstart-user could not be created' }
    }
    $op = New-OperatorCredential 'appstart-user' $password
    $lnk = if (Test-Path $PublicLnk) { $PublicLnk } else { (@(Get-LauncherShortcuts) | Where-Object { $_.Arguments -eq "--app $appId" } | Select-Object -First 1).Path }
    if (-not $lnk) { throw 'Shortcut "AppStart TestApp" not found' }
    if ((Get-Service AppStart).Status -ne 'Running') { Start-Service AppStart; Start-Sleep -Seconds 2 }

    # ================================================================ 3. TestApp
    Write-Host "`n-- TestApp"
    try {
        Start-Sleep -Seconds 3   # launcher cooldown
        $t = Get-Date
        Start-ShortcutAs $op $lnk | Out-Null
        $apps = @(Wait-TestApp $t 1 30)
        if (-not $apps) {
            $dialogs = @(Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | ForEach-Object { Get-ProcessWindows $_.Id } | ForEach-Object { (Get-WindowTexts $_) -join ' | ' })
            throw "TestApp does not start as appstart-app. $($dialogs -join ' / ')"
        }
        $w = Wait-ProcessWindow $apps[0].Id 30
        if (-not $w) { throw 'no window "AppStart TestApp"' }
        Start-Sleep -Seconds 2   # checks of the TestApp run at start
        Invoke-Button $w 'Start command prompt'
        $deadline = (Get-Date).AddSeconds(10)
        while (-not ((Get-WindowTextControls $w) -contains 'Start blocked by job object') -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
        $texts = Get-WindowTextControls $w
        $note = if ($texts -contains 'Start blocked by job object') { '' } else { 'CAUTION: "Start blocked by job object" not found' }
        [void](Save-Shot $w 'testapp.png' $note)
        Close-UiWindow $w
    } catch {
        Set-ShotSkip @('testapp.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    } finally { Stop-TestApps }

    # ================================================================ 3b. helper programs and learn mode (add-allowed-child-processes 8.1)
    Write-Host "`n-- Other programs, learning mode"
    $childFiles = @('lernmodus.png', 'lernergebnis.png', 'hilfsprogramme.png', 'setup-weitere-programme.png')
    $wiz = $null
    try {
        $wiz = Start-Process $SetupExe -WorkingDirectory $CurDir -PassThru
        $win = Wait-ProcessWindow $wiz.Id 60
        if (-not $win) { throw 'Window "AppStart Setup" does not appear' }
        if (-not (Wait-Element $win "Protection for $TestAppName" 60)) { throw 'TestApp does not appear in step 1' }
        $item = Find-ByName $win $TestAppName ([System.Windows.Automation.ControlType]::ListItem)
        if ($item) { try { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { } }
        Start-Sleep -Milliseconds 500

        # "Other programs": Allowed helper programs only
        $combo = Find-ById $win 'ChildModeBox'
        if (-not $combo) { throw 'Selection "Other programs" (ChildModeBox) missing' }
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 500
        $opt = Find-ByName $combo 'Allowed helper programs only' ([System.Windows.Automation.ControlType]::ListItem)
        if (-not $opt) { $opt = Find-ByName $UIA::RootElement 'Allowed helper programs only' ([System.Windows.Automation.ControlType]::ListItem) }
        if (-not $opt) { throw 'Entry "Allowed helper programs only" not found' }
        $opt.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        try { $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
        Start-Sleep -Milliseconds 500

        # Dialog "Allowed helper programs" (still empty) -> Start learning mode…
        $btn = Find-ById $win 'HelpersButton'
        if (-not $btn) { throw 'Button "Helper programs…" missing' }
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $helpers = Wait-DialogWith $wiz.Id 'Start learning mode…' 20
        if (-not $helpers) { throw 'Dialog "Allowed helper programs" does not appear' }
        Invoke-Button $helpers 'Start learning mode…'
        $learn = Wait-DialogWith $wiz.Id 'End learning mode' 30
        if (-not $learn) { throw 'Dialog "Learning mode" does not appear' }

        # The operator uses the application: allowed helper (its grandchild cmd.exe) and the command prompt.
        Start-Sleep -Seconds 3
        $t = Get-Date
        Start-ShortcutAs $op $lnk | Out-Null
        $apps = @(Wait-TestApp $t 1 30)
        if (-not $apps) { throw 'TestApp does not start in learning mode' }
        $tw = Wait-ProcessWindow $apps[0].Id 30
        Start-Sleep -Seconds 2
        Invoke-Button $tw 'Start allowed helper program'
        Start-Sleep -Seconds 3
        Invoke-Button $tw 'Start command prompt'
        Start-Sleep -Seconds 2
        Close-UiWindow $tw
        Stop-TestApps
        Stop-OperatorCmd
        Start-Sleep -Seconds 4   # the dialog refreshes every 2 s
        [void](Save-Shot $learn 'lernmodus.png')

        Invoke-Button $learn 'End learning mode'
        $result = Wait-DialogWith $wiz.Id 'Discard' 30
        if (-not $result) { throw 'Dialog "Apply suggestions" does not appear' }
        [void](Save-Shot $result 'lernergebnis.png')
        # "Apply 1 program" / "Apply {n} programs": the number depends on what was recorded.
        $take = @($result.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))) |
            Where-Object { $_.Current.Name -like 'Apply *program*' } | Select-Object -First 1
        if (-not $take -or -not $take.Current.IsEnabled) { throw 'Button "Apply … programs" missing or disabled' }
        $take.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

        # Back in "Allowed helper programs" with the TestHelper on the list
        $helpers = Wait-DialogWith $wiz.Id 'AppStart.TestHelper.exe' 30
        if (-not $helpers) { throw 'TestHelper is not in the list after applying' }
        [void](Save-Shot $helpers 'hilfsprogramme.png')
        Invoke-Button $helpers 'Apply'
        Start-Sleep -Seconds 1
        [void](Save-Shot $win 'setup-weitere-programme.png')
    } catch {
        Set-ShotSkip $childFiles "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    } finally {
        Stop-TestApps
        Stop-OperatorCmd
        if ($wiz -and -not $wiz.HasExited) {
            # Leave the wizard without finishing: the list is only in the wizard, apps.json stays as it was.
            for ($i = 0; $i -lt 3 -and -not $wiz.HasExited; $i++) {
                foreach ($w in Get-ProcessWindows $wiz.Id) {
                    $yes = @('Yes', 'Ja') | ForEach-Object { Find-ByName $w $_ ([System.Windows.Automation.ControlType]::Button) } | Where-Object { $_ } | Select-Object -First 1 # MessageBox buttons follow the Windows language
                    if ($yes) { try { $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } } else { Close-UiWindow $w }
                }
                $wiz.WaitForExit(5000) | Out-Null
            }
            if (-not $wiz.HasExited) { try { $wiz.Kill() } catch { } }
        }
        Invoke-Setup @('learn', 'stop', '--app', $appId) -Quiet | Out-Null
    }

    # ================================================================ 4. verify view, console
    Write-Host "`n-- Verification view"
    function Capture-VerifyView([string] $file, [string] $expect) {
        $p = Start-Process $SetupExe -ArgumentList 'verify --ui' -WorkingDirectory $CurDir -PassThru
        try {
            $w = Wait-ProcessWindow $p.Id 60
            if (-not $w) { throw 'Verification view does not appear' }
            $box = Wait-Element $w $expect 90
            if (-not $box) { throw "\"$expect\" does not appear: $((Get-WindowTexts $w) -join ' | ')" }
            if ($box.Current.IsOffscreen) {
                try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Maximized'); Start-Sleep -Milliseconds 800 } catch { }
            }
            [void](Save-Shot $w $file $(if ($box.Current.IsOffscreen) { 'CAUTION: box outside the visible area' } else { '' }))
        } finally { Close-Process $p }
    }
    try { Capture-VerifyView 'pruefansicht-ok.png' 'Setup matches the expected state' }
    catch { Set-ShotSkip @('pruefansicht-ok.png') $_.Exception.Message }

    try {
        Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
        $adminDeviation = $true
        try { Capture-VerifyView 'pruefansicht-abweichung.png' 'Do not close the service visit' }
        catch { Set-ShotSkip @('pruefansicht-abweichung.png') $_.Exception.Message }

        Write-Host "`n-- Konsole"
        try {
            # Classic console host (not Windows Terminal) so that the window belongs to a known process.
            $bat = Join-Path $ShotWork 'verify.cmd'
            @(
                '@echo off'
                'chcp 65001 >nul'
                'mode con: cols=110 lines=36'
                'title Command Prompt'
                "cd /d `"$CurDir`""
                'echo.'
                'echo %CD%^>AppStart.Setup.exe verify'
                'AppStart.Setup.exe verify'
                'echo.'
            ) | Set-Content $bat -Encoding ASCII
            $beforeCmd = @(Get-Process cmd, conhost -ErrorAction SilentlyContinue | ForEach-Object Id)
            $tc = Get-Date
            $host1 = Start-Process conhost.exe -ArgumentList "cmd.exe /k `"$bat`"" -PassThru
            $w = Wait-TopWindow { $_.Current.Name -like '*Command Prompt*' -and $_.Current.ClassName -eq 'ConsoleWindowClass' } 20
            if (-not $w) { throw 'Console window not found' }
            # Wait until verify has finished (no AppStart.Setup.exe verify process started after $tc)
            $deadline = (Get-Date).AddSeconds(90)
            do {
                Start-Sleep -Milliseconds 500
                $running = @(Get-Process AppStart.Setup -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $tc })
            } while ($running.Count -gt 0 -and (Get-Date) -lt $deadline)
            Start-Sleep -Seconds 2
            [void](Save-Shot $w 'verify-konsole.png')
            Close-UiWindow $w
            Start-Sleep -Milliseconds 500
            Get-Process cmd, conhost -ErrorAction SilentlyContinue | Where-Object { $beforeCmd -notcontains $_.Id -and $_.StartTime -ge $tc } |
                Stop-Process -Force -ErrorAction SilentlyContinue
            if ($host1 -and -not $host1.HasExited) { try { $host1.Kill() } catch { } }
        } catch { Set-ShotSkip @('verify-konsole.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)" }
    } catch {
        Set-ShotSkip @('pruefansicht-abweichung.png', 'verify-konsole.png') "Deviation cannot be produced: $($_.Exception.Message)"
    } finally {
        if ($adminDeviation) { Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user' -ErrorAction SilentlyContinue; $adminDeviation = $false }
    }

    # ================================================================ 6. launcher messages
    Write-Host "`n-- Launcher-Meldungen"
    try {
        Start-Sleep -Seconds 3
        $exeBackup = Join-Path $ShotWork 'AppStart.TestApp.exe.orig'
        Copy-Item $CopyExe $exeBackup -Force
        Set-OneByteChanged $CopyExe
        Capture-LauncherDialog $op $lnk 'launcher-verweigert.png' 'Launch denied'
    } catch {
        Set-ShotSkip @('launcher-verweigert.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    } finally {
        if ($exeBackup -and (Test-Path $exeBackup)) { Copy-Item $exeBackup $CopyExe -Force }
        Stop-TestApps
    }
    try {
        Stop-Service AppStart -Force
        $serviceStopped = $true
        (Get-Service AppStart).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        Start-Sleep -Seconds 1
        Capture-LauncherDialog $op $lnk 'launcher-nicht-erreichbar.png' 'Launch service not reachable'
    } catch {
        Set-ShotSkip @('launcher-nicht-erreichbar.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    } finally {
        if ($serviceStopped) { Start-Service AppStart -ErrorAction SilentlyContinue; $serviceStopped = $false }
        Stop-TestApps
    }

    # ================================================================ 7. Event Viewer
    Write-Host "`n-- Ereignisanzeige"
    try {
        if (-not (Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'AppStart'; Id = 1201 } -MaxEvents 1 -ErrorAction SilentlyContinue)) {
            Write-Host '  Note: no event 1201 present'
        }
        New-Item -ItemType Directory -Force $ViewDir | Out-Null
        @"
<ViewerConfig>
  <QueryConfig>
    <QueryParams><UserQuery /></QueryParams>
    <QueryNode>
      <Name>AppStart</Name>
      <Description>Anwendung, Quelle AppStart</Description>
      <QueryList>
        <Query Id="0" Path="Application">
          <Select Path="Application">*[System[Provider[@Name='AppStart']]]</Select>
        </Query>
      </QueryList>
    </QueryNode>
  </QueryConfig>
</ViewerConfig>
"@ | Set-Content $ViewFile -Encoding Unicode
        $beforeMmc = @(Get-Process mmc -ErrorAction SilentlyContinue | ForEach-Object Id)
        Start-Process eventvwr.exe -ArgumentList "/v:`"$ViewFile`""
        $mmc = Wait-NewProcess 'mmc' $beforeMmc 30
        if (-not $mmc) { throw 'Event Viewer (mmc.exe) does not start' }
        # mmc first shows "Add Snap-in"; wait for the main window.
        $w = $null
        $deadline = (Get-Date).AddSeconds(60)
        while (-not $w -and (Get-Date) -lt $deadline) {
            $w = @(Get-ProcessWindows $mmc.Id) | Where-Object { $_.Current.Name -like '*Ereignisanzeige*' -or $_.Current.Name -like '*Event Viewer*' } | Select-Object -First 1
            if (-not $w) { Start-Sleep -Milliseconds 500 }
        }
        if (-not $w) { throw 'Main window of the Event Viewer not found' }
        try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Maximized') } catch { }
        Start-Sleep -Seconds 4   # view loads its events
        # Select the newest 1201 row (best effort: MMC list rows are ListItem/DataItem with the event ID as a cell)
        $selected = $false
        $rowCond = New-Object System.Windows.Automation.OrCondition(
            (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)),
            (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)))
        foreach ($row in @($w.FindAll('Descendants', $rowCond))) {
            $cells = @($row.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name })
            if ($cells -contains '1201') {
                try { $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $selected = $true } catch { }
                if ($selected) { break }
            }
        }
        Start-Sleep -Seconds 2
        [void](Save-Shot $w 'ereignisanzeige.png' $(if ($selected) { 'entry 1201 selected' } else { 'CAUTION: entry 1201 not selected automatically, check the image' }))
        Close-UiWindow $w
        if (-not $mmc.WaitForExit(10000)) { try { $mmc.Kill() } catch { } }
    } catch {
        Set-ShotSkip @('ereignisanzeige.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        Get-Process mmc -ErrorAction SilentlyContinue | Where-Object { $beforeMmc -notcontains $_.Id } | Stop-Process -Force -ErrorAction SilentlyContinue
    } finally {
        Remove-Item $ViewFile -Force -ErrorAction SilentlyContinue
    }

    # ================================================================ 8. Start menu folder
    Write-Host "`n-- Start menu"
    try {
        if (-not (Test-Path $StartMenuDir)) { throw "Start menu folder $StartMenuDir is missing" }
        Write-Host "  Entries: $((Get-ChildItem $StartMenuDir | ForEach-Object Name) -join ', ')"
        Start-Process explorer.exe -ArgumentList "`"$StartMenuDir`""
        $w = Wait-TopWindow { $_.Current.ClassName -eq 'CabinetWClass' -and $_.Current.Name -like 'AppStart*' } 20
        if (-not $w) { throw 'Explorer window "AppStart" not found' }
        Start-Sleep -Seconds 1
        [void](Save-Shot $w 'startmenue.png' 'Explorer window of the Start menu folder')
        Close-UiWindow $w
    } catch {
        Set-ShotSkip @('startmenue.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    }
}
catch {
    Write-Host "ABBRUCH: $($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    Set-ShotSkip $AllShots "Abbruch: $($_.Exception.Message)"
}
finally {
    $ErrorActionPreference = 'Continue'
    if ($exeBackup -and (Test-Path $exeBackup) -and (Test-Path $CopyExe)) { Copy-Item $exeBackup $CopyExe -Force -ErrorAction SilentlyContinue }
    Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user' -ErrorAction SilentlyContinue
    Remove-Item $ViewFile -Force -ErrorAction SilentlyContinue
    Get-Process mmc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like '*Ereignis*' -or $_.MainWindowTitle -like '*Event*' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Invoke-E2ECleanup -extraPaths @($CopyDir, $ShotWork)
    $password = $null; $op = $null
    if ($consoleHwnd -ne [IntPtr]::Zero) { [void][AppStartShotNative]::ShowWindow($consoleHwnd, 9) }
    Write-Host "`n================ Bilder ================"
    foreach ($k in $Shots.Keys) { Write-Host ("{0,-32} {1}" -f $k, $Shots[$k]) }
    $ok = @($Shots.Values | Where-Object { $_ -like 'OK*' }).Count
    Write-Host ("{0} OK, {1} SKIP  ->  {2}" -f $ok, ($Shots.Count - $ok), $OutputDir)
}
