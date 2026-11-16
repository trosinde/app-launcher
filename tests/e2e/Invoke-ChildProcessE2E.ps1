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

function Invoke-TestAppButton($app, [string] $name) {
    $btn = $app.Window.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)))
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3
}

function Get-TestAppRow($app, [string] $label) {
    # Rows are label, value, badge in this order.
    $texts = @(Get-WindowTextControls $app.Window)
    $i = [Array]::IndexOf($texts, $label)
    if ($i -lt 0 -or $i + 2 -ge $texts.Count) { return '' }
    return "$($texts[$i + 1]) | $($texts[$i + 2])"
}

function Get-OperatorCmd {
    return @(Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object { (Get-ProcessOwner $_) -eq 'appstart-app' })
}

function Stop-OperatorCmd { Get-OperatorCmd | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }

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
    if (Get-AppStartProduct) { throw 'AppStart ist bereits installiert. Test nur auf einer frischen Test-VM (Snapshot) ausführen.' }
    Initialize-WorkDir
    New-TestAppPrerequisites
    $answer = Join-Path $StageDir 'appstart-answer.json'
    Write-Answer $answer

    # ------------------------------------------------------------ installation in one step (spec installation)
    if ($BootstrapperPath) {
        $code = Invoke-AsSystem $BootstrapperPath "/quiet /config `"$answer`""
        $applyLog = @(Get-ChildItem (Join-Path $AppStartData 'Logs') -Filter 'apply-*.log' -ErrorAction SilentlyContinue)
        Check 'Softwareverteilung in einem Schritt: AppStart-Setup.exe /quiet /config als SYSTEM -> Exit-Code 0, eingerichtet, Protokoll in Logs' (
            $code -in 0, 3010 -and (Get-TestAppConfig) -and $applyLog.Count -ge 1) "exit $code, apply-Logs $($applyLog.Count)"
        $bad = Join-Path $StageDir 'appstart-answer-bad.json'
        Write-Answer $bad ', "password": "geheim"'
        $code = Invoke-AsSystem $BootstrapperPath "/quiet /config `"$bad`""
        Check 'Einrichtung scheitert: ungültige Antwortdatei -> Bootstrapper Exit-Code 11' ($code -eq 11) "exit $code"
    } else {
        $code = Install-AppStartMsi $MsiPath (Join-Path $StageDir 'child-msi.log')
        Check 'Vorbereitung: MSI installiert' ($code -eq 0) "exit $code"
        $r = Invoke-Setup @('apply', '--config', $answer)
        Check 'Erste Einrichtung: apply --config -> Exit-Code 0' ($r.Exit -eq 0 -and (Get-TestAppConfig)) "exit $($r.Exit)"
        Skip 'Bootstrapper /quiet /config als SYSTEM' 'ohne -BootstrapperPath'
    }

    $before = (Get-FileHash $ConfigPath).Hash
    $r = Invoke-Setup @('apply', '--config', $answer)
    Check 'Zweite Anwendung: dieselbe Antwortdatei -> nichts geändert, Exit-Code 0' ($r.Exit -eq 0 -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"
    $bad = Join-Path $StageDir 'appstart-answer-missing.json'
    [IO.File]::WriteAllText($bad, '{ "version": 1, "apps": [ { "exe": "C:\\nicht\\da\\App.exe" } ] }')
    $r = Invoke-Setup @('apply', '--config', $bad)
    Check 'Ungültige Datei: Pfad existiert nicht -> nichts geändert, Ausgabe nennt das Feld, Exit-Code 2' (
        $r.Exit -eq 2 -and $r.Text -like '*apps`[0`].exe*' -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"

    $password = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $password
    $op = New-OperatorCredential 'appstart-user' $password
    $script:Lnk = (@(Get-LauncherShortcuts) | Where-Object { $_.Arguments -eq "--app $AppId" } | Select-Object -First 1).Path
    Check 'Vorbereitung: Bedienerkonto und Verknüpfung' ($r.Exit -eq 0 -and $script:Lnk) "lnk=$($script:Lnk)"

    # ------------------------------------------------------------ mode blocked (default)
    $app = Open-TestApp $op
    if ($app) {
        Invoke-TestAppButton $app 'Erlaubtes Hilfsprogramm starten'
        $row = Get-TestAppRow $app 'Hilfsprogramm (TestHelper)'
        Check 'Modus gesperrt: Hilfsprogramm blockiert durch Job-Objekt' ($row -like 'Start blockiert durch Job-Objekt*') $row
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        $row = Get-TestAppRow $app 'Kindprozess (cmd.exe)'
        Check 'Ausbruch über Dateidialog: Modus gesperrt -> cmd.exe blockiert' ($row -like 'Start blockiert durch Job-Objekt*' -and (Get-OperatorCmd).Count -eq 0) $row
    } else { Check 'Modus gesperrt: TestApp gestartet' $false }
    Close-TestApp $app

    # ------------------------------------------------------------ command line (spec installation)
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Cmd)
    Check 'Shell ohne Bestätigung: allow-child cmd.exe -> Exit-Code 2, apps.json unverändert' ($r.Exit -eq 2 -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"
    $public = 'C:\Users\Public\AppStartE2E-Helper.exe'
    Copy-Item $Helper $public -Force
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $public)
    Check 'Beschreibbarer Ordner: Hilfsprogramm in C:\Users\Public -> Exit-Code 1, apps.json unverändert' ($r.Exit -eq 1 -and (Get-FileHash $ConfigPath).Hash -eq $before) "exit $($r.Exit)"
    Remove-Item $public -Force
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Helper)
    $cfg = Get-TestAppConfig
    Check 'Hilfsprogramm per Kommandozeile erlauben: Exit-Code 0, allowlist mit TestHelper, verify lief' (
        $r.Exit -eq 0 -and $cfg.childProcesses -eq 'allowlist' -and @($cfg.allowedChildren).exe -contains $Helper -and $r.Text -like '*Automatische Prüfung*') "exit $($r.Exit)"

    # ------------------------------------------------------------ mode allowlist (spec app-launch «Positivliste»)
    $app = Open-TestApp $op
    if ($app) {
        $t = Get-Date
        Invoke-TestAppButton $app 'Erlaubtes Hilfsprogramm starten'
        $row = Get-TestAppRow $app 'Hilfsprogramm (TestHelper)'
        Check 'Erlaubtes Hilfsprogramm: TestHelper läuft als appstart-app, Info-Ereignis 1101' (
            $row -like 'Gestartet und beendet*appstart-app*' -and (Find-Events 1101 $t "*$Helper*").Count -ge 1) $row
        Check 'Enkelprozess: cmd.exe aus dem TestHelper wird beendet, Ereignis 1206' (
            $row -like '*Enkelprozess cmd.exe: von AppStart beendet*' -and (Find-Events 1206 $t '*cmd.exe*').Count -ge 1) $row
        Save-WindowShot $app.Window (Join-Path $StageDir 'child-allowlist.png')
        $t = Get-Date
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        $row = Get-TestAppRow $app 'Kindprozess (cmd.exe)'
        $ev = Find-Events 1206 $t '*cmd.exe*'
        Check 'Nicht erlaubter Prozess: cmd.exe sofort beendet, Warn-Ereignis 1206 mit Benutzer, AppId, Pfad, Grund' (
            $row -like 'Von AppStart sofort beendet*' -and $ev.Count -ge 1 -and $ev[0].Message -like '*appstart-user*' -and $ev[0].Message -like "*$AppId*" -and $ev[0].Message -like '*Grund:*' -and (Get-OperatorCmd).Count -eq 0) $row
    } else { Check 'Modus allowlist: TestApp gestartet' $false }
    Close-TestApp $app

    # ------------------------------------------------------------ changed helper (app-launch «Veränderte Datei», verification «Hilfsprogramm verändert»)
    $helperBackup = Join-Path $WorkDir 'AppStart.TestHelper.exe.orig'
    Copy-Item $Helper $helperBackup -Force
    Set-OneByteChanged $Helper
    $app = Open-TestApp $op
    if ($app) {
        $t = Get-Date
        Invoke-TestAppButton $app 'Erlaubtes Hilfsprogramm starten'
        $row = Get-TestAppRow $app 'Hilfsprogramm (TestHelper)'
        Check 'Veränderte Datei: TestHelper beendet, Ereignis nennt «Prüfsumme stimmt nicht»' (
            $row -like 'Von AppStart sofort beendet*' -and (Find-Events 1206 $t '*Prüfsumme stimmt nicht*').Count -ge 1) $row
    } else { Check 'Veränderte Datei: TestApp gestartet' $false }
    Close-TestApp $app
    $r = Invoke-Setup @('verify')
    Check 'Hilfsprogramm verändert: verify meldet Abweichung mit Pfad und rehash-Hinweis' (
        $r.Exit -ne 0 -and $r.Text -like "*rehash --app $AppId --child*$Helper*") "exit $($r.Exit)"
    Copy-Item $helperBackup $Helper -Force
    $r = Invoke-Setup @('verify') -Quiet
    Check 'Original zurück: verify wieder Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ app ends before the helper, service stops
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Cmd, '--allow-shell') -Quiet
    Check 'Vorbereitung: cmd.exe mit --allow-shell erlaubt' ($r.Exit -eq 0) "exit $($r.Exit)"
    $app = Open-TestApp $op
    if ($app) {
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        # Not $cmd: PowerShell variable names ignore case, that would overwrite $Cmd (the path).
        $cmdProcs = @(Get-OperatorCmd)
        Close-TestApp $app
        Start-Sleep -Seconds 3
        $still = @(Get-OperatorCmd | Where-Object { $cmdProcs.ProcessId -contains $_.ProcessId })
        Check 'Applikation endet vor dem Hilfsprogramm: erlaubtes cmd.exe läuft nach dem Ende der TestApp weiter' ($cmdProcs.Count -ge 1 -and $still.Count -ge 1) "cmd vorher $($cmdProcs.Count), nachher $($still.Count)"
        Stop-Service AppStart
        Start-Sleep -Seconds 3
        $after = @(Get-OperatorCmd | Where-Object { $cmdProcs.ProcessId -contains $_.ProcessId })
        Check 'Dienst stoppt: Windows beendet alle Prozesse des Job-Objekts (KILL_ON_JOB_CLOSE)' ($still.Count -ge 1 -and $after.Count -eq 0) "nach Stopp $($after.Count)"
        Start-Service AppStart
        Start-Sleep -Seconds 5
    } else { Check 'Applikation endet vor dem Hilfsprogramm: TestApp gestartet' $false }
    Stop-OperatorCmd
    $r = Invoke-Setup @('deny-child', '--app', $AppId, '--exe', $Cmd)
    Check 'Vorbereitung: cmd.exe wieder entfernt' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ mode all (verification «Alle weiteren Programme erlaubt»)
    $r = Invoke-Setup @('set-children', '--app', $AppId, '--mode', 'all') -Quiet
    $v = Invoke-Setup @('verify')
    Check 'Alle weiteren Programme erlaubt: verify erfüllt, Hinweis mit AppId' (
        $r.Exit -eq 0 -and $v.Exit -eq 0 -and $v.Text -like "*Alle weiteren Programme erlaubt ($AppId)*") "exit $($v.Exit)"
    $r = Invoke-Setup @('allow-child', '--app', $AppId, '--exe', $Helper) -Quiet
    Check 'Vorbereitung: wieder allowlist mit TestHelper' ($r.Exit -eq 0 -and (Get-TestAppConfig).childProcesses -eq 'allowlist') "exit $($r.Exit)"

    # ------------------------------------------------------------ learn mode (spec app-launch «Lernmodus»)
    $t = Get-Date
    $r = Invoke-Setup @('learn', 'start', '--app', $AppId, '--minutes', '5')
    Check 'Lernmodus starten: Exit-Code 0, Ereignis 1400' ($r.Exit -eq 0 -and (Get-AppStartEvents 1400 $t).Count -ge 1) "exit $($r.Exit)"
    $v = Invoke-Setup @('verify')
    Check 'Lernmodus vergessen: verify meldet Abweichung mit Ablaufzeit und learn stop' ($v.Exit -ne 0 -and $v.Text -like "*Lernmodus aktiv bis*learn stop --app $AppId*") "exit $($v.Exit)"
    $app = Open-TestApp $op
    if ($app) {
        $t = Get-Date
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        $row = Get-TestAppRow $app 'Kindprozess (cmd.exe)'
        $show = Invoke-Setup @('learn', 'show', '--app', $AppId)
        Check 'Aufzeichnung: cmd.exe läuft im Lernmodus und steht mit Prüfwerten in der Aufzeichnung, Ereignis 1102' (
            $row -like 'Gestartet*' -and $show.Text -like '*cmd.exe – Shell oder Skript-Host*' -and (Get-AppStartEvents 1102 $t).Count -ge 1) $row
        Stop-OperatorCmd
        Invoke-TestAppButton $app 'Erlaubtes Hilfsprogramm starten'
    } else { Check 'Lernmodus: TestApp gestartet' $false }
    Close-TestApp $app
    $r = Invoke-Setup @('learn', 'apply', '--app', $AppId, '--all')
    $cfg = Get-TestAppConfig
    Check 'learn apply --all: Shell nicht übernommen, Lernmodus beendet' (
        $r.Exit -eq 0 -and @($cfg.allowedChildren).exe -notcontains $Cmd -and (Invoke-Setup @('verify') -Quiet).Exit -eq 0) "exit $($r.Exit)"

    # «Zeitablauf»: learn mode ends in a running instance (duration 5 min, 40 s left when the app starts).
    $now = (Get-Date).ToUniversalTime()
    Write-LearnState $now.AddSeconds(-260) $now.AddSeconds(40)
    $app = Open-TestApp $op
    if ($app) {
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        $first = Get-TestAppRow $app 'Kindprozess (cmd.exe)'
        Stop-OperatorCmd
        while ((Get-Date).ToUniversalTime() -lt $now.AddSeconds(45)) { Start-Sleep -Seconds 1 }
        $t = Get-Date
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        $second = Get-TestAppRow $app 'Kindprozess (cmd.exe)'
        Check 'Zeitablauf: nach Ablauf beendet der Dienst cmd.exe in der laufenden Instanz, Ereignis 1401' (
            $first -like 'Gestartet*' -and $second -like 'Von AppStart sofort beendet*' -and (Get-AppStartEvents 1401 $t).Count -ge 1) "vorher: $first / nachher: $second"
    } else { Check 'Zeitablauf: TestApp gestartet' $false }
    Close-TestApp $app
    Stop-OperatorCmd

    # «Manipulierte Steuerdatei»: learn.json writable for Users -> ignored with error event, allowlist applies.
    $now = (Get-Date).ToUniversalTime()
    Write-LearnState $now $now.AddMinutes(30)
    icacls $LearnPath /grant '*S-1-5-32-545:(M)' | Out-Null
    $app = Open-TestApp $op
    if ($app) {
        $t = $app.Since
        Invoke-TestAppButton $app 'Eingabeaufforderung starten'
        $row = Get-TestAppRow $app 'Kindprozess (cmd.exe)'
        Check 'Manipulierte Steuerdatei: Lernmodus ignoriert, Fehler-Ereignis 1207, cmd.exe beendet' (
            (Get-AppStartEvents 1207 $t).Count -ge 1 -and $row -like 'Von AppStart sofort beendet*') $row
    } else { Check 'Manipulierte Steuerdatei: TestApp gestartet' $false }
    Close-TestApp $app
    Stop-OperatorCmd
    $r = Invoke-Setup @('learn', 'stop', '--app', $AppId) -Quiet
    $v = Invoke-Setup @('verify') -Quiet
    Check 'Aufräumen: learn stop schützt learn.json wieder, verify Exit-Code 0' ($r.Exit -eq 0 -and $v.Exit -eq 0) "verify $($v.Exit)"
}
catch {
    Check 'Testlauf ohne Ausnahme' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    Stop-OperatorCmd
    if ($helperBackup -and (Test-Path $helperBackup) -and (Test-Path $Helper)) { Copy-Item $helperBackup $Helper -Force -ErrorAction SilentlyContinue }
    Remove-Item 'C:\Users\Public\AppStartE2E-Helper.exe' -Force -ErrorAction SilentlyContinue
    Invoke-E2ECleanup
    Write-Summary
    Stop-Transcript | Out-Null
}
