#Requires -RunAsAdministrator
<#
  Runs spikes 1.1, 1.2 and the job-object check unattended in one elevated session:
  prepare accounts -> all variants as SYSTEM -> collect logs -> cleanup (unless -KeepSetup).
  Output: <StageDir>\spike-run.txt
#>
param(
    [string] $StageDir = $PSScriptRoot,
    [switch] $KeepSetup
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $StageDir 'spike-run.txt'
Start-Transcript -Path $out -Force | Out-Null
try {
    Write-Host "== Windows: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"
    Write-Host "== Active console session: $((Get-Process -Id $PID).SessionId) (this script), explorer sessions: $((Get-Process explorer -ErrorAction SilentlyContinue).SessionId -join ',')"

    & (Join-Path $StageDir 'Prepare-TestVm.ps1')

    $exe = 'C:\Program Files\AppStartSpike\AppStart.Spike.Launch.exe'
    New-Item -ItemType Directory -Force (Split-Path $exe) | Out-Null
    Copy-Item (Join-Path $StageDir 'AppStart.Spike.Launch.exe') $exe -Force
    $logDir = Join-Path $env:ProgramData 'AppStartSpike'

    # Order matters: the desktop ACE stays until logoff, so the control run without ACL goes first.
    $variants = [ordered]@{
        'NoDesktopAcl'   = '--logon batch --no-desktop-acl --wait'
        'S4UInteractive' = '--logon interactive --wait'
        'S4UNetwork'     = '--logon network --wait'
        'S4UBatch'       = '--logon batch --wait'
        'Job'            = '--logon batch --job --wait'
    }
    foreach ($v in $variants.GetEnumerator()) {
        Write-Host "`n================ Variant $($v.Key) ================"
        $before = Get-Date
        $arguments = "launch --user appstart-app --exe `"$exe`" --args probe $($v.Value)"
        $name = "AppStartSpike-$($v.Key)"
        $action = New-ScheduledTaskAction -Execute $exe -Argument $arguments
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
        Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
        Start-ScheduledTask -TaskName $name
        $deadline = (Get-Date).AddSeconds(60)
        do { Start-Sleep -Seconds 2 } while ((Get-ScheduledTask -TaskName $name).State -eq 'Running' -and (Get-Date) -lt $deadline)
        $info = Get-ScheduledTaskInfo -TaskName $name
        Write-Host "Task result: 0x$('{0:X8}' -f $info.LastTaskResult)"
        Unregister-ScheduledTask -TaskName $name -Confirm:$false
        Get-ChildItem $logDir -Filter '*.log' | Where-Object LastWriteTime -ge $before | Sort-Object LastWriteTime | ForEach-Object {
            Write-Host "--- $($_.Name)"
            Get-Content $_.FullName
        }
    }
}
finally {
    if (-not $KeepSetup) {
        Write-Host "`n================ Cleanup ================"
        & (Join-Path $StageDir 'Remove-SpikeSetup.ps1')
    }
    Stop-Transcript | Out-Null
}
