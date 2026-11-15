#Requires -RunAsAdministrator
<#
  Spike 1.4 with AppStart.TestApp: starts the TestApp the way the service will
  (SYSTEM -> S4U token of appstart-app -> operator session), drives it via UI Automation,
  takes screenshots and records registry/file state.
    Run 1 "Job"   : with job object (ActiveProcessLimit = 1) -> cmd.exe must be blocked
    Run 2 "NoJob" : control run without job object -> cmd.exe starts (breakout possible)
  Ends with cleanup. Output: <StageDir>\testapp-run.txt and screenshots *.png
#>
param([string] $StageDir = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $StageDir 'testapp-run.txt') -Force | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace Native -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[Native.Dpi]::SetProcessDPIAware() | Out-Null
$UIA = [System.Windows.Automation.AutomationElement]

function Find-Window([string] $name, [int] $seconds = 30) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $w = $UIA::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Invoke-Button($window, [string] $name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)))
    $b = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $b) { Write-Host "  button '$name' NOT found"; return }
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "  clicked '$name'"
    Start-Sleep -Milliseconds 1500
}

function Show-Texts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }
    Write-Host "  UI texts: $($texts -join ' | ')"
}

function Save-Screenshot($window, [string] $file) {
    $r = $window.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $StageDir $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "  screenshot $file"
}

function Show-Data {
    $k = 'HKLM:\SOFTWARE\AppStartTest'
    if (Test-Path $k) {
        $v = Get-ItemProperty $k
        Write-Host "  Registry: TestValue='$($v.TestValue)' Password='$($v.Password)' LastWriter='$($v.LastWriter)'"
    } else { Write-Host '  Registry: key missing' }
    $f = Join-Path $env:ProgramData 'AppStartTest\exchange.txt'
    if (Test-Path $f) { Write-Host "  Exchange file: $((Get-Content $f) -join ' ; ')" } else { Write-Host '  Exchange file: missing' }
}

function Stop-AppAccountCmd {
    Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | ForEach-Object {
        $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
        if ($o.User -eq 'appstart-app') { Stop-Process -Id $_.ProcessId -Force; Write-Host "  closed cmd.exe PID $($_.ProcessId) (owner appstart-app)" }
    }
}

try {
    & (Join-Path $StageDir 'Prepare-TestVm.ps1')
    $dir = 'C:\Program Files\AppStartSpike'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item (Join-Path $StageDir 'AppStart.Spike.Launch.exe') $dir -Force
    Copy-Item (Join-Path $StageDir 'AppStart.TestApp.exe') $dir -Force
    $launcher = Join-Path $dir 'AppStart.Spike.Launch.exe'
    $testApp = Join-Path $dir 'AppStart.TestApp.exe'

    foreach ($run in @(@{ Name = 'Job'; Extra = '--job' }, @{ Name = 'NoJob'; Extra = '' })) {
        Write-Host "`n================ Run $($run.Name) ================"
        $taskName = "AppStartSpike-TestApp-$($run.Name)"
        $arguments = "launch --user appstart-app --exe `"$testApp`" --logon batch --wait $($run.Extra)"
        $action = New-ScheduledTaskAction -Execute $launcher -Argument $arguments
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal `
            -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 5)) -Force | Out-Null
        Start-ScheduledTask -TaskName $taskName

        $w = Find-Window 'AppStart TestApp'
        if (-not $w) { Write-Host '  TestApp window NOT found' }
        else {
            Start-Sleep -Seconds 2
            $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$($w.Current.ProcessId)"
            Write-Host "  TestApp PID $($proc.ProcessId), owner $((Invoke-CimMethod -InputObject $proc -MethodName GetOwner).User), session $($proc.SessionId)"
            Show-Texts $w
            Save-Screenshot $w "testapp-$($run.Name)-1-start.png"
            Invoke-Button $w 'In Registry und Datei speichern'
            Invoke-Button $w 'Eingabeaufforderung starten'
            Invoke-Button $w 'Erneut prüfen'
            Show-Texts $w
            Save-Screenshot $w "testapp-$($run.Name)-2-after.png"
            Show-Data
            Stop-AppAccountCmd
            $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        }
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Get-ChildItem (Join-Path $env:ProgramData 'AppStartSpike') -Filter 'launch-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 |
            ForEach-Object { Get-Content $_.FullName | Select-String 'RESULT|exited|FATAL|failed' | ForEach-Object { Write-Host "  launch log: $_" } }
    }
}
finally {
    Write-Host "`n================ Cleanup ================"
    Get-ScheduledTask -TaskName 'AppStartSpike-TestApp-*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    Get-Process AppStart.TestApp -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item 'HKLM:\SOFTWARE\AppStartTest' -Recurse -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:ProgramData 'AppStartTest') -Recurse -Force -ErrorAction SilentlyContinue
    & (Join-Path $StageDir 'Remove-SpikeSetup.ps1')
    Stop-Transcript | Out-Null
}
