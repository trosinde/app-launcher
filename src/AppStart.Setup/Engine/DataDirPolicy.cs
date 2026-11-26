namespace AppStart.Setup.Engine;

/// <summary>
/// spec fix-wizard-findings «Warnung bei Datenordner im Programmordner»: appstart-app gets full control of its
/// data folder. When that folder is the program folder of the application, lies in it or contains it, the
/// application could replace its own EXE and DLLs; replaced DLLs are not caught by the hash or signature check.
/// </summary>
public static class DataDirPolicy
{
    /// <summary>True when <paramref name="dataDir"/> is the folder of <paramref name="exePath"/>, lies in it or contains it.</summary>
    public static bool OverlapsProgramFolder(string? dataDir, string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        if (string.IsNullOrWhiteSpace(dataDir) || !Path.IsPathFullyQualified(dataDir.Trim()) || !Path.IsPathFullyQualified(exePath))
        {
            return false;
        }

        var data = Normalize(dataDir);
        var program = Normalize(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? string.Empty);
        return program.Length > 0 && (IsSameOrBelow(data, program) || IsSameOrBelow(program, data));
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    private static bool IsSameOrBelow(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
