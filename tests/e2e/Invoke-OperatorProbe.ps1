<#
  Runs as a REAL operator (appstart-user or a second test user), started by the e2e scripts through
  Start-Process -Credential (CreateProcessWithLogonW: own logon, standard user token, no elevation).
  Performs exactly one action and writes one result line to -ResultFile. Never run as admin by hand:
  as admin every access would be allowed and the result meaningless.

  Result tokens per attempt: ALLOWED, DENIED, MISSING, E<win32 code> or ERROR:<exception type>.
#>
param(
    [Parameter(Mandatory)] [string] $Action,
    [Parameter(Mandatory)] [string] $ResultFile,
    [string] $Argument = ''
)
$ErrorActionPreference = 'Stop'

function Try-Access([string] $label, [scriptblock] $body) {
    try { & $body; return "${label}=ALLOWED" }
    catch {
        $ex = $_.Exception
        while ($ex.InnerException -and $ex -is [System.Management.Automation.MethodInvocationException]) { $ex = $ex.InnerException }
        if ($ex -is [System.UnauthorizedAccessException] -or $ex -is [System.Security.SecurityException]) { return "${label}=DENIED" }
        if ($ex -is [System.IO.FileNotFoundException] -or $ex -is [System.IO.DirectoryNotFoundException]) { return "${label}=MISSING" }
        if ($ex -is [System.ComponentModel.Win32Exception]) { return "${label}=E$($ex.NativeErrorCode)" }
        return "${label}=ERROR:$($ex.GetType().Name):$($ex.Message -replace '\s+', ' ')"
    }
}

$prog = 'C:\Program Files\AppStart'
$cur = Join-Path $prog 'current'
$config = Join-Path $env:ProgramData 'AppStart\apps.json'
$out = [System.Collections.Generic.List[string]]::new()

