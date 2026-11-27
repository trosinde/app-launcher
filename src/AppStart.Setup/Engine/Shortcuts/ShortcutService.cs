using System.IO;
using System.Runtime.InteropServices;
using AppStart.Core.Configuration;
using Microsoft.Win32;

namespace AppStart.Setup.Engine.Shortcuts;

/// <summary>
/// Launcher shortcuts per app (public Start menu and public desktop) and the handling of existing shortcuts
/// that point directly to an app EXE (concept "Installation and selection of the applications", task 6.4).
/// All roots are injectable so tests never touch the real Start menu or desktop.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    private static readonly HashSet<string> SkippedProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Public", "Default", "Default User", "All Users",
    };

    private static readonly EnumerationOptions RecursiveLinks = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        // Do not follow junctions/symlinks (loops, other profiles); hidden shortcuts still count.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly string _publicStartMenu;
    private readonly string _publicDesktop;
    private readonly string _profilesRoot;
    private readonly string _backupDirectory;

    /// <summary>Real default roots (public Start menu, public desktop, C:\Users, C:\ProgramData\AppStart\ShortcutBackup).</summary>
    public ShortcutService()
        : this(null, null, null, null)
    {
    }

    /// <summary>Injectable roots (tests); null takes the default.</summary>
    /// <param name="publicStartMenu">Default: CommonPrograms (C:\ProgramData\Microsoft\Windows\Start Menu\Programs).</param>
    /// <param name="publicDesktop">Default: CommonDesktopDirectory (C:\Users\Public\Desktop).</param>
    /// <param name="profilesRoot">Default: ProfilesDirectory from the registry (C:\Users).</param>
    /// <param name="backupDirectory">Default: C:\ProgramData\AppStart\ShortcutBackup.</param>
    public ShortcutService(
        string? publicStartMenu,
        string? publicDesktop,
        string? profilesRoot,
        string? backupDirectory)
    {
        _publicStartMenu = Path.GetFullPath(publicStartMenu ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
        _publicDesktop = Path.GetFullPath(publicDesktop ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        _profilesRoot = Path.GetFullPath(profilesRoot ?? DefaultProfilesRoot());
        _backupDirectory = Path.GetFullPath(backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppStart", "ShortcutBackup"));
    }

    /// <summary>Same as <see cref="ShortcutService()"/>.</summary>
    public static ShortcutService CreateDefault() => new();

    public IReadOnlyList<string> CreateAppShortcuts(AppEntry app, string launcherPath)
    {
        ArgumentNullException.ThrowIfNull(app);
        RequireFullPath(launcherPath, "The path to the launcher");
        RequireFullPath(app.Exe, $"The path to the EXE of \"{app.Name}\"");

        var fileName = ShortcutNames.ToFileName(app.Name);
        var arguments = $"--app {app.Id}";
        var workingDirectory = string.IsNullOrWhiteSpace(app.WorkingDir)
            ? Path.GetDirectoryName(Path.GetFullPath(app.Exe)) ?? string.Empty
            : app.WorkingDir;
        string[] paths = [Path.Combine(_publicStartMenu, fileName), Path.Combine(_publicDesktop, fileName)];

        // Check both places before writing anything, so a conflict leaves no half-done state.
        foreach (var path in paths)
        {
            if (File.Exists(path) && !IsOwnShortcut(path, launcherPath, arguments))
            {
                throw new InvalidOperationException(
                    $"The shortcut \"{path}\" already exists and does not belong to this application. " +
                    "It is not overwritten. Please check the file and back it up or remove it by hand.");
            }
        }

        foreach (var path in paths)
        {
            Write(path, Path.GetFullPath(launcherPath), arguments, Path.GetFullPath(app.Exe), 0, workingDirectory, app.Name);
        }

        return paths;
    }

    /// <summary>
    /// Writes any shortcut (e.g. AppStart's own Start menu entries). Creates the folder if needed and
    /// replaces an existing file at <paramref name="lnkPath"/>. <paramref name="iconLocation"/> is
    /// "path,index" or just a path (index 0); empty means the target's own icon.
    /// </summary>
    public void CreateShortcut(string lnkPath, string targetPath, string arguments, string iconLocation, string workingDirectory, string? description = null)
    {
        RequireFullPath(lnkPath, "The path to the shortcut");
        RequireFullPath(targetPath, "The target of the shortcut");
        if (!string.Equals(Path.GetExtension(lnkPath), ShortcutNames.Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The path to the shortcut must end in .lnk: \"{lnkPath}\".", nameof(lnkPath));
        }

        var (iconPath, iconIndex) = ParseIconLocation(iconLocation);
        Write(Path.GetFullPath(lnkPath), targetPath, arguments ?? string.Empty, iconPath, iconIndex, workingDirectory ?? string.Empty, description ?? string.Empty);
    }

    /// <summary>"C:\x\app.exe,3" → (path, 3); "C:\x\app.ico" → (path, 0). A comma without a number belongs to the path.</summary>
    internal static (string Path, int Index) ParseIconLocation(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return (string.Empty, 0);
        }

        var comma = iconLocation.LastIndexOf(',');
        if (comma > 0 && int.TryParse(iconLocation.AsSpan(comma + 1).Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            return (iconLocation[..comma].Trim(), index);
        }

        return (iconLocation.Trim(), 0);
    }

    // Saves to a temp file next to the target and moves it over, so a failed save never leaves a broken .lnk.
    private static void Write(string path, string target, string arguments, string iconPath, int iconIndex, string workingDirectory, string description)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $"~appstart-{Guid.NewGuid():N}.lnk");
        try
        {
            ShellLink.Save(temp, target, arguments, workingDirectory, iconPath, iconIndex, description);
            File.Move(temp, path, overwrite: true);
        }
        catch (COMException ex)
        {
            throw new IOException($"The shortcut \"{path}\" could not be written: {ex.Message}", ex);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public IReadOnlyList<string> FindDirectShortcuts(string exePath)
    {
        RequireFullPath(exePath, "The path to the EXE");
        var exe = Normalize(exePath);
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in EnumerateCandidateLinks())
        {
            if (!seen.Add(link))
            {
                continue;
            }

            var info = Read(link);
            if (info is not null && SamePath(info.TargetPath, exe))
            {
                found.Add(link);
            }
        }

        return found;
    }

    public ShortcutBackup BackupAndRemove(string shortcutPath, string appId)
    {
        RequireFullPath(shortcutPath, "The path to the shortcut");
        var original = Path.GetFullPath(shortcutPath);
        if (!File.Exists(original))
        {
            throw new FileNotFoundException($"The shortcut \"{original}\" does not exist and cannot be backed up.", original);
        }

        Directory.CreateDirectory(_backupDirectory);
        var backup = Path.Combine(_backupDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(original)}");

        // A move (rename on the same volume) never leaves the shortcut lost: either it is still at the
        // original place or already in the backup folder.
        File.Move(original, backup, overwrite: false);
        return new ShortcutBackup(original, backup, appId);
    }

    public void Restore(ShortcutBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (!File.Exists(backup.BackupPath))
        {
            throw new FileNotFoundException(
                $"The backup \"{backup.BackupPath}\" of the shortcut \"{backup.OriginalPath}\" is missing. It cannot be restored.",
                backup.BackupPath);
        }

        if (File.Exists(backup.OriginalPath) || Directory.Exists(backup.OriginalPath))
        {
            throw new IOException(ExistsMessage(backup));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalPath)!);
        try
        {
            File.Move(backup.BackupPath, backup.OriginalPath, overwrite: false);
        }
        catch (IOException ex) when (File.Exists(backup.OriginalPath))
        {
            // Created between the check and the move.
            throw new IOException(ExistsMessage(backup), ex);
        }
    }

    public void Delete(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            if (!string.Equals(Path.GetExtension(path), ShortcutNames.Extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"\"{path}\" is not a shortcut (.lnk) and is not deleted.", nameof(paths));
            }

            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // Folder is gone, so is the shortcut.
            }
        }
    }

    public ShortcutInfo? Read(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return ShellLink.Load(path);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }

    private bool IsOwnShortcut(string path, string launcherPath, string arguments)
    {
        var info = Read(path);
        return info is not null
            && SamePath(info.TargetPath, Normalize(launcherPath))
            && string.Equals(info.Arguments.Trim(), arguments, StringComparison.Ordinal);
    }

    private IEnumerable<string> EnumerateCandidateLinks()
    {
        IEnumerable<string> roots = [_publicStartMenu, _publicDesktop, .. EnumerateProfileFolders()];
        foreach (var root in roots)
        {
            foreach (var link in EnumerateLinks(root))
            {
                yield return link;
            }
        }
    }

    private IEnumerable<string> EnumerateProfileFolders()
    {
        List<string> profiles;
        try
        {
            profiles = [.. new DirectoryInfo(_profilesRoot).EnumerateDirectories()
                .Where(d => !SkippedProfiles.Contains(d.Name) && !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(d => d.FullName)];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        foreach (var profile in profiles)
        {
            yield return Path.Combine(profile, "Desktop");
            yield return Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs");
        }
    }

    private static List<string> EnumerateLinks(string root)
    {
        try
        {
            // Materialised here so Access Denied / vanished folders during enumeration are caught.
            return Directory.Exists(root) ? [.. Directory.EnumerateFiles(root, "*.lnk", RecursiveLinks)] : [];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static bool SamePath(string candidate, string normalizedExpected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            return string.Equals(Normalize(candidate), normalizedExpected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))));

    private static void RequireFullPath(string path, string what)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"{what} must be a full path: \"{path}\".");
        }
    }

    private static string ExistsMessage(ShortcutBackup backup) =>
        $"A file already exists at the original location \"{backup.OriginalPath}\". The backed-up shortcut " +
        $"is not written over it and stays in \"{backup.BackupPath}\". Please check by hand.";

    private static string DefaultProfilesRoot()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (key?.GetValue("ProfilesDirectory") is string configured && !string.IsNullOrWhiteSpace(configured))
        {
            return Environment.ExpandEnvironmentVariables(configured);
        }

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        return systemDrive + @"\Users";
    }
}
