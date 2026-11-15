#Requires -RunAsAdministrator
<#
  UAT runner: runs the acceptance scripts of this folder in one elevated session (one UAC prompt)
  and copies their logs and screenshots to a result folder the developer can read.
  TEST VM ONLY (same rules as the scripts it runs: AppStart must not be installed, everything is
  removed again by each script).

  Admin code must not run from a folder operators can write to (project.md). The scripts and
  packages are therefore copied to C:\AppStartUAT\<timestamp>, which only SYSTEM and
  Administrators can write, and run from there.

    Start-Process pwsh -Verb RunAs -Wait -ArgumentList '-NoProfile', '-File', 'tests\e2e\Invoke-Uat.ps1',
        '-MsiPath', 'artifacts\uat\AppStart-x.msi', '-ResultDir', "$env:LOCALAPPDATA\AppStartUAT\run1"
#>
param(
    [string] $MsiPath,
    [string] $ResultDir,
    [ValidateSet('Acceptance', 'Setup', 'Service', 'Operations')]
    [string[]] $Suites = @('Acceptance'),
    # Started by the scheduled task "AppStartUAT": parameters come from %LOCALAPPDATA%\AppStartUAT\request.json.
    [switch] $FromRequest
)
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- unattended runs (test VM only)
# The scheduled task runs this script elevated without a UAC prompt, started on demand with
# Start-ScheduledTask AppStartUAT by the same user. It is registered on every elevated run.
# Risk (accepted for the dedicated test VM): any process of this user can change the repository
# scripts and then start the task, i.e. elevate without a prompt.
$uatHome = Join-Path $env:LOCALAPPDATA 'AppStartUAT'
$taskName = 'AppStartUAT'
if (-not (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute (Get-Command pwsh).Source `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -FromRequest" -WorkingDirectory (Split-Path $PSCommandPath)
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Scheduled task '$taskName' registered for $user (Highest, interactive)."
}

if ($FromRequest) {
    $request = Get-Content (Join-Path $uatHome 'request.json') -Raw | ConvertFrom-Json
    $MsiPath = $request.msiPath
    $Suites = @($request.suites)
    $ResultDir = $request.resultDir
    foreach ($s in $Suites) { if ($s -notin 'Acceptance', 'Setup', 'Service', 'Operations') { throw "Unbekannte Suite $s" } }
    if (-not $ResultDir.StartsWith($uatHome + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "resultDir muss unter $uatHome liegen." }
}
if (-not $MsiPath -or -not $ResultDir) { throw 'MsiPath und ResultDir sind nötig (oder -FromRequest).' }
if (-not $MsiPath.EndsWith('.msi', [StringComparison]::OrdinalIgnoreCase)) { throw 'MsiPath muss auf ein MSI zeigen.' }
$MsiPath = (Resolve-Path $MsiPath).Path
New-Item -ItemType Directory -Force $ResultDir | Out-Null
Remove-Item (Join-Path $ResultDir 'uat-summary.txt') -ErrorAction SilentlyContinue # written last = run finished
Start-Transcript -Path (Join-Path $ResultDir 'uat-runner.txt') -Force | Out-Null
$repoE2E = $PSScriptRoot
$repoSpikes = Join-Path $PSScriptRoot '..\..\spikes' | Resolve-Path

$stage = Join-Path 'C:\AppStartUAT' (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Force $stage | Out-Null
# Only SYSTEM and Administrators: full control; Users: none. No inheritance from C:\.
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        (New-Object System.Security.Principal.SecurityIdentifier $sid), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
Set-Acl -Path $stage -AclObject $acl

Copy-Item (Join-Path $repoE2E '*.ps1') $stage
Copy-Item (Join-Path $repoSpikes 'Prepare-TestVm.ps1'), (Join-Path $repoSpikes 'Remove-SpikeSetup.ps1') $stage
Copy-Item $MsiPath $stage
$msi = Join-Path $stage (Split-Path $MsiPath -Leaf)

$scripts = @{
    Acceptance = @{ File = 'Invoke-AcceptanceTest.ps1'; Args = @('-MsiPath', $msi) }
    Setup      = @{ File = 'Invoke-SetupE2E.ps1'; Args = @('-MsiPath', $msi) }
    Service    = @{ File = 'Invoke-ServiceE2E.ps1'; Args = @('-MsiPath', $msi) }
    Operations = @{ File = 'Invoke-OperationsTests.ps1'; Args = @('-MsiPath', $msi) }
}

$summary = @()
foreach ($suite in $Suites) {
    $s = $scripts[$suite]
    Write-Host "==> $suite ($($s.File))"
    $started = Get-Date
    # Windows PowerShell, like the README of this folder (UI Automation via Add-Type).
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage $s.File) @($s.Args) 2>&1 | Out-Host
    $summary += "$suite exit=$LASTEXITCODE start=$($started.ToString('s')) end=$((Get-Date).ToString('s'))"
}

Get-ChildItem $stage -File | Where-Object { $_.Extension -in '.txt', '.png', '.log', '.html', '.json' } | Copy-Item -Destination $ResultDir -Force
Remove-Item $stage -Recurse -Force
Write-Host "Results: $ResultDir"
Stop-Transcript | Out-Null
$summary | Set-Content (Join-Path $ResultDir 'uat-summary.txt')
