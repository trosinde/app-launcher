#Requires -RunAsAdministrator
<#
  Spike 1.1/1.2/4.5 – runs AppStart.Spike.Launch as SYSTEM (session 0, like the later service)
  via a one-shot scheduled task. TEST VM ONLY.

  Variants (each run writes logs to C:\ProgramData\AppStartSpike):
    .\Run-Spike.ps1 -Variant S4UBatch            # 1.1 S4U, logon type Batch  (expected primary path)
    .\Run-Spike.ps1 -Variant S4UNetwork          # 1.1 S4U, logon type Network
    .\Run-Spike.ps1 -Variant S4UInteractive      # 1.1 must FAIL: interactive logon is denied
    .\Run-Spike.ps1 -Variant NoDesktopAcl        # 1.2 control run: expect 0xC0000142 / invisible window
    .\Run-Spike.ps1 -Variant Job                 # 4.5 probe tries cmd.exe inside job -> must be BLOCKED
  -Session defaults to the active console session (the signed-in operator).
#>
param(
    [ValidateSet('S4UBatch', 'S4UNetwork', 'S4UInteractive', 'NoDesktopAcl', 'Job')]
    [string] $Variant = 'S4UBatch',
    [int] $Session = -1,
    [string] $SpikeExe = (Join-Path $PSScriptRoot 'out\AppStart.Spike.Launch.exe')
)
$ErrorActionPreference = 'Stop'

# The spike EXE must live where standard users cannot replace it (it runs as SYSTEM).
$target = 'C:\Program Files\AppStartSpike'
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $SpikeExe $target -Force
$exe = Join-Path $target 'AppStart.Spike.Launch.exe'

$common = "launch --user appstart-app --exe `"$exe`" --args probe"
if ($Session -ge 0) { $common += " --session $Session" }
$arguments = switch ($Variant) {
    'S4UBatch'       { "$common --logon batch" }
    'S4UNetwork'     { "$common --logon network" }
    'S4UInteractive' { "$common --logon interactive" }
    'NoDesktopAcl'   { "$common --logon batch --no-desktop-acl --wait" }
    'Job'            { "$common --logon batch --job" }
}

$name = "AppStartSpike-$Variant"
$action = New-ScheduledTaskAction -Execute $exe -Argument $arguments
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $name
Write-Host "Started '$name' as SYSTEM with: $arguments"
Write-Host "Check the operator's screen for the probe console window. Waiting for the launch log..."
Start-Sleep -Seconds 8
Get-ChildItem 'C:\ProgramData\AppStartSpike\launch-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content
Write-Host "`n--- grant-desktop log ---"
Get-ChildItem 'C:\ProgramData\AppStartSpike\grant-desktop-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content
Unregister-ScheduledTask -TaskName $name -Confirm:$false
