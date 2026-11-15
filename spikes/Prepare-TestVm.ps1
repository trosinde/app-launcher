#Requires -RunAsAdministrator
<#
  Spike 1.1/1.2 – prepares a Windows TEST VM (snapshot!) with the accounts from the concept.
  NEVER run on a development or production machine: creates accounts and changes user rights.

  - appstart-app : local admin, random password nobody knows, interactive + RDP logon denied
  - appstart-user: local standard user (operator), member of "AppStart Users"
#>
param(
    # Optional: only then the operator account appstart-user is created.
    [securestring] $OperatorPassword
)
$ErrorActionPreference = 'Stop'

function New-RandomPassword {
    $bytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ConvertTo-SecureString ([Convert]::ToBase64String($bytes) + 'aA1!') -AsPlainText -Force
}

if (-not (Get-LocalGroup -Name 'AppStart Users' -ErrorAction SilentlyContinue)) {
    New-LocalGroup -Name 'AppStart Users' -Description 'May start AppStart applications' | Out-Null
}
if (-not (Get-LocalUser -Name 'appstart-app' -ErrorAction SilentlyContinue)) {
    New-LocalUser -Name 'appstart-app' -Password (New-RandomPassword) -PasswordNeverExpires `
        -UserMayNotChangePassword -Description 'AppStart application account' | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'appstart-app'  # Administrators, language-independent
}
if ($OperatorPassword -and -not (Get-LocalUser -Name 'appstart-user' -ErrorAction SilentlyContinue)) {
    New-LocalUser -Name 'appstart-user' -Password $OperatorPassword -PasswordNeverExpires | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member 'appstart-user'  # Users
    Add-LocalGroupMember -Group 'AppStart Users' -Member 'appstart-user'
}

# Deny interactive and RDP logon for appstart-app (SeDenyInteractiveLogonRight, SeDenyRemoteInteractiveLogonRight).
$sid = (Get-LocalUser 'appstart-app').SID.Value
$tmp = Join-Path $env:TEMP "appstart-secpol"
New-Item -ItemType Directory -Force $tmp | Out-Null
secedit /export /cfg "$tmp\cur.inf" /areas USER_RIGHTS | Out-Null
$backup = Join-Path $env:ProgramData "AppStartSpike\user-rights-backup.inf"
if (-not (Test-Path $backup)) { New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Copy-Item "$tmp\cur.inf" $backup }
$lines = Get-Content "$tmp\cur.inf"
foreach ($right in 'SeDenyInteractiveLogonRight', 'SeDenyRemoteInteractiveLogonRight') {
    $existing = $lines | Where-Object { $_ -like "$right*" }
    if ($existing) {
        if ($existing -notmatch [regex]::Escape("*$sid")) { $lines = $lines -replace "^$right = (.*)$", "$right = `$1,*$sid" }
    } else {
        $idx = [array]::IndexOf($lines, '[Privilege Rights]')
        $lines = $lines[0..$idx] + "$right = *$sid" + $lines[($idx + 1)..($lines.Count - 1)]
    }
}
$lines | Set-Content "$tmp\new.inf" -Encoding Unicode
secedit /configure /db "$tmp\secedit.sdb" /cfg "$tmp\new.inf" /areas USER_RIGHTS | Out-Null
Remove-Item -Recurse -Force $tmp

Write-Host "Test VM prepared. appstart-app SID: $sid"
Write-Host "Sign in as appstart-user in a second session (or switch user), then run Run-Spike.ps1 as admin."
