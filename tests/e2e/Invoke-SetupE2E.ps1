#Requires -RunAsAdministrator
<#
  End-to-end test of AppStart.Setup (section 6) through the command line on a Windows TEST machine:
  MSI install -> add TestApp (data folder + registry key) -> add-user appstart-user -> verify (8/8) ->
  start through the created shortcut -> remove/rehash checks -> uninstall -> MSI uninstall.
  Everything is removed again. Output: <StageDir>\setup-e2e.txt
#>
param(
    [Parameter(Mandatory)] [string] $MsiPath,
    [string] $StageDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $StageDir 'setup-e2e.txt') -Force | Out-Null
$results = [System.Collections.Generic.List[string]]::new()
function Check([string] $name, [bool] $ok, [string] $detail = '') {
    $line = "{0}  {1}  {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail
    $results.Add($line); Write-Host $line
}

$cur = 'C:\Program Files\AppStart\current'
$setup = Join-Path $cur 'AppStart.Setup.exe'
$testExe = Join-Path $cur 'TestApp\AppStart.TestApp.exe'
$dataDir = 'C:\ProgramData\AppStartTest'
$regKey = 'HKLM:\SOFTWARE\AppStartTest'
$startMenu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'AppStart'

function Invoke-Setup([string[]] $arguments, [string] $stdin = $null) {
    # Runs elevated already: output goes to files, exit code is returned.
    $out = Join-Path $StageDir 'setup-out.txt'; $err = Join-Path $StageDir 'setup-err.txt'
    $quoted = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi = @{ FilePath = $setup; ArgumentList = $quoted; Wait = $true; PassThru = $true; NoNewWindow = $true
              RedirectStandardOutput = $out; RedirectStandardError = $err }
    if ($stdin) { $in = Join-Path $StageDir 'setup-in.txt'; Set-Content $in $stdin -NoNewline; $psi.RedirectStandardInput = $in }
    $p = Start-Process @psi
    if ($stdin) { Remove-Item (Join-Path $StageDir 'setup-in.txt') }
    $text = ((Get-Content $out -Raw -ErrorAction SilentlyContinue) + (Get-Content $err -Raw -ErrorAction SilentlyContinue)).Trim()
    Write-Host "  > AppStart.Setup.exe $($arguments -join ' ')  (exit $($p.ExitCode))"
    if ($text) { $text -split "`r?`n" | ForEach-Object { Write-Host "    $_" } }
    return @{ Exit = $p.ExitCode; Text = $text }
}

function Get-Sddl([string] $path) { (Get-Acl $path).Sddl }

