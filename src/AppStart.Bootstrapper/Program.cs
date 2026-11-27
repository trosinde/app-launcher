using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Windows.Win32.System.LibraryLoader;
using Windows.Win32;

// P/Invokes of this assembly resolve only from System32, never from the folder the EXE was started from.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AppStart.Bootstrapper;

/// <summary>
/// AppStart-Setup.exe (task 7.5, design D6, spec installation "Interactive installation via bootstrapper"):
///   (no arguments)  extract the embedded MSI into a protected folder, msiexec /qn with log and progress
///                   dialog, evaluate the exit code, then start the installed AppStart.Setup.exe (wizard)
///                   after the signature check.
///   /quiet          install only, no UI, no wizard; exit code = msiexec exit code.
///   /?              short help.
/// Never loads or starts anything from its own folder (often Downloads) or from %TEMP%.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")] // Windows 10 and 11 only (project.md).
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Any DLL loaded later (msi.dll, wintrust.dll, comctl32 helpers, ...) comes from System32 only.
        PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_SYSTEM32);

        var parsed = BootstrapperLogic.ParseArguments(args);
        switch (parsed.Mode)
        {
            case RunMode.Help:
                Dialog.Show(BootstrapperLogic.Help);
                return BootstrapperLogic.ExitOk;
            case RunMode.Invalid:
                if (parsed.ShowUi)
                {
                    Dialog.Show(BootstrapperLogic.InvalidArgument(parsed.Invalid!));
                }

                return BootstrapperLogic.ExitInvalidArgument;
        }

        using var log = new BootstrapLog();
        var ui = parsed.ShowUi;
        try
        {
            return Run(ui, log, parsed.ConfigPath);
        }
        catch (Exception ex)
        {
            log.Write("Unexpected error: " + ex);
            Report(ui, BootstrapperLogic.Unexpected(ex.Message, log.FilePath));
            return BootstrapperLogic.ExitInternalError;
        }
    }

    private static int Run(bool ui, BootstrapLog log, string? configPath)
    {
        var paths = BootstrapperPaths.ForMachine();
        var now = DateTime.Now;
        log.Write($"AppStart-Setup.exe {typeof(Program).Assembly.GetName().Version} started from {Environment.ProcessPath}, mode {(ui ? "interactive" : "/quiet")}{(configPath is null ? string.Empty : ", answer file " + configPath)}.");

        if (!IsElevated())
        {
            Report(ui, BootstrapperLogic.NotAdmin);
            return BootstrapperLogic.ExitNotAdmin;
        }

        using var embedded = MsiPackage.OpenEmbedded();
        if (embedded is null)
        {
            Report(ui, BootstrapperLogic.NoPackage);
            return BootstrapperLogic.ExitNoPackage;
        }

        // Protected folders before anything is written: ProgramData\AppStart, Logs, Installer, Installer\<Guid>.
        var runId = Guid.NewGuid();
        var extractionDir = paths.ExtractionDirectory(runId);
        if (!paths.IsSafeExtractionPath(extractionDir))
        {
            Report(ui, BootstrapperLogic.UnsafeFolder(extractionDir, null));
            return BootstrapperLogic.ExitInternalError;
        }

        try
        {
            ProtectedFolders.Ensure(paths.DataDirectory);
            ProtectedFolders.Ensure(paths.LogsDirectory);
            log.Open(paths.BootstrapperLog(now));
            ProtectedFolders.Ensure(paths.InstallerRoot);
            ProtectedFolders.CreateNew(extractionDir);
        }
        catch (UnsafeFolderException ex)
        {
            log.Write("Folder not protected: " + ex.Message);
            Report(ui, BootstrapperLogic.UnsafeFolder(ex.Folder, log.FilePath));
            return BootstrapperLogic.ExitInternalError;
        }

        var msiPath = paths.ExtractedMsi(runId);
        var installLog = paths.InstallLog(now);
        InstalledProduct? installed;
        string? upgradeCode;
        int msiExit;
        try
        {
            try
            {
                MsiPackage.Extract(embedded, msiPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Write("Extraction failed: " + ex);
                Report(ui, BootstrapperLogic.ExtractFailed(extractionDir, ex.Message, log.FilePath));
                return BootstrapperLogic.ExitInternalError;
            }

            // Held open (read-only share) until msiexec has finished: nobody can replace the package meanwhile.
            using var locked = new FileStream(msiPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var info = MsiPackage.ReadInfo(msiPath);
            upgradeCode = info.UpgradeCode;
            installed = MsiPackage.FindInstalled(upgradeCode);
            log.Write($"Package {msiPath}: version {info.ProductVersion ?? "?"}, UpgradeCode {info.UpgradeCode ?? "?"}; installed: {installed?.Version ?? "nothing"}.");

            if (installed is not null && BootstrapperLogic.ShouldSkipInstall(installed.Version, info.ProductVersion))
            {
                log.Write("Same or newer version installed: installation skipped.");
                if (!ui)
                {
                    return configPath is null ? BootstrapperLogic.ExitOk : RunApply(paths, installed, log, configPath, BootstrapperLogic.ExitOk, now);
                }

                Dialog.Show(BootstrapperLogic.AlreadyInstalled(installed.Version!, info.ProductVersion!));
                return StartWizard(paths, installed, log);
            }

            var msiexec = paths.Msiexec;
            var arguments = BootstrapperLogic.MsiexecArguments(msiPath, installLog);
            log.Write($"Starting {msiexec} {arguments}");
            Process process;
            try
            {
                process = Process.Start(new ProcessStartInfo(msiexec, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.Combine(paths.WindowsDirectory, "System32"),
                }) ?? throw new Win32Exception("Process was not created.");
            }
            catch (Win32Exception ex)
            {
                log.Write("msiexec cannot be started: " + ex);
                Report(ui, BootstrapperLogic.MsiexecFailed(ex.Message, log.FilePath));
                return BootstrapperLogic.ExitInternalError;
            }

            using (process)
            {
                var wait = process.WaitForExitAsync();
                if (ui)
                {
                    Dialog.ShowProgressUntil(wait);
                }

                wait.GetAwaiter().GetResult();
                msiExit = process.ExitCode;
            }

            log.Write($"msiexec ended with {msiExit}. Log: {installLog}");
        }
        finally
        {
            DeleteExtracted(extractionDir, log);
        }

        var outcome = BootstrapperLogic.EvaluateMsiexec(msiExit, installLog);
        if (!ui)
        {
            // /quiet /config: set up from the answer file only after a successful installation (design D6).
            return configPath is not null && outcome.Success
                ? RunApply(paths, MsiPackage.FindInstalled(upgradeCode) ?? installed, log, configPath, msiExit, now)
                : msiExit;
        }

        if (!outcome.Success)
        {
            Dialog.Show(outcome.Message!);
            return outcome.ExitCode;
        }

        if (outcome.Message is not null)
        {
            Dialog.Show(outcome.Message); // 3010: restart required, then continue with the wizard.
        }

        // Re-read after the installation: install location of the (new) product.
        var current = MsiPackage.FindInstalled(upgradeCode) ?? installed;
        return StartWizard(paths, current, log);
    }

    private static int StartWizard(BootstrapperPaths paths, InstalledProduct? installed, BootstrapLog log)
    {
        var setupPath = SetupStarter.LocateSetup(paths, installed);
        var result = SetupStarter.VerifyAndStart(setupPath, SetupStarter.OwnSignerThumbprint(), log);
        log.Write($"Setup {setupPath}: {result.Status} ({result.Detail}).");
        switch (result.Status)
        {
            case SetupStartStatus.Started:
                return BootstrapperLogic.ExitOk;
            case SetupStartStatus.Missing:
                Dialog.Show(BootstrapperLogic.SetupMissing(setupPath, log.FilePath));
                return BootstrapperLogic.ExitSetupMissing;
            case SetupStartStatus.Untrusted:
                Dialog.Show(BootstrapperLogic.SetupUntrusted(setupPath, log.FilePath));
                return BootstrapperLogic.ExitSetupUntrusted;
            default:
                Dialog.Show(BootstrapperLogic.SetupStartFailed(setupPath, result.Detail, log.FilePath));
                return BootstrapperLogic.ExitSetupStartFailed;
        }
    }

    // /quiet /config: installed AppStart.Setup.exe (signature checked like the wizard) with apply --config, no UI.
    private static int RunApply(BootstrapperPaths paths, InstalledProduct? installed, BootstrapLog log, string configPath, int msiExit, DateTime now)
    {
        var setupPath = SetupStarter.LocateSetup(paths, installed);
        var applyLog = Path.Combine(paths.LogsDirectory, BootstrapperLogic.ApplyLogFileName(now));
        var result = SetupStarter.VerifyAndRun(setupPath, SetupStarter.OwnSignerThumbprint(), log,
            BootstrapperLogic.ApplyArguments(configPath), applyLog, out var applyExit);
        log.Write($"Setup {setupPath}: {result.Status} ({result.Detail}). Output: {applyLog}");
        return result.Status switch
        {
            SetupStartStatus.Started => BootstrapperLogic.MapApplyExit(applyExit, msiExit),
            SetupStartStatus.Missing => BootstrapperLogic.ExitSetupMissing,
            SetupStartStatus.Untrusted => BootstrapperLogic.ExitSetupUntrusted,
            _ => BootstrapperLogic.ExitSetupStartFailed,
        };
    }

    private static void DeleteExtracted(string extractionDir, BootstrapLog log)
    {
        try
        {
            if (Directory.Exists(extractionDir))
            {
                Directory.Delete(extractionDir, recursive: true);
            }

            log.Write($"Extracted package deleted: {extractionDir}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write($"Extracted package could not be deleted ({extractionDir}): {ex.Message}");
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void Report(bool ui, BootstrapperMessage message)
    {
        if (ui)
        {
            Dialog.Show(message);
        }
    }
}
