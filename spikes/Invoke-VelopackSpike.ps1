#Requires -RunAsAdministrator
<#
  Spike 1.3 – Velopack MSI per-machine with a Windows service, unattended:
    A) install v1.0.0 MSI  -> where, hooks elevated?, service running?, folder ACL
    B) "update" with v1.0.1 MSI (as a service technician would) -> upgrade or side-by-side?, service?
    C) uninstall -> service and folder removed?
    D) install with VELOPACK_INSTALLDIR="C:\Program Files\AppStartVeloSpike" -> path honoured? then uninstall
  Ends with a forced cleanup. Output: <StageDir>\velo-run.txt
#>
param([string] $StageDir = $PSScriptRoot)
$ErrorActionPreference = 'Continue'
$msi1 = Join-Path $StageDir 'msi\v100.msi'
$msi2 = Join-Path $StageDir 'msi\v101.msi'
$logDir = Join-Path $StageDir 'msilogs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$hookLog = Join-Path $env:ProgramData 'AppStartVeloSpike\hooks.log'
Remove-Item $hookLog -ErrorAction SilentlyContinue
Start-Transcript -Path (Join-Path $StageDir 'velo-run.txt') -Force | Out-Null

function Invoke-Msi([string] $arguments, [string] $log) {
    $p = Start-Process msiexec.exe -ArgumentList "$arguments /qn /l*v `"$(Join-Path $logDir $log)`"" -Wait -PassThru
    Write-Host "msiexec $arguments -> exit $($p.ExitCode)"
    Start-Sleep -Seconds 3   # hooks / service start
}

function Get-Products {
    Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Get-ItemProperty | Where-Object { $_.DisplayName -like '*AppStartVeloSpike*' }
}

function Show-State([string] $title) {
    Write-Host "`n---------------- $title ----------------"
    $prods = @(Get-Products)
    Write-Host "Installed products: $($prods.Count)"
    $prods | ForEach-Object { Write-Host "  $($_.DisplayName) $($_.DisplayVersion) | $($_.PSChildName) | InstallLocation=$($_.InstallLocation) | Uninstall=$($_.UninstallString)" }
    $dirs = @('C:\Program Files\AppStartSpike\AppStartVeloSpike', 'C:\Program Files\AppStartVeloSpike', "$env:LOCALAPPDATA\AppStartVeloSpike") +
            @(Get-ChildItem 'C:\Program Files' -Directory -Filter '*AppStartVeloSpike*' -Recurse -Depth 1 -ErrorAction SilentlyContinue | ForEach-Object FullName)
    foreach ($d in ($dirs | Select-Object -Unique)) {
        if (Test-Path $d) {
            Write-Host "Folder: $d"
            Get-ChildItem $d | ForEach-Object { Write-Host "    $($_.Name)" }
            Write-Host "  ACL (write for non-admins?):"
            (Get-Acl $d).Access | ForEach-Object { Write-Host "    $($_.IdentityReference) $($_.AccessControlType) $($_.FileSystemRights)" }
        }
    }
    $svc = Get-CimInstance Win32_Service -Filter "Name='AppStartVeloSpike'"
    if ($svc) { Write-Host "Service: $($svc.State), start $($svc.StartMode), account $($svc.StartName), path $($svc.PathName)" }
    else { Write-Host 'Service: not present' }
    Write-Host 'Hook log:'
    if (Test-Path $hookLog) { Get-Content $hookLog | ForEach-Object { Write-Host "    $_" } ; Clear-Content $hookLog }
}

try {
    Show-State 'Before'
    Invoke-Msi "/i `"$msi1`"" 'A-install-100.log';  Show-State 'A) after install 1.0.0'
    Invoke-Msi "/i `"$msi2`"" 'B-install-101.log';  Show-State 'B) after MSI 1.0.1 over 1.0.0'
    foreach ($p in @(Get-Products)) {
        if ($p.PSChildName -match '^\{') { Invoke-Msi "/x $($p.PSChildName)" "C-uninstall-$($p.DisplayVersion).log" }
        elseif ($p.UninstallString) { Write-Host "Non-MSI uninstall entry: $($p.UninstallString)" }
    }
    Show-State 'C) after uninstall'
    Invoke-Msi "/i `"$msi1`" VELOPACK_INSTALLDIR=`"C:\Program Files\AppStartVeloSpike`"" 'D-install-dir.log'; Show-State 'D) install with VELOPACK_INSTALLDIR'
    foreach ($p in @(Get-Products)) { if ($p.PSChildName -match '^\{') { Invoke-Msi "/x $($p.PSChildName)" 'D-uninstall.log' } }
    Show-State 'D) after uninstall'
}
finally {
    Write-Host "`n---------------- Forced cleanup ----------------"
    sc.exe stop AppStartVeloSpike 2>$null | Out-Null
    sc.exe delete AppStartVeloSpike 2>$null | Out-Null
    foreach ($p in @(Get-Products)) { if ($p.PSChildName -match '^\{') { Start-Process msiexec.exe "/x $($p.PSChildName) /qn" -Wait } }
    foreach ($d in 'C:\Program Files\AppStartSpike', 'C:\Program Files\AppStartVeloSpike', "$env:LOCALAPPDATA\AppStartVeloSpike", (Join-Path $env:ProgramData 'AppStartVeloSpike')) {
        if (Test-Path $d) { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue; Write-Host "removed $d" }
    }
    Write-Host "Remaining products: $(@(Get-Products).Count), service present: $([bool](Get-Service AppStartVeloSpike -ErrorAction SilentlyContinue))"
    Stop-Transcript | Out-Null
}
