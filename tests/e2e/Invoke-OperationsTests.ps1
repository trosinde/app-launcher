#Requires -RunAsAdministrator
<#
  Operations tests (task 8.4, automatable part). Only on the TEST VM.
    1. Second operator appstart-op2 outside «AppStart Users» -> «Keine Berechtigung», then added with
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
    $s['Lokale Konten'] = (@(Get-LocalUser | Where-Object { $_.Name -ne $op2Name } | ForEach-Object Name) | Sort-Object) -join ','
    $s['Lokale Gruppen'] = (@(Get-LocalGroup | ForEach-Object Name) | Sort-Object) -join ','
    $s['Dienst AppStart'] = [string][bool](Get-Service AppStart -ErrorAction SilentlyContinue)
    $s['Programmordner'] = [string](Test-Path $ProgDir)
    $s['apps.json'] = [string](Test-Path $ConfigPath)
    $s['Startmenü AppStart'] = [string](Test-Path $StartMenuDir)
    $s['Launcher-Verknüpfungen'] = (@(Get-LauncherShortcuts | ForEach-Object Path) | Sort-Object) -join ','
    $s['Datenordner SDDL'] = (Get-Sddl $TestDataDir) -replace 'AI', ''
    $s['Registry-Key SDDL'] = (Get-Sddl $TestRegKey) -replace 'AI', ''
    $s['Registry-Wert'] = (Get-ItemProperty $TestRegKey).Password
    $s['Produkt'] = [string][bool](Get-AppStartProduct)
    return $s
}

function Start-Pair([pscredential] $a, [pscredential] $b, [string] $appId, [int] $delayMs) {
    # Starts both launchers, the second after $delayMs; returns the two results.
    $pa = Start-Process $LauncherExe -Credential $a -ArgumentList "--app $appId" -WorkingDirectory $CurDir -PassThru
    if ($delayMs) { Start-Sleep -Milliseconds $delayMs }
    $pb = Start-Process $LauncherExe -Credential $b -ArgumentList "--app $appId" -WorkingDirectory $CurDir -PassThru
    $null = $pa.Handle; $null = $pb.Handle
    # Dialogs of both are read in turn
    $ra = Wait-ProcessHandlingDialogs $pa 30 ''
    $rb = Wait-ProcessHandlingDialogs $pb 30 ''
    Write-Host "  op1 exit=$($ra.Exit) $($ra.Texts) | op2 exit=$($rb.Exit) $($rb.Texts)"
    return @($ra, $rb)
}

