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

    public static SetupStartResult VerifyAndStart(string setupPath, string? ownThumbprint, BootstrapLog log)
    {
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
                    log.Write($"WARNUNG: AppStart-Setup.exe ist nicht signiert (Entwicklungs-Build). Signaturprüfung von {setupPath} übersprungen (Signatur dort: {setupThumbprint ?? "keine gültige"}).");
                    break;
                case SignatureDecision.Untrusted:
                    return new(SetupStartStatus.Untrusted, setupPath,
                        $"erwartet Thumbprint {ownThumbprint}, gefunden {setupThumbprint ?? "keine gültige Signatur"}");
                default:
                    log.Write($"Signatur von {setupPath} gültig, Thumbprint {setupThumbprint}.");
                    break;
            }

            try
            {
                var start = new ProcessStartInfo(setupPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(setupPath)!,
                };
                using var process = Process.Start(start);
                return process is null
                    ? new(SetupStartStatus.StartFailed, setupPath, "Prozess wurde nicht erstellt.")
                    : new(SetupStartStatus.Started, setupPath, $"PID {process.Id}");
            }
            catch (Win32Exception ex)
            {
                return new(SetupStartStatus.StartFailed, setupPath, ex.Message);
            }
        }
    }
}