switch ($Action) {
    'whoami' {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($id)
        $admins = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
        $inAdmins = @($id.Groups | Where-Object { $_.Value -eq $admins.Value }).Count -gt 0
        $groupNames = @($id.Groups | ForEach-Object { try { $_.Translate([System.Security.Principal.NTAccount]).Value } catch { $_.Value } })
        $out.Add("user=$($id.Name)")
        $out.Add("elevated=$($principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator))")
        $out.Add("adminsInToken=$inAdmins")
        $out.Add("appstartUsers=$(@($groupNames | Where-Object { $_ -like '*\AppStart Users' }).Count -gt 0)")
        $out.Add("session=$((Get-Process -Id $PID).SessionId)")
    }
    'reg-read' {
        # Like regedit: open HKLM\SOFTWARE\AppStartTest for reading and enumerate it.
        $out.Add((Try-Access 'open' {
            $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
            $k = $hklm.OpenSubKey('SOFTWARE\AppStartTest', $false)
            if (-not $k) { throw (New-Object System.IO.FileNotFoundException('key missing')) }
            $null = $k.GetValueNames(); $k.Dispose()
        }))
    }
    'data-read' {
        $dir = 'C:\ProgramData\AppStartTest'
        $out.Add((Try-Access 'list' { $null = [System.IO.Directory]::GetFileSystemEntries($dir) }))
        $out.Add((Try-Access 'write' { [System.IO.File]::WriteAllText((Join-Path $dir 'operator-probe.txt'), 'x') }))
    }
    'config' {
        # apps.json: read, overwrite, replace by copy, delete, rename, new file next to it.
        $tmp = Join-Path (Split-Path $ResultFile) "fake-apps-$PID.json"
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "apps": [] }')
        $out.Add((Try-Access 'read' { $null = [System.IO.File]::ReadAllText($config) }))
        $out.Add((Try-Access 'openWrite' { $s = [System.IO.File]::Open($config, 'Open', 'Write', 'ReadWrite'); $s.Dispose() }))
        $out.Add((Try-Access 'replace' { [System.IO.File]::Copy($tmp, $config, $true) }))
        $out.Add((Try-Access 'delete' { [System.IO.File]::Delete($config); if (Test-Path $config) { throw (New-Object System.UnauthorizedAccessException('still there')) } }))
        $out.Add((Try-Access 'rename' { [System.IO.File]::Move($config, "$config.old") }))
        $out.Add((Try-Access 'newFile' { [System.IO.File]::WriteAllText((Join-Path (Split-Path $config) 'operator-probe.json'), 'x') }))
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
    'pipe' {
        $out.Add((Try-Access 'connect' {
            # Same rights as the launcher (design D9): no GENERIC_WRITE, which would include FILE_CREATE_PIPE_INSTANCE.
            $rights = [System.IO.Pipes.PipeAccessRights]'ReadData, WriteData, ReadAttributes, WriteAttributes, Synchronize'
            $c = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'AppStart', $rights, [System.IO.Pipes.PipeOptions]::None,
                [System.Security.Principal.TokenImpersonationLevel]::Identification, [System.IO.HandleInheritability]::None)
            try { $c.Connect(3000) } finally { $c.Dispose() }
        }))
        # Pipe squatting: a member of AppStart Users must not create a further server instance (D9).
        $out.Add((Try-Access 'createInstance' {
            $s = New-Object System.IO.Pipes.NamedPipeServerStream('AppStart', [System.IO.Pipes.PipeDirection]::InOut, 4)
            $s.Dispose()
        }))
    }
    'progfiles' {
        foreach ($dir in $prog, $cur, (Join-Path $cur 'TestApp')) {
            $out.Add((Try-Access "newFile[$dir]" { [System.IO.File]::WriteAllText((Join-Path $dir 'operator-probe.txt'), 'x') }))
        }
        foreach ($file in (Join-Path $cur 'AppStart.Launcher.exe'), (Join-Path $cur 'TestApp\AppStart.TestApp.exe')) {
            $out.Add((Try-Access "openWrite[$(Split-Path $file -Leaf)]" { $s = [System.IO.File]::Open($file, 'Open', 'Write', 'ReadWrite'); $s.Dispose() }))
            $out.Add((Try-Access "rename[$(Split-Path $file -Leaf)]" { [System.IO.File]::Move($file, "$file.old") }))
        }
    }
    'start-direct' {
        # Start an EXE with requireAdministrator directly, like a double click without UAC consent:
        # CreateProcess must fail with 740 (ERROR_ELEVATION_REQUIRED).
        $out.Add((Try-Access 'start' {
            $psi = New-Object System.Diagnostics.ProcessStartInfo($Argument)
            $psi.UseShellExecute = $false
            $psi.WorkingDirectory = Split-Path $Argument
            if ($Argument -like '*AppStart.Setup.exe') { $psi.Arguments = 'verify' }
            $p = [System.Diagnostics.Process]::Start($psi)
            try { if (-not $p.WaitForExit(5000)) { $p.Kill() } } finally { $p.Dispose() }
        }))
    }
    'start-invoker' {
        # Bypass the manifest (RunAsInvoker): the TestApp itself must refuse to run and exit with code 1.
        $psi = New-Object System.Diagnostics.ProcessStartInfo($Argument)
        $psi.UseShellExecute = $false
        $psi.WorkingDirectory = Split-Path $Argument
        $psi.EnvironmentVariables['__COMPAT_LAYER'] = 'RunAsInvoker'
        try {
            $p = [System.Diagnostics.Process]::Start($psi)
            if ($p.WaitForExit(90000)) { $out.Add("exit=$($p.ExitCode)") } else { $p.Kill(); $out.Add('exit=TIMEOUT') }
        } catch { $out.Add("start=ERROR:$($_.Exception.Message -replace '\s+', ' ')") }
    }
    default { $out.Add("unknown action $Action") }
}

[System.IO.File]::WriteAllText($ResultFile, ($out -join ' '), [System.Text.Encoding]::UTF8)