try {
    # Pre-existing state the app had before AppStart (to be restored by uninstall)
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    New-Item -Force $regKey | Out-Null
    Set-ItemProperty $regKey -Name Password -Value 'AppStartTest-Passwort-0815'
    $dataSddlBefore = Get-Sddl $dataDir
    $regSddlBefore = Get-Sddl $regKey

    $p = Start-Process msiexec.exe -ArgumentList "/i `"$MsiPath`" /qn" -Wait -PassThru
    Check 'MSI installed' ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
    Start-Sleep -Seconds 3
    Check 'Start menu: "AppStart – Setup" and "AppStart – Verify setup" created' ((Test-Path "$startMenu\AppStart – Setup.lnk") -and (Test-Path "$startMenu\AppStart – Verify setup.lnk")) ''
    $velopackShortcut = @(Get-ChildItem ([Environment]::GetFolderPath('CommonDesktopDirectory')), ([Environment]::GetFolderPath('Desktop')) -Filter 'AppStart.lnk' -ErrorAction SilentlyContinue)
    Check 'No Velopack shortcut to the service' ($velopackShortcut.Count -eq 0) ($velopackShortcut.FullName -join ', ')

    # A pre-existing direct shortcut to the app must be backed up and replaced
    $direct = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'TestApp direkt.lnk'
    $sh = (New-Object -ComObject WScript.Shell).CreateShortcut($direct); $sh.TargetPath = $testExe; $sh.Save()

    $r = Invoke-Setup @('add', '--exe', $testExe, '--data-dir', $dataDir, '--reg-key', 'HKLM\SOFTWARE\AppStartTest', '--name', 'AppStart TestApp')
    Check 'add: TestApp approved' ($r.Exit -eq 0) ''
    $r = Invoke-Setup @('add-user', '--name', 'appstart-user', '--password-stdin') 'Operator-Test-2026!'
    Check 'add-user: appstart-user created' ($r.Exit -eq 0 -and (Get-LocalUser appstart-user -ErrorAction SilentlyContinue)) ''

    $publicLnk = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'AppStart TestApp.lnk'
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut($publicLnk)
    Check 'Shortcut to the launcher with --app' ($lnk.TargetPath -like '*AppStart.Launcher.exe' -and $lnk.Arguments -like '--app *') "$($lnk.TargetPath) $($lnk.Arguments) icon=$($lnk.IconLocation)"
    Check 'Direct shortcut backed up and removed' (-not (Test-Path $direct)) ''
    $foreign = (Get-Acl $dataDir).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) | Where-Object {
        $_.IdentityReference.Value -notin 'S-1-5-18', 'S-1-5-32-544' -and $_.IdentityReference.Translate([System.Security.Principal.NTAccount]).Value -notlike '*\appstart-app' }
    Check 'Data folder only appstart-app, SYSTEM, Administrators' (-not $foreign) (($foreign | ForEach-Object { $_.IdentityReference }) -join ', ')

    $r = Invoke-Setup @('list')
    Check 'list shows TestApp' ($r.Exit -eq 0 -and $r.Text -like '*AppStart TestApp*') ''

    $report = Join-Path $StageDir 'setup-verify.html'
    $r = Invoke-Setup @('verify', '--report', $report)
    Check 'verify: exit code 0, 10 of 10' ($r.Exit -eq 0 -and $r.Text -like '*10 of 10*') ''
    Check 'Verification report saved' ((Test-Path $report) -and @(Get-ChildItem "$env:ProgramData\AppStart\Reports" -Filter 'verify-*.html').Count -ge 1) ''

    # Start through the shortcut created by setup
    Start-Process $publicLnk; Start-Sleep -Seconds 6
    $proc = Get-CimInstance Win32_Process -Filter "Name='AppStart.TestApp.exe'" | Select-Object -First 1
    $owner = if ($proc) { (Invoke-CimMethod -InputObject $proc -MethodName GetOwner).User } else { '' }
    Check 'Start through setup shortcut: TestApp as appstart-app' ($owner -eq 'appstart-app') "owner=$owner"
    Get-Process AppStart.TestApp -ErrorAction SilentlyContinue | Stop-Process -Force

    # Deliberate misconfiguration -> verify must fail
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'
    $r = Invoke-Setup @('verify')
    Check 'verify after misconfiguration: exit code not 0' ($r.Exit -ne 0 -and $r.Text -like '*appstart-user*') ''
    Remove-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-user'

    $r = Invoke-Setup @('uninstall')
    Check 'uninstall' ($r.Exit -eq 0) ''
    Check 'uninstall: accounts and group removed' (-not (Get-LocalUser appstart-app -ErrorAction SilentlyContinue) -and -not (Get-LocalUser appstart-user -ErrorAction SilentlyContinue) -and -not (Get-LocalGroup 'AppStart Users' -ErrorAction SilentlyContinue)) ''
    Check 'uninstall: direct shortcut back, setup shortcut gone' ((Test-Path $direct) -and -not (Test-Path $publicLnk)) ''
    Check 'uninstall: data folder permissions reset' ((Get-Sddl $dataDir) -replace 'AI', '' -eq ($dataSddlBefore -replace 'AI', '')) ''
    Check 'uninstall: registry key permissions reset' ((Get-Sddl $regKey) -replace 'AI', '' -eq ($regSddlBefore -replace 'AI', '')) ''

    $product = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' | Get-ItemProperty | Where-Object { $_.DisplayName -eq 'AppStart' -and $_.PSChildName -match '^\{' } | Select-Object -First 1
    $p = Start-Process msiexec.exe -ArgumentList "/x $($product.PSChildName) /qn" -Wait -PassThru
    Start-Sleep -Seconds 2
    Check 'MSI uninstalled, service and Start menu folder gone' ($p.ExitCode -eq 0 -and -not (Get-Service AppStart -ErrorAction SilentlyContinue) -and -not (Test-Path $startMenu)) ''
}
catch {
    Check 'Test run without exception' $false $_.Exception.Message
}
finally {
    Write-Host "`n================ Cleanup ================"
    Get-Process AppStart.TestApp, AppStart.Launcher -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path $setup) { Invoke-Setup @('uninstall') | Out-Null }
    $product = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' | Get-ItemProperty | Where-Object { $_.DisplayName -eq 'AppStart' -and $_.PSChildName -match '^\{' } | Select-Object -First 1
    if ($product) { Start-Process msiexec.exe -ArgumentList "/x $($product.PSChildName) /qn" -Wait }
    foreach ($u in 'appstart-app', 'appstart-user') { if (Get-LocalUser $u -ErrorAction SilentlyContinue) { Remove-LocalUser $u } }
    if (Get-LocalGroup 'AppStart Users' -ErrorAction SilentlyContinue) { Remove-LocalGroup 'AppStart Users' }
    Get-CimInstance Win32_UserProfile | Where-Object { $_.LocalPath -like '*\appstart-*' } | Remove-CimInstance -ErrorAction SilentlyContinue
    Remove-Item $direct, $publicLnk -ErrorAction SilentlyContinue
    Remove-Item $regKey -Recurse -ErrorAction SilentlyContinue
    Remove-Item $dataDir, "$env:ProgramData\AppStart", 'C:\Program Files\AppStart', $startMenu -Recurse -Force -ErrorAction SilentlyContinue
    if ([System.Diagnostics.EventLog]::SourceExists('AppStart')) { [System.Diagnostics.EventLog]::DeleteEventSource('AppStart') }
    Write-Host "Left over: Service=$([bool](Get-Service AppStart -ErrorAction SilentlyContinue)) Accounts=$([bool](Get-LocalUser appstart-app -ErrorAction SilentlyContinue)) ProgramData=$(Test-Path "$env:ProgramData\AppStart") Programs=$(Test-Path 'C:\Program Files\AppStart')"
    Write-Host "`n================ Result ================"
    $results | ForEach-Object { Write-Host $_ }
    Write-Host ("{0} PASS, {1} FAIL" -f @($results | Where-Object { $_ -like 'PASS*' }).Count, @($results | Where-Object { $_ -like 'FAIL*' }).Count)
    Stop-Transcript | Out-Null
}
