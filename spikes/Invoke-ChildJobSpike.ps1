<#
  Spike 1.1 (add-allowed-child-processes) – job object with completion port.
  Runs as the current user. Installs nothing, changes no accounts or rights.
  Logs go to $Out (default %LOCALAPPDATA%\AppStartSpike\childjob-logs).

    .\Invoke-ChildJobSpike.ps1                 # all runs A–E
    .\Invoke-ChildJobSpike.ps1 -Runs A,C       # selected runs
#>
param(
    [string[]] $Runs = @('A', 'B', 'C', 'D', 'E'),
    [string] $Exe = (Join-Path $env:LOCALAPPDATA 'AppStartSpike\childjob\AppStart.Spike.ChildJob.exe'),
    [string] $Out = (Join-Path $env:LOCALAPPDATA 'AppStartSpike\childjob-logs')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null

# Minimal valid PDF for the "pdf" scenario.
$pdf = Join-Path $Out 'spike.pdf'
if (-not (Test-Path $pdf)) {
    $body = "%PDF-1.4`n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj`n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj`n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj`ntrailer<</Root 1 0 R>>`n%%EOF`n"
    [IO.File]::WriteAllText($pdf, $body)
}

function Invoke-Run([string] $name, [string[]] $monitorArgs, [scriptblock] $during) {
    $log = Join-Path $Out "run-$name.log"
    Remove-Item $log -ErrorAction SilentlyContinue
    $all = @('monitor') + $monitorArgs[0..($monitorArgs.IndexOf('--') - 1)] + @('--log', "`"$log`"") + $monitorArgs[$monitorArgs.IndexOf('--')..($monitorArgs.Count - 1)]
    $p = Start-Process -FilePath $Exe -ArgumentList $all -PassThru
    if ($during) { & $during $p $log } else { $p.WaitForExit(300000) | Out-Null }
    Write-Host "`n===== Run $name (monitor exit: $(if ($p.HasExited) { $p.ExitCode } else { 'running' })) ====="
    Get-Content $log
}

$delegation = (Get-ItemProperty 'HKCU:\Console\%%Startup' -ErrorAction SilentlyContinue).DelegationConsole
Write-Host "Windows $([Environment]::OSVersion.Version), default terminal delegation: $(if ($delegation) { $delegation } else { '(not set = let Windows decide)' })"

if ('A' -in $Runs) {
    # Learn: nothing is killed, shows which processes appear as NEW_PROCESS.
    Invoke-Run 'A-learn' @('--mode', 'learn', '--', 'console', 'console-hidden', 'ping', 'crash', 'grandchild', 'pdf', '--pdf', "`"$pdf`"") $null
}
if ('B' -in $Runs) {
    # Enforce, nothing extra allowed: cmd, ping, WerFault, viewer must be killed; time to kill is measured.
    Invoke-Run 'B-enforce' @('--mode', 'enforce', '--', 'console', 'console-hidden', 'ping', 'crash', 'grandchild', 'pdf', '--pdf', "`"$pdf`"") $null
}
if ('C' -in $Runs) {
    # Service stop: allowed ping keeps running, then the monitor is killed hard (like a crashing service).
    Invoke-Run 'C-killonclose' @('--mode', 'enforce', '--allow', 'ping.exe', '--', 'longrun') {
        param($p, $log)
        Start-Sleep -Seconds 4
        $before = @(Get-CimInstance Win32_Process -Filter "Name='ping.exe'" | Where-Object CommandLine -like '*-n 120*')
        Stop-Process -Id $p.Id -Force
        Start-Sleep -Seconds 2
        $after = @(Get-CimInstance Win32_Process -Filter "Name='ping.exe'" | Where-Object CommandLine -like '*-n 120*')
        $probe = @(Get-CimInstance Win32_Process -Filter "Name='AppStart.Spike.ChildJob.exe'")
        Add-Content $log "SCRIPT monitor killed hard; ping -n 120 before=$($before.Count) after=$($after.Count); spike processes left=$($probe.Count)"
    }
}
if ('D' -in $Runs) {
    # Application ends before its allowed helper: monitor must keep watching until ACTIVE_PROCESS_ZERO.
    Invoke-Run 'D-exitearly' @('--mode', 'enforce', '--allow', 'ping.exe', '--', 'exit-early') $null
}
if ('E' -in $Runs) {
    # Repeat the kill measurement 20 times for a distribution.
    Invoke-Run 'E-timing' @('--mode', 'enforce', '--', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping', 'ping') $null
}
