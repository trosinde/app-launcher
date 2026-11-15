namespace AppStart.Bootstrapper;

/// <summary>
/// Path rules of the bootstrapper (design D6, project.md: elevated code only from folders operators cannot
/// write to). The MSI is extracted only below C:\ProgramData\AppStart\Installer\&lt;Guid&gt;, never to %TEMP%
/// or next to the bootstrapper (Downloads). Roots are injectable for tests.
/// </summary>
public sealed class BootstrapperPaths
{
    public const string InstallerFolderName = "Installer";
    public const string LogsFolderName = "Logs";
    public const string MsiFileName = "AppStart.msi";

    /// <summary>
    /// Folders the bootstrapper creates with this descriptor: owner Administrators, protected DACL
    /// (no inheritance from ProgramData, where users may create files), full access for SYSTEM and
    /// Administrators only, inherited by files and subfolders. Same rights AppStart.Setup sets later.
    /// </summary>
    public const string ProtectedFolderSddl = "O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

    public BootstrapperPaths(string dataDirectory, string programFiles, string windowsDirectory, string tempDirectory, string? bootstrapperDirectory)
    {
        DataDirectory = Normalize(dataDirectory);
        ProgramFiles = Normalize(programFiles);
        WindowsDirectory = Normalize(windowsDirectory);
        TempDirectory = Normalize(tempDirectory);
        BootstrapperDirectory = bootstrapperDirectory is null ? null : Normalize(bootstrapperDirectory);
    }

    /// <summary>Real machine paths. ProgramFiles, Windows and ProgramData come from known folders, not from environment variables.</summary>
    public static BootstrapperPaths ForMachine() => new(
        AppStart.Core.AppStartConstants.DataDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Path.GetTempPath(),
        Path.GetDirectoryName(Environment.ProcessPath));

    /// <summary>C:\ProgramData\AppStart</summary>
    public string DataDirectory { get; }

    /// <summary>C:\Program Files</summary>
    public string ProgramFiles { get; }

    public string WindowsDirectory { get; }

    public string TempDirectory { get; }

    /// <summary>Folder the bootstrapper was started from (often Downloads): never used for anything.</summary>
    public string? BootstrapperDirectory { get; }

    /// <summary>C:\ProgramData\AppStart\Installer</summary>
    public string InstallerRoot => Path.Combine(DataDirectory, InstallerFolderName);

    /// <summary>C:\ProgramData\AppStart\Logs</summary>
    public string LogsDirectory => Path.Combine(DataDirectory, LogsFolderName);

    /// <summary>%SystemRoot%\System32\msiexec.exe (full path, no search).</summary>
    public string Msiexec => Path.Combine(WindowsDirectory, "System32", "msiexec.exe");

    /// <summary>C:\Program Files\AppStart (Velopack per-machine default).</summary>
    public string DefaultInstallRoot => Path.Combine(ProgramFiles, "AppStart");

    /// <summary>C:\ProgramData\AppStart\Installer\&lt;Guid&gt;</summary>
    public string ExtractionDirectory(Guid id) => Path.Combine(InstallerRoot, id.ToString("D"));

    public string ExtractedMsi(Guid id) => Path.Combine(ExtractionDirectory(id), MsiFileName);

    public string InstallLog(DateTime now) => Path.Combine(LogsDirectory, BootstrapperLogic.InstallLogFileName(now));

    public string BootstrapperLog(DateTime now) => Path.Combine(LogsDirectory, BootstrapperLogic.BootstrapperLogFileName(now));

    /// <summary>
    /// The extraction folder must lie strictly below C:\ProgramData\AppStart\Installer and never below
    /// %TEMP% or the bootstrapper's own folder. Relative paths and ".." segments are rejected.
    /// </summary>
    public bool IsSafeExtractionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || ContainsDotSegments(path))
        {
            return false;
        }

        var full = Normalize(path);
        return IsStrictlyBelow(full, InstallerRoot)
               && !IsBelowOrEqual(full, TempDirectory)
               && (BootstrapperDirectory is null || !IsBelowOrEqual(full, BootstrapperDirectory));
    }

    /// <summary>
    /// Install root reported by Windows Installer or the registry. Accepted only below Program Files
    /// (admin-only); anything else (null, relative, elsewhere) falls back to C:\Program Files\AppStart.
    /// </summary>
    public string ResolveInstallRoot(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return DefaultInstallRoot;
        }

        var trimmed = reported.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(trimmed) || ContainsDotSegments(trimmed))
        {
            return DefaultInstallRoot;
        }

        var full = Normalize(trimmed);
        return IsStrictlyBelow(full, ProgramFiles) ? full : DefaultInstallRoot;
    }

    /// <summary>Velopack layout: &lt;root&gt;\current\AppStart.Setup.exe.</summary>
    public static string SetupExecutable(string installRoot) => Path.Combine(installRoot, "current", BootstrapperLogic.SetupExeName);

    private static bool ContainsDotSegments(string path) =>
        path.Split('\\', '/').Any(segment => segment is "." or "..");

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsBelowOrEqual(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || IsStrictlyBelow(path, root);

    private static bool IsStrictlyBelow(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
