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
    'testapp.png', 'pruefansicht-ok.png', 'pruefansicht-abweichung.png', 'verify-konsole.png',
    'launcher-nicht-erreichbar.png', 'launcher-verweigert.png', 'ereignisanzeige.png')
$Shots = [ordered]@{}
foreach ($s in $AllShots) { $Shots[$s] = 'SKIP  nicht erreicht' }

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
        if ($w -le 0 -or $hgt -le 0) { throw 'Fenster hat keine Grösse' }
        $bmp = New-Object System.Drawing.Bitmap($w, $hgt)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        $Shots[$file] = "OK    $path" + $(if ($note) { "  ($note)" } else { '' })
        Write-Host "  OK   $file"
        return $true
    } catch {
        Set-ShotSkip @($file) "Bildschirmfoto nicht möglich: $($_.Exception.Message)"
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
    throw "Knopf «$name» nicht gefunden oder nicht aktiv"
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

function Capture-LauncherDialog([pscredential] $op, [string] $lnk, [string] $file, [string] $expect) {
    # Starts the shortcut as the operator, photographs the launcher's TaskDialog and closes it.
    $before = @(Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-ShortcutAs $op $lnk | Out-Null
    $p = Wait-NewProcess 'AppStart.Launcher' $before 20
    if (-not $p) { Set-ShotSkip @($file) 'Launcher-Prozess nicht gestartet'; return }
    $w = Wait-ProcessWindow $p.Id 30
    if (-not $w) { Set-ShotSkip @($file) "keine Meldung des Launchers (exit $(if ($p.HasExited) { $p.ExitCode } else { 'läuft' }))"; Close-Process $p; return }
    Start-Sleep -Milliseconds 500
    $texts = (Get-WindowTexts $w) -join ' | '
    Write-Host "  Launcher-Meldung: '$($w.Current.Name)' $texts"
    if ($texts -like "*$expect*") { [void](Save-Shot $w $file) }
    else { Set-ShotSkip @($file) "Meldung enthält «$expect» nicht: $texts" }
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
    if (Get-AppStartProduct) { throw 'AppStart ist bereits installiert. Nur auf einer frischen Test-VM (Snapshot) ausführen.' }
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
            if ($texts -like '*AppStart wird installiert*') {
                if (-not $progressSeen) {
                    $progressSeen = $true
                    Start-Sleep -Milliseconds 1200   # marquee bar visibly moving
                    [void](Save-Shot $w 'bootstrapper.png')
                }
            } elseif ($texts) {
                # Any other message (3010 restart, already installed, untrusted, failure): log and close.
                Write-Host "  Bootstrapper-Meldung: $texts"
                Close-UiWindow $w
            }
        }
        $wizard = Get-Process AppStart.Setup -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $t0 -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if (-not $wizard -and $boot.HasExited) { break }
        Start-Sleep -Milliseconds 150
    }
    if (-not $progressSeen) { Set-ShotSkip @('bootstrapper.png') 'Fortschrittsdialog nicht gesehen (Installation zu schnell oder Meldung statt Fortschritt)' }
    if ($wizard) {
        # This wizard loaded an empty configuration; it is reopened after the release of the TestApp.
        $null = $wizard.Handle
        for ($i = 0; $i -lt 3 -and -not $wizard.HasExited; $i++) {
            foreach ($w in Get-ProcessWindows $wizard.Id) {
                $yes = Find-ByName $w 'Ja' ([System.Windows.Automation.ControlType]::Button)
                if ($yes) { try { $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } } else { Close-UiWindow $w }
            }
            $wizard.WaitForExit(5000) | Out-Null
        }
        if (-not $wizard.HasExited) { $wizard.Kill() }
    }
    if (-not $boot.WaitForExit(30000)) {
        foreach ($w in @(Get-ProcessWindows $boot.Id)) { Write-Host "  Bootstrapper-Meldung: $((Get-WindowTexts $w) -join ' | ')"; Close-UiWindow $w }
        if (-not $boot.WaitForExit(10000)) { $boot.Kill() }
    }
    if (-not (Get-AppStartProduct) -or -not (Test-Path $SetupExe)) { throw 'AppStart wurde durch den Bootstrapper nicht installiert.' }
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
        if (-not $win) { throw 'Fenster «AppStart Setup» erscheint nicht' }
        if (-not (Wait-Element $win "Schutz für $TestAppName" 60)) { throw 'TestApp erscheint nicht in Schritt 1' }
        $item = Find-ByName $win $TestAppName ([System.Windows.Automation.ControlType]::ListItem)
        if ($item) { try { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { } }
        $cb = Find-ByName $win $TestAppName ([System.Windows.Automation.ControlType]::CheckBox)
        if ($cb) {
            $tp = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($tp.Current.ToggleState -ne 'On') { $tp.Toggle() }
        }
        Start-Sleep -Milliseconds 500
        [void](Save-Shot $win 'setup-1.png' 'eine Applikation (SHA-256); zweite signierte Applikation nicht automatisiert')

        # ---- step 2
        Invoke-Button $win 'Weiter'
        $opBox = Wait-Element $win 'Bedienerkonto appstart-user anlegen' 20 ([System.Windows.Automation.ControlType]::CheckBox)
        if (-not $opBox) { throw 'Schritt 2: Kontrollkästchen «Bedienerkonto appstart-user anlegen» fehlt' }
        $tp = $opBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($tp.Current.ToggleState -ne 'On') { $tp.Toggle() }
        Start-Sleep -Milliseconds 500
        $pwCond = New-Object System.Windows.Automation.PropertyCondition($UIA::IsPasswordProperty, $true)
        $pwBoxes = @($win.FindAll('Descendants', $pwCond))
        if ($pwBoxes.Count -lt 2) { throw "Schritt 2: $($pwBoxes.Count) Passwortfeld(er) statt 2" }
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
        if (Find-ByName $win 'Die Passwörter stimmen nicht überein.') { throw 'Schritt 2: Passwörter wurden nicht übernommen' }
        # Example member chip for the image only; removed again before «Weiter» (account does not exist).
        $memberAdded = $false
        $memberBox = Find-ByName $win 'Benutzer hinzufügen' ([System.Windows.Automation.ControlType]::Edit)
        if ($memberBox) {
            try {
                $memberBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('DOMAIN\bediener01')
                Start-Sleep -Milliseconds 300
                Invoke-Button $win 'Hinzufügen' 5
                $memberAdded = [bool](Wait-Element $win 'DOMAIN\bediener01' 5 ([System.Windows.Automation.ControlType]::Text))
            } catch { Write-Host "  Mitglied-Beispiel nicht möglich: $($_.Exception.Message)" }
        }
        [void](Save-Shot $win 'setup-2.png' $(if ($memberAdded) { 'mit Beispiel DOMAIN\bediener01' } else { 'ohne weiteres Mitglied' }))
        if ($memberAdded) {
            Invoke-Button $win 'Mitglied entfernen' 5
            Start-Sleep -Milliseconds 500
            if (Find-ByName $win 'DOMAIN\bediener01' ([System.Windows.Automation.ControlType]::Text)) { throw 'Beispiel-Mitglied DOMAIN\bediener01 liess sich nicht entfernen' }
        } elseif ($memberBox) {
            try { $memberBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('') } catch { }
        }

        # ---- step 3: running, then done
        Invoke-Button $win 'Weiter'
        $runningShot = $false
        $deadline = (Get-Date).AddMinutes(5)
        $fertig = $null
        while ((Get-Date) -lt $deadline) {
            $fertig = Find-ByName $win 'Fertig' ([System.Windows.Automation.ControlType]::Button)
            if ($fertig -and $fertig.Current.IsEnabled) { break }
            # First shot as soon as «Einrichtung läuft» shows a progress line other than the start text.
            if (-not $runningShot -and (Find-ByName $win 'Einrichtung läuft') -and -not (Find-ByName $win 'Einrichtung wird gestartet…')) {
                $grab = $Shots['setup-3-laeuft.png']
                $runningShot = Save-Shot $win 'setup-3-laeuft.png'
                # If it finished meanwhile the image would show the result: discard it.
                if ($runningShot -and -not (Find-ByName $win 'Einrichtung läuft')) {
                    Remove-Item (Join-Path $OutputDir 'setup-3-laeuft.png') -ErrorAction SilentlyContinue
                    $Shots['setup-3-laeuft.png'] = $grab; $runningShot = $true
                    Set-ShotSkip @('setup-3-laeuft.png') 'Einrichtung während der Aufnahme fertig geworden'
                }
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $runningShot) { Set-ShotSkip @('setup-3-laeuft.png') 'Einrichtung zu schnell, Zwischenzustand nicht erwischt' }
        if (-not ($fertig -and $fertig.Current.IsEnabled)) { throw 'Schritt 3: Einrichtung nach 5 Minuten nicht abgeschlossen' }
        Start-Sleep -Milliseconds 800
        $texts = (Get-WindowTexts $win) -join ' | '
        $passed = $texts -like '*Prüfung bestanden*'
        if (-not $passed) { Write-Host "  Achtung, Prüfung nicht bestanden: $texts" }
        [void](Save-Shot $win 'setup-3.png' $(if ($passed) { '' } else { 'ACHTUNG: Prüfung nicht bestanden, Bild prüfen' }))
        Invoke-Button $win 'Fertig'
        if (-not $wiz.WaitForExit(20000)) { Close-Process $wiz }
    } catch {
        Set-ShotSkip $wizardFiles "Assistent: $($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        if ($wiz -and -not $wiz.HasExited) { try { $wiz.Kill() } catch { } }
    }

    # Later images need the operator; if the wizard did not create it, the CLI does.
    if (-not (Get-LocalUser appstart-user -ErrorAction SilentlyContinue)) {
        Write-Host '  appstart-user fehlt nach dem Assistenten: add-user über die Kommandozeile'
        $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
        if ($r.Exit -ne 0) { throw 'appstart-user konnte nicht angelegt werden' }
    }
    $op = New-OperatorCredential 'appstart-user' $password
    $lnk = if (Test-Path $PublicLnk) { $PublicLnk } else { (@(Get-LauncherShortcuts) | Where-Object { $_.Arguments -eq "--app $appId" } | Select-Object -First 1).Path }
    if (-not $lnk) { throw 'Verknüpfung «AppStart TestApp» nicht gefunden' }
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
            throw "TestApp startet nicht als appstart-app. $($dialogs -join ' / ')"
        }
        $w = Wait-ProcessWindow $apps[0].Id 30
        if (-not $w) { throw 'kein Fenster «AppStart TestApp»' }
        Start-Sleep -Seconds 2   # checks of the TestApp run at start
        Invoke-Button $w 'Eingabeaufforderung starten'
        $deadline = (Get-Date).AddSeconds(10)
        while (-not ((Get-WindowTextControls $w) -contains 'Start blockiert durch Job-Objekt') -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
        $texts = Get-WindowTextControls $w
        $note = if ($texts -contains 'Start blockiert durch Job-Objekt') { '' } else { 'ACHTUNG: «Start blockiert durch Job-Objekt» nicht gefunden' }
        [void](Save-Shot $w 'testapp.png' $note)
        Close-UiWindow $w
    } catch {
        Set-ShotSkip @('testapp.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
    } finally { Stop-TestApps }

    # ================================================================ 4. verify view, console
    Write-Host "`n-- Prüfansicht"
    function Capture-VerifyView([string] $file, [string] $expect) {
        $p = Start-Process $SetupExe -ArgumentList 'verify --ui' -WorkingDirectory $CurDir -PassThru
        try {
            $w = Wait-ProcessWindow $p.Id 60
            if (-not $w) { throw 'Prüfansicht erscheint nicht' }
            $box = Wait-Element $w $expect 90
            if (-not $box) { throw "«$expect» erscheint nicht: $((Get-WindowTexts $w) -join ' | ')" }
            if ($box.Current.IsOffscreen) {
                try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Maximized'); Start-Sleep -Milliseconds 800 } catch { }
            }
            [void](Save-Shot $w $file $(if ($box.Current.IsOffscreen) { 'ACHTUNG: Kasten ausserhalb des sichtbaren Bereichs' } else { '' }))
        } finally { Close-Process $p }
    }
    try { Capture-VerifyView 'pruefansicht-ok.png' 'Einrichtung entspricht dem Soll' }
    catch { Set-ShotSkip @('pruefansicht-ok.png') $_.Exception.Message }

    try {
        Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
        $adminDeviation = $true
        try { Capture-VerifyView 'pruefansicht-abweichung.png' 'Einsatz nicht abschliessen' }
        catch { Set-ShotSkip @('pruefansicht-abweichung.png') $_.Exception.Message }

        Write-Host "`n-- Konsole"
        try {
            # Classic console host (not Windows Terminal) so that the window belongs to a known process.
            $bat = Join-Path $ShotWork 'verify.cmd'
            @(
                '@echo off'
                'chcp 65001 >nul'
                'mode con: cols=110 lines=36'
                'title Eingabeaufforderung'
                "cd /d `"$CurDir`""
                'echo.'
                'echo %CD%^>AppStart.Setup.exe verify'
                'AppStart.Setup.exe verify'
                'echo.'
            ) | Set-Content $bat -Encoding ASCII
            $beforeCmd = @(Get-Process cmd, conhost -ErrorAction SilentlyContinue | ForEach-Object Id)
            $tc = Get-Date
            $host1 = Start-Process conhost.exe -ArgumentList "cmd.exe /k `"$bat`"" -PassThru
            $w = Wait-TopWindow { $_.Current.Name -like '*Eingabeaufforderung*' -and $_.Current.ClassName -eq 'ConsoleWindowClass' } 20
            if (-not $w) { throw 'Konsolenfenster nicht gefunden' }
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
        Set-ShotSkip @('pruefansicht-abweichung.png', 'verify-konsole.png') "Abweichung nicht herstellbar: $($_.Exception.Message)"
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
        Capture-LauncherDialog $op $lnk 'launcher-verweigert.png' 'Start verweigert'
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
        Capture-LauncherDialog $op $lnk 'launcher-nicht-erreichbar.png' 'Start-Dienst nicht erreichbar'
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
            Write-Host '  Hinweis: kein Ereignis 1201 vorhanden'
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
        if (-not $mmc) { throw 'Ereignisanzeige (mmc.exe) startet nicht' }
        $w = Wait-ProcessWindow $mmc.Id 30
        if (-not $w) { throw 'Fenster der Ereignisanzeige nicht gefunden' }
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
        [void](Save-Shot $w 'ereignisanzeige.png' $(if ($selected) { 'Eintrag 1201 markiert' } else { 'ACHTUNG: Eintrag 1201 nicht automatisch markiert, Bild prüfen' }))
        Close-UiWindow $w
        if (-not $mmc.WaitForExit(10000)) { try { $mmc.Kill() } catch { } }
    } catch {
        Set-ShotSkip @('ereignisanzeige.png') "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
        Get-Process mmc -ErrorAction SilentlyContinue | Where-Object { $beforeMmc -notcontains $_.Id } | Stop-Process -Force -ErrorAction SilentlyContinue
    } finally {
        Remove-Item $ViewFile -Force -ErrorAction SilentlyContinue
    }

    # ================================================================ 8. Start menu folder
    Write-Host "`n-- Startmenü"
    try {
        if (-not (Test-Path $StartMenuDir)) { throw "Startmenü-Ordner $StartMenuDir fehlt" }
        Write-Host "  Einträge: $((Get-ChildItem $StartMenuDir | ForEach-Object Name) -join ', ')"
        Start-Process explorer.exe -ArgumentList "`"$StartMenuDir`""
        $w = Wait-TopWindow { $_.Current.ClassName -eq 'CabinetWClass' -and $_.Current.Name -like 'AppStart*' } 20
        if (-not $w) { throw 'Explorer-Fenster «AppStart» nicht gefunden' }
        Start-Sleep -Seconds 1
        [void](Save-Shot $w 'startmenue.png' 'Explorer-Fenster des Startmenü-Ordners')
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
