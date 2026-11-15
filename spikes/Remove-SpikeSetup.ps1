#Requires -RunAsAdministrator
<#
  Removes everything the spike created: deny-logon entries, accounts, group, profile,
  scheduled tasks and spike folders. Leaves C:\ProgramData\AppStartSpike\*.log unless -RemoveLogs.
#>
param([switch] $RemoveLogs)
$ErrorActionPreference = 'Continue'

$user = Get-LocalUser -Name 'appstart-app' -ErrorAction SilentlyContinue
if ($user) {
    $sid = $user.SID.Value
    # Remove the SID from the deny-logon rights first, otherwise an orphaned SID remains in the policy.
    $tmp = Join-Path $env:TEMP 'appstart-secpol-rm'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    secedit /export /cfg "$tmp\cur.inf" /areas USER_RIGHTS | Out-Null
    $lines = Get-Content "$tmp\cur.inf" | ForEach-Object {
        if ($_ -match '^(SeDenyInteractiveLogonRight|SeDenyRemoteInteractiveLogonRight) = (.*)$') {
            $rest = ($Matches[2] -split ',') | Where-Object { $_.Trim() -ne "*$sid" }
            if ($rest) { "$($Matches[1]) = $($rest -join ',')" } else { "$($Matches[1]) =" }
        } else { $_ }
    }
    $lines | Set-Content "$tmp\new.inf" -Encoding Unicode
    secedit /configure /db "$tmp\secedit.sdb" /cfg "$tmp\new.inf" /areas USER_RIGHTS | Out-Null
    Remove-Item -Recurse -Force $tmp

    Get-CimInstance Win32_UserProfile | Where-Object SID -eq $sid | Remove-CimInstance
    Remove-LocalUser -Name 'appstart-app'
    Write-Host "Removed appstart-app ($sid) incl. profile and deny-logon entries"
}
if (Get-LocalUser -Name 'appstart-user' -ErrorAction SilentlyContinue) {
    $sid = (Get-LocalUser 'appstart-user').SID.Value
    Get-CimInstance Win32_UserProfile | Where-Object SID -eq $sid | Remove-CimInstance
    Remove-LocalUser -Name 'appstart-user'
    Write-Host 'Removed appstart-user'
}
if (Get-LocalGroup -Name 'AppStart Users' -ErrorAction SilentlyContinue) {
    Remove-LocalGroup -Name 'AppStart Users'
    Write-Host 'Removed group AppStart Users'
}
Get-ScheduledTask -TaskName 'AppStartSpike-*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
Remove-Item -Recurse -Force 'C:\Program Files\AppStartSpike' -ErrorAction SilentlyContinue
if ($RemoveLogs) { Remove-Item -Recurse -Force (Join-Path $env:ProgramData 'AppStartSpike') -ErrorAction SilentlyContinue }
Write-Host 'Cleanup done.'
