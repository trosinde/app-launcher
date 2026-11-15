#Requires -RunAsAdministrator
<#
  Acceptance test with the TestApp (task 8.3, concept «Abnahmetest mit der Testapplikation», 7 steps)
  plus abuse attempts of a real operator (task 8.2). Only on the TEST VM.

  MSI install -> add TestApp (data folder C:\ProgramData\AppStartTest, key HKLM\SOFTWARE\AppStartTest)
  -> add-user appstart-user (random password per run) -> the 7 steps, each as the REAL operator:
  processes of appstart-user are started with Start-Process -Credential (CreateProcessWithLogonW),
  i.e. a genuine standard user token with membership in «AppStart Users» and without admin rights.
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
    if (Get-AppStartProduct) { throw 'AppStart ist bereits installiert. Test nur auf einer frischen Test-VM (Snapshot) ausführen.' }
    Initialize-WorkDir
    New-TestAppPrerequisites

    # ------------------------------------------------------------ preparation
    $code = Install-AppStartMsi $MsiPath (Join-Path $StageDir 'acceptance-msi.log')
    Check 'Vorbereitung: MSI installiert' ($code -eq 0) "exit $code"
    $r = Invoke-Setup @('add', '--exe', $PackagedTestApp, '--data-dir', $TestDataDir, '--reg-key', $TestRegKeyCli, '--name', $TestAppName)
    Check 'Vorbereitung: TestApp freigegeben (add)' ($r.Exit -eq 0) ''
    $password = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
    Check 'Vorbereitung: appstart-user angelegt (add-user)' ($r.Exit -eq 0 -and (Get-LocalUser appstart-user -ErrorAction SilentlyContinue)) ''
    $op = New-OperatorCredential 'appstart-user' $password
    $appId = Get-TestAppId $PackagedTestApp
    $lnk = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$TestAppName.lnk"
    if (-not (Test-Path $lnk)) { $lnk = (@(Get-LauncherShortcuts) | Where-Object { $_.Arguments -eq "--app $appId" } | Select-Object -First 1).Path }
    Check 'Vorbereitung: AppId und Verknüpfung vorhanden' ($appId -and $lnk -and (Test-Path $lnk)) "id=$appId lnk=$lnk"

    $who = Invoke-AsOperator $op 'whoami'
    Check 'Bediener-Token echt: nicht erhöht, nicht in Administratoren, in AppStart Users' ($who -like '*elevated=False*' -and $who -like '*adminsInToken=False*' -and $who -like '*appstartUsers=True*') $who

    # ------------------------------------------------------------ step 1: start through the shortcut
    Start-Sleep -Seconds 3
    $t = Get-Date
    Start-ShortcutAs $op $lnk | Out-Null
    $apps = @(Wait-TestApp $t)
    $mySession = (Get-Process -Id $PID).SessionId
    Check 'Schritt 1: Start über Verknüpfung als appstart-user -> TestApp läuft als appstart-app in der Bediener-Session' ($apps.Count -ge 1 -and $apps[0].Session -eq $mySession) "owner=$($apps[0].Owner) session=$($apps[0].Session)"
    if (-not $apps) {
        $dialogs = @(Get-Process AppStart.Launcher -ErrorAction SilentlyContinue | ForEach-Object { Get-ProcessWindows $_.Id } | ForEach-Object { (Get-WindowTexts $_) -join ' | ' })
        if ($dialogs) { Write-Host "  Launcher-Meldung: $($dialogs -join ' / ')" }
    }
    $win = if ($apps) { Wait-ProcessWindow $apps[0].Id 20 } else { $null }
    if ($win) {
        Start-Sleep -Seconds 2   # checks of the TestApp run at start
        $texts = Get-WindowTextControls $win
        $joined = $texts -join ' | '
        Check 'Schritt 1: alle Anzeigen grün (Konto appstart-app, Admin ja, Registry OK, Dateien OK)' (
            $joined -like '*appstart-app | OK | Admin-Rechte | Ja | OK | Registry lesen/schreiben | HKLM\SOFTWARE\AppStartTest | OK | Dateien lesen/schreiben | C:\ProgramData\AppStartTest | OK*' -and
            $texts -notcontains 'Fehler' -and $texts -notcontains 'Nicht via AppStart') $joined
        Save-WindowShot $win (Join-Path $StageDir 'acceptance-step1.png')

        # ---------------------------------------------------------- step 5: «Eingabeaufforderung starten»
        $btn = $win.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'Eingabeaufforderung starten')))
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 2
        $texts = Get-WindowTextControls $win
        $cmd = @(Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object { (Get-ProcessOwner $_) -eq 'appstart-app' })
        Check 'Schritt 5: «Eingabeaufforderung starten» scheitert (Start blockiert durch Job-Objekt)' ($texts -contains 'Start blockiert durch Job-Objekt' -and $cmd.Count -eq 0) "cmd als appstart-app: $($cmd.Count)"
        Save-WindowShot $win (Join-Path $StageDir 'acceptance-step5.png')
        Close-UiWindow $win
    } else {
        Check 'Schritt 1: TestApp-Fenster gefunden' $false
        Check 'Schritt 5: «Eingabeaufforderung starten» scheitert' $false 'kein TestApp-Fenster'
    }
    Stop-TestApps

    # ------------------------------------------------------------ steps 2 and 3: registry and data folder
    $r = Invoke-AsOperator $op 'reg-read'
    Check 'Schritt 2: appstart-user öffnet HKLM\SOFTWARE\AppStartTest -> Zugriff verweigert' ($r -eq 'open=DENIED') $r
    $r = Invoke-AsOperator $op 'data-read'
    Check 'Schritt 3: appstart-user öffnet C:\ProgramData\AppStartTest -> Zugriff verweigert' ($r -eq 'list=DENIED write=DENIED') $r

    # ------------------------------------------------------------ step 4: TestApp EXE directly
    $r = Invoke-AsOperator $op 'start-direct' $PackagedTestApp
    Check 'Schritt 4: TestApp-EXE direkt als appstart-user -> Abbruch (740 ERROR_ELEVATION_REQUIRED)' ($r -eq 'start=E740') $r
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
    Check 'Schritt 4: TestApp ohne Admin-Rechte (RunAsInvoker) bricht mit Meldung ab, Exit-Code 1' ($msg -like '*benötigt Administratorrechte*' -and $r -eq 'exit=1') "$r :: $msg"
    Stop-TestApps

    # ------------------------------------------------------------ step 6: one byte of the EXE changed
    Start-Sleep -Seconds 3
    $exeBackup = Join-Path $WorkDir 'AppStart.TestApp.exe.orig'
    Copy-Item $PackagedTestApp $exeBackup -Force
    Set-OneByteChanged $PackagedTestApp
    Check 'Schritt 6: ein Byte der TestApp-EXE geändert' ((Get-FileHash $PackagedTestApp).Hash -ne (Get-FileHash $exeBackup).Hash) ''
    $t = Get-Date
    $l = Start-LauncherAs $op "--app $appId" 'acceptance-step6.png'
    Start-Sleep -Seconds 2
    $started = @(Get-TestAppProcesses $t)
    $ev = Get-AppStartEvents 1201 $t
    Check 'Schritt 6: Start über Launcher verweigert (Exit-Code 3, «Start verweigert», Ereignis-ID)' ($l.Exit -eq 3 -and $l.Texts -like '*Start verweigert*' -and $l.Texts -like '*Ereignis-ID*' -and $started.Count -eq 0) "exit=$($l.Exit) :: $($l.Texts)"
    Check 'Schritt 6: Ereignis 1201 (DeniedIntegrity) protokolliert' ($ev.Count -ge 1) "$($ev.Count) Ereignis(se)"
    $r = Invoke-Setup @('verify')
    Check 'Schritt 6: verify meldet die veränderte EXE (Exit-Code ungleich 0)' ($r.Exit -ne 0) "exit $($r.Exit)"
    Copy-Item $exeBackup $PackagedTestApp -Force
    Check 'Schritt 6: Original-EXE zurückgestellt' ((Get-FileHash $PackagedTestApp).Hash -eq (Get-FileHash $exeBackup).Hash) ''

    # ------------------------------------------------------------ step 7: verify
    $report = Join-Path $StageDir 'acceptance-verify.html'
    $r = Invoke-Setup @('verify', '--report', $report)
    Check 'Schritt 7: verify -> Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
    $r = Invoke-Setup @('verify')
    Check 'Schritt 7: appstart-user in Administratoren -> verify Exit-Code ungleich 0, nennt appstart-user' ($r.Exit -ne 0 -and $r.Text -like '*appstart-user*') "exit $($r.Exit)"
    Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
    $regSddl = Get-Sddl $TestRegKey
    $acl = Get-Acl $TestRegKey
    $acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')), 'ReadKey', 'ContainerInherit', 'None', 'Allow')))
    Set-Acl $TestRegKey $acl
    $r = Invoke-Setup @('verify')
    Check 'Schritt 7: Registry-Key für Benutzer lesbar -> verify Exit-Code ungleich 0' ($r.Exit -ne 0) "exit $($r.Exit)"
    $acl = Get-Acl $TestRegKey; $acl.PurgeAccessRules((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545'))); Set-Acl $TestRegKey $acl
    Check 'Schritt 7: Rechte Registry-Key wiederhergestellt' ((Get-Sddl $TestRegKey) -eq $regSddl) ''
    $r = Invoke-Setup @('verify')
    Check 'Schritt 7: nach Korrektur wieder Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ abuse attempts of the operator (8.2)
    $r = Invoke-AsOperator $op 'config'
    Check 'Missbrauch: apps.json lesen/überschreiben/ersetzen/löschen/umbenennen, neue Datei -> alles verweigert' (
        $r -eq 'read=DENIED openWrite=DENIED replace=DENIED delete=DENIED rename=DENIED newFile=DENIED') $r
    $r = Invoke-AsOperator $op 'pipe'
    Check 'Missbrauch: Bediener darf verbinden, aber keine eigene Pipe-Instanz anlegen' ($r -eq 'connect=ALLOWED createInstance=DENIED') $r
    $r = Invoke-AsOperator $op 'progfiles'
    $allowed = @(($r -split ' ') | Where-Object { $_ -notlike '*=DENIED' })
    Check 'Missbrauch: nichts in C:\Program Files\AppStart schreiben, ersetzen oder umbenennen' ($allowed.Count -eq 0) $r
    $r = Invoke-AsOperator $op 'start-direct' $SetupExe
    Check 'Missbrauch: AppStart.Setup.exe verify als Bediener nur mit Admin-Rechten (740)' ($r -eq 'start=E740') $r

    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op '--app unknown-app' 'acceptance-unknown.png'
    Check 'Missbrauch: unbekannte AppId -> «Start verweigert», Exit-Code 3, Ereignis 1200' ($l.Exit -eq 3 -and $l.Texts -like '*Start verweigert*' -and (Get-AppStartEvents 1200 $t).Count -ge 1) "exit=$($l.Exit)"
    Start-Sleep -Seconds 3
    $l = Start-LauncherAs $op '' 'acceptance-noapp.png'
    Check 'Missbrauch: Launcher ohne --app -> «Ungültige Verknüpfung», Exit-Code 1' ($l.Exit -eq 1 -and $l.Texts -like '*Ungültige Verknüpfung*') "exit=$($l.Exit)"
    $l = Start-LauncherAs $op "--app $appId C:\Windows\System32\cmd.exe" ''
    Check 'Missbrauch: Launcher mit Zusatzargument -> «Ungültige Verknüpfung», Exit-Code 1' ($l.Exit -eq 1 -and $l.Texts -like '*Ungültige Verknüpfung*') "exit=$($l.Exit)"

    # Still everything fine after the attempts
    $r = Invoke-Setup @('verify') -Quiet
    Check 'Nach allen Versuchen: verify Exit-Code 0, apps.json unverändert vorhanden' ($r.Exit -eq 0 -and (Test-Path $ConfigPath)) "exit $($r.Exit)"
}
catch {
    Check 'Testlauf ohne Ausnahme' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    if ($exeBackup -and (Test-Path $exeBackup) -and (Test-Path $PackagedTestApp)) { Copy-Item $exeBackup $PackagedTestApp -Force -ErrorAction SilentlyContinue }
    Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user' -ErrorAction SilentlyContinue
    Invoke-E2ECleanup
    Write-Summary
    Stop-Transcript | Out-Null
}
