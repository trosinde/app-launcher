<#
  Runs NON-elevated next to Invoke-ServiceE2E.ps1: a user outside "AppStart Users" must neither
  connect to \\.\pipe\AppStart nor create an own instance of it (pipe squatting).
  Waits for <StageDir>\service-ready.txt, writes <StageDir>\nonadmin-result.txt.
#>
param([string] $StageDir = $PSScriptRoot)
$deadline = (Get-Date).AddSeconds(120)
while (-not (Test-Path (Join-Path $StageDir 'service-ready.txt')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }

$result = @()
try {
    $c = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'AppStart', [System.IO.Pipes.PipeDirection]::InOut)
    $c.Connect(3000); $c.Dispose()
    $result += 'CONNECTED'
} catch [System.UnauthorizedAccessException] { $result += 'DENIED-ACCESS' }
catch { $result += "ERROR-CONNECT:$($_.Exception.GetType().Name)" }

try {
    $s = New-Object System.IO.Pipes.NamedPipeServerStream('AppStart', [System.IO.Pipes.PipeDirection]::InOut, 4)
    $s.Dispose(); $result += 'CREATED-INSTANCE'
} catch [System.UnauthorizedAccessException] { $result += 'CREATE-DENIED' }
catch { $result += "CREATE-ERROR:$($_.Exception.GetType().Name)" }

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Set-Content (Join-Path $StageDir 'nonadmin-result.txt') (($result -join ' ') + " (elevated=$elevated)")
