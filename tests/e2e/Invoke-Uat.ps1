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
    # Acceptance, Setup, Service, Operations, ChildProcesses, Install (also comma-separated in one argument, as with -File).
    [string[]] $Suites = @('Acceptance'),
    # Suite Install only: older MSI installed first, then updated to -MsiPath.
    [string] $PreviousMsiPath,
    # Started by the scheduled task "AppStartUAT": parameters come from %LOCALAPPDATA%\AppStartUAT\request.json.
    [switch] $FromRequest
)
$ErrorActionPreference = 'Stop'

# Not elevated: start again elevated (one UAC prompt) with the same parameters and wait.
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $relaunch = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    foreach ($p in $PSBoundParameters.GetEnumerator()) {
        if ($p.Value -is [switch]) { if ($p.Value) { $relaunch += "-$($p.Key)" } }
        elseif ($p.Value -is [array]) { $relaunch += "-$($p.Key)"; $relaunch += ($p.Value -join ',') }
        else { $relaunch += "-$($p.Key)"; $relaunch += "`"$($p.Value)`"" }
    }
    Write-Host 'Nicht als Administrator gestartet: starte erhöht neu (UAC-Abfrage mit «Ja» bestätigen) ...'
    $child = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -Wait -PassThru -ArgumentList $relaunch
    Write-Host "Fertig (Exit-Code $($child.ExitCode))."
    exit $child.ExitCode
}

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
    $PreviousMsiPath = $request.previousMsiPath
    if (-not $ResultDir.StartsWith($uatHome + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "resultDir muss unter $uatHome liegen." }
}
$Suites = @($Suites | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($s in $Suites) { if ($s -notin 'Acceptance', 'Setup', 'Service', 'Operations', 'ChildProcesses', 'Install') { throw "Unbekannte Suite $s" } }
if ($Suites -contains 'Install' -and -not $PreviousMsiPath) { throw 'Suite Install braucht PreviousMsiPath (älteres MSI).' }
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
# Bootstrapper next to the MSI (Build-Release.ps1 output): staged too, for /quiet /config as SYSTEM.
$bootSource = Join-Path (Split-Path $MsiPath) 'AppStart-Setup.exe'
$boot = $null
if (Test-Path $bootSource) {
    Copy-Item $bootSource $stage
    $boot = Join-Path $stage 'AppStart-Setup.exe'
}
$previousMsi = $null
if ($PreviousMsiPath) {
    if (-not $PreviousMsiPath.EndsWith('.msi', [StringComparison]::OrdinalIgnoreCase)) { throw 'PreviousMsiPath muss auf ein MSI zeigen.' }
    New-Item -ItemType Directory -Force (Join-Path $stage 'previous') | Out-Null
    Copy-Item (Resolve-Path $PreviousMsiPath).Path (Join-Path $stage 'previous')
    $previousMsi = Join-Path $stage "previous\$(Split-Path $PreviousMsiPath -Leaf)"
}

# -StageDir explicitly: Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults of
# advanced scripts started with -File.
$scripts = @{
    Acceptance = @{ File = 'Invoke-AcceptanceTest.ps1'; Args = @('-MsiPath', $msi, '-StageDir', $stage) }
    Setup      = @{ File = 'Invoke-SetupE2E.ps1'; Args = @('-MsiPath', $msi, '-StageDir', $stage) }
    Service    = @{ File = 'Invoke-ServiceE2E.ps1'; Args = @('-MsiPath', $msi, '-StageDir', $stage) }
    Operations = @{ File = 'Invoke-OperationsTests.ps1'; Args = @('-MsiPath', $msi, '-StageDir', $stage) }
    ChildProcesses = @{ File = 'Invoke-ChildProcessE2E.ps1'; Args = @('-MsiPath', $msi, '-StageDir', $stage) + $(if ($boot) { @('-BootstrapperPath', $boot) } else { @() }) }
    Install    = @{ File = 'Invoke-InstallTests.ps1'; Args = @('-MsiPath', $previousMsi, '-MsiNextPath', $msi, '-StageDir', $stage) + $(if ($boot) { @('-BootstrapperPath', $boot) } else { @() }) }
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