try {
    Write-Host "== Windows $((Get-CimInstance Win32_OperatingSystem).Version), Session $((Get-Process -Id $PID).SessionId), MSI $MsiPath"
    if (Get-AppStartProduct) { throw 'AppStart ist bereits installiert. Test nur auf einer frischen Test-VM (Snapshot) ausführen.' }
    Initialize-WorkDir
    New-TestAppPrerequisites
    $original = Get-OriginalState

    $code = Install-AppStartMsi $MsiPath (Join-Path $StageDir 'operations-msi.log')
    Check 'Vorbereitung: MSI installiert' ($code -eq 0) "exit $code"
    $r = Invoke-Setup @('add', '--exe', $PackagedTestApp, '--data-dir', $TestDataDir, '--reg-key', $TestRegKeyCli, '--name', $TestAppName)
    Check 'Vorbereitung: TestApp freigegeben' ($r.Exit -eq 0) ''
    $pw1 = New-RandomPassword
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') $pw1
    Check 'Vorbereitung: appstart-user angelegt' ($r.Exit -eq 0) ''
    $op1 = New-OperatorCredential 'appstart-user' $pw1
    $pw2 = New-RandomPassword
    New-LocalTestUser $op2Name $pw2
    $op2 = New-OperatorCredential $op2Name $pw2
    $appId = Get-TestAppId $PackagedTestApp
    $mySession = (Get-Process -Id $PID).SessionId

    # ------------------------------------------------------------ 1. user outside the group, then added
    $l = Start-LauncherAs $op2 "--app $appId" 'operations-nopermission.png'
    Check 'Benutzer ausserhalb von AppStart Users: «Keine Berechtigung», Exit-Code 4, kein Start' ($l.Exit -eq 4 -and $l.Texts -like '*Keine Berechtigung*' -and @(Get-TestAppProcesses).Count -eq 0) "exit=$($l.Exit)"
    $r = Invoke-Setup @('add-user', '--member', "$env:COMPUTERNAME\$op2Name")
    $isMember = [bool](Get-LocalGroupMember -Group 'AppStart Users' -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*\$op2Name" })
    Check 'add-user --member: zweiter Bediener in AppStart Users' ($r.Exit -eq 0 -and $isMember) ''
    $r = Invoke-Setup @('verify') -Quiet
    Check 'verify mit zwei Bedienern: Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ 2. two operators
    Start-Sleep -Seconds 3
    $t = Get-Date
    $res = Start-Pair $op1 $op2 $appId 3000   # more than the 2 s cooldown per session
    $apps = @(Wait-TestApp $t 2)
    Check 'Zwei Bediener nacheinander (3 s Abstand): beide Starts OK, zwei TestApps als appstart-app' ($res[0].Exit -eq 0 -and $res[1].Exit -eq 0 -and $apps.Count -eq 2) "exit=$($res[0].Exit)/$($res[1].Exit) apps=$($apps.Count)"
    Check 'Zwei Bediener: beide TestApps in der Session der Anfragenden' (@($apps | Where-Object { $_.Session -ne $mySession }).Count -eq 0 -and $apps.Count -eq 2) (($apps | ForEach-Object { "pid $($_.Id) session $($_.Session)" }) -join ', ')
    Stop-TestApps

    Start-Sleep -Seconds 3
    $t = Get-Date
    $res = Start-Pair $op1 $op2 $appId 0
    $apps = @(Wait-TestApp $t 1)
    Start-Sleep -Seconds 2; $apps = @(Get-TestAppProcesses $t | Where-Object { $_.Owner -eq 'appstart-app' })
    $okCount = @($res | Where-Object { $_.Exit -eq 0 }).Count
    $busy = @($res | Where-Object { $_.Exit -eq 3 -and $_.Texts -like '*Start läuft bereits*' }).Count
    # Same session (Start-Process -Credential): the per-session gate lets one through and answers «busy» to the other.
    # With two real sessions both would start: manual test M3.
    Check 'Zwei Bediener gleichzeitig in derselben Session: einer startet, der andere erhält «Start läuft bereits»' ($okCount -eq 1 -and $busy -eq 1 -and $apps.Count -eq 1) "ok=$okCount busy=$busy apps=$($apps.Count)"
    Check 'Ereignis 1203 (DeniedBusy) protokolliert' ((Get-AppStartEvents 1203 $t).Count -ge 1) ''
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
    Check 'Dienstprozess beendet -> SCM startet den Dienst neu (Wiederherstellung)' ($newPid -ne 0) "alt $oldPid, neu $newPid nach ~$restartSec s"
    $scmEvent = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Service Control Manager'; Id = 7031; StartTime = $t } -ErrorAction SilentlyContinue)
    Check 'System-Ereignis 7031 (Dienst unerwartet beendet, Neustart) vorhanden' ($scmEvent.Count -ge 1) ''
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op1 "--app $appId" ''
    $apps = @(Wait-TestApp $t)
    Check 'Nach Neustart des Dienstes: Start als appstart-user funktioniert' ($l.Exit -eq 0 -and $apps.Count -ge 1) "exit=$($l.Exit)"
    Stop-TestApps

    # ------------------------------------------------------------ 4. application update + rehash
    $changed = Join-Path $WorkDir 'AppStart.TestApp.update.exe'
    Copy-Item $PackagedTestApp $changed -Force
    Set-OneByteChanged $changed
    Copy-Item $changed $PackagedTestApp -Force   # the "update": EXE replaced by a different build
    Check 'Update der Applikation: EXE durch veränderte Kopie ersetzt' ((Get-FileHash $PackagedTestApp).Hash -eq (Get-FileHash $changed).Hash) ''
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op1 "--app $appId" 'operations-update-denied.png'
    Check 'Update ohne rehash: Start verweigert (Exit-Code 3), Ereignis 1201' ($l.Exit -eq 3 -and $l.Texts -like '*Start verweigert*' -and (Get-AppStartEvents 1201 $t).Count -ge 1 -and @(Get-TestAppProcesses $t).Count -eq 0) "exit=$($l.Exit)"
    $r = Invoke-Setup @('rehash', '--app', $appId)
    Check 'rehash --app: Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Start-Sleep -Seconds 3
    $t = Get-Date
    $l = Start-LauncherAs $op1 "--app $appId" ''
    $apps = @(Wait-TestApp $t)
    Check 'Nach rehash: Start der aktualisierten Applikation funktioniert' ($l.Exit -eq 0 -and $apps.Count -ge 1) "exit=$($l.Exit)"
    Stop-TestApps
    $r = Invoke-Setup @('verify') -Quiet
    Check 'Nach rehash: verify Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"

    # ------------------------------------------------------------ 5. uninstall
    $r = Invoke-Setup @('uninstall')
    Check 'uninstall (CLI): Exit-Code 0' ($r.Exit -eq 0) "exit $($r.Exit)"
    Remove-LocalUser $op2Name   # test user, not created by AppStart (excluded from the comparison)
    $codes = @(Uninstall-AppStartMsi)
    Check 'MSI-Deinstallation: Exit-Code 0' ($codes.Count -ge 1 -and @($codes | Where-Object { $_ -ne 0 }).Count -eq 0) ($codes -join ',')
    $now = Get-OriginalState
    $diff = @(foreach ($k in $original.Keys) { if ($original[$k] -ne $now[$k]) { "$k : '$($original[$k])' -> '$($now[$k])'" } })
    $diff | ForEach-Object { Write-Host "  ABWEICHUNG $_" }
    Check 'Deinstallation: ursprünglicher Zustand (Konten, Gruppe, Dienst, Programmordner, Verknüpfungen, Rechte)' ($diff.Count -eq 0) "$($diff.Count) Abweichung(en)"
    # Information only, not part of the spec scenario
    $profiles = @(Get-CimInstance Win32_UserProfile | Where-Object { (Split-Path $_.LocalPath -Leaf) -like 'appstart-*' } | ForEach-Object LocalPath)
    Write-Host "  INFO verbleibende Profile: $(if ($profiles) { $profiles -join ', ' } else { 'keine' })"
    Write-Host "  INFO Ereignisquelle AppStart nach Deinstallation: $([System.Diagnostics.EventLog]::SourceExists('AppStart'))"
    Write-Host "  INFO C:\ProgramData\AppStart nach Deinstallation: $(if (Test-Path $AppStartData) { (Get-ChildItem $AppStartData -Recurse | ForEach-Object { $_.FullName.Substring($AppStartData.Length) }) -join ', ' } else { 'entfernt' })"
}
catch {
    Check 'Testlauf ohne Ausnahme' $false "$($_.Exception.Message) @ $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    Invoke-E2ECleanup -extraUsers @($op2Name)
    Write-Summary
    Stop-Transcript | Out-Null
}
