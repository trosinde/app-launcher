using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using AppStart.Core.Integrity;

namespace AppStart.Bootstrapper;

internal enum SetupStartStatus
{
    Started,
    Missing,
    Untrusted,
    StartFailed,
}

internal sealed record SetupStartResult(SetupStartStatus Status, string SetupPath, string Detail);

/// <summary>
/// Starts the installed AppStart.Setup.exe (wizard, no arguments) after an Authenticode check pinned to the
/// bootstrapper's own signer. The file stays locked (FileShare.Read) from the check until the process exists,
/// so it cannot be swapped in between (design D7). The child inherits the elevated token: no second UAC prompt.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")] // Windows 10 and 11 only (project.md).
internal static class SetupStarter
{
    /// <summary>Thumbprint of the bootstrapper's own valid signature, null when unsigned (development build).</summary>
    public static string? OwnSignerThumbprint()
    {
        var self = Environment.ProcessPath;
        if (self is null)
        {
            return null;
        }

        using var exe = LockedExecutable.Open(self);
        return ExecutableVerifier.TryGetValidSignerThumbprint(exe);
    }

    /// <summary>Install root: Windows Installer, then Velopack's uninstall entry, else C:\Program Files\AppStart; always below Program Files.</summary>
    public static string LocateSetup(BootstrapperPaths paths, InstalledProduct? installed)
    {
        var reported = installed?.InstallLocation;
        if (string.IsNullOrWhiteSpace(reported))
        {
            reported = MsiPackage.VelopackInstallLocation();
        }

        return BootstrapperPaths.SetupExecutable(paths.ResolveInstallRoot(reported));
    }

    public static SetupStartResult VerifyAndStart(string setupPath, string? ownThumbprint, BootstrapLog log) =>
        VerifyThenStart(setupPath, ownThumbprint, log, null, null, out _);

    /// <summary>
    /// /quiet /config (design D6): same signature check, then AppStart.Setup.exe with <paramref name="arguments"/>
    /// without UI; waits and writes its output to <paramref name="outputLog"/>. <paramref name="exitCode"/> is the
    /// exit code of apply when the status is Started.
    /// </summary>
    public static SetupStartResult VerifyAndRun(string setupPath, string? ownThumbprint, BootstrapLog log, string arguments, string outputLog, out int exitCode) =>
        VerifyThenStart(setupPath, ownThumbprint, log, arguments, outputLog, out exitCode);

    private static SetupStartResult VerifyThenStart(string setupPath, string? ownThumbprint, BootstrapLog log, string? arguments, string? outputLog, out int exitCode)
    {
        exitCode = -1;
        LockedExecutable exe;
        try
        {
            exe = LockedExecutable.Open(setupPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(SetupStartStatus.Missing, setupPath, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SetupStartStatus.StartFailed, setupPath, ex.Message);
        }

        using (exe)
        {
            var setupThumbprint = ExecutableVerifier.TryGetValidSignerThumbprint(exe);
            switch (BootstrapperLogic.DecideSignature(ownThumbprint, setupThumbprint))
            {
                case SignatureDecision.SkippedUnsignedBootstrapper:
                    log.Write($"WARNING: AppStart-Setup.exe is not signed (development build). Signature check of {setupPath} skipped (signature there: {setupThumbprint ?? "none valid"}).");
                    break;
                case SignatureDecision.Untrusted:
                    return new(SetupStartStatus.Untrusted, setupPath,
                        $"expected thumbprint {ownThumbprint}, found {setupThumbprint ?? "no valid signature"}");
                default:
                    log.Write($"Signature of {setupPath} valid, thumbprint {setupThumbprint}.");
                    break;
            }

            try
            {
                var start = new ProcessStartInfo(setupPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(setupPath)!,
                };
                if (arguments is null)
                {
                    using var wizard = Process.Start(start);
                    return wizard is null
                        ? new(SetupStartStatus.StartFailed, setupPath, "Process was not created.")
                        : new(SetupStartStatus.Started, setupPath, $"PID {wizard.Id}");
                }

                // apply without UI: output (stdout and stderr) into the log, wait for the end.
                start.Arguments = arguments;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = System.Text.Encoding.UTF8;
                start.StandardErrorEncoding = System.Text.Encoding.UTF8;
                using var output = new StreamWriter(outputLog!, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
                var gate = new Lock();
                using var process = new Process { StartInfo = start };
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (gate) { output.WriteLine(e.Data); } } };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (gate) { output.WriteLine(e.Data); } } };
                if (!process.Start())
                {
                    return new(SetupStartStatus.StartFailed, setupPath, "Process was not created.");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                exitCode = process.ExitCode;
                return new(SetupStartStatus.Started, setupPath, $"{arguments}: exit code {exitCode}");
            }
            catch (Win32Exception ex)
            {
                return new(SetupStartStatus.StartFailed, setupPath, ex.Message);
            }
        }
    }
}
