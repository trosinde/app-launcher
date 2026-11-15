using System.Text;

namespace AppStart.Setup.Engine.Shortcuts;

/// <summary>
/// Turns an application name into a Windows file name for its shortcut. The launcher shows the shortcut
/// name as window title, so the result stays as close to the name as possible.
/// </summary>
public static class ShortcutNames
{
    public const string Extension = ".lnk";

    /// <summary>Maximum length of the name without ".lnk"; keeps full paths well below MAX_PATH.</summary>
    public const int MaxLength = 120;

    private const char Replacement = '_';

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
        "CONIN$", "CONOUT$",
    };

    /// <summary>"&lt;sanitised name&gt;.lnk".</summary>
    public static string ToFileName(string appName) => Sanitize(appName) + Extension;

    /// <summary>
    /// Replaces characters Windows forbids in file names (control characters and &lt;&gt;:"/\|?*) with '_',
    /// trims leading spaces and trailing dots/spaces, shortens to <see cref="MaxLength"/> and escapes
    /// reserved device names (CON, NUL, COM1 ...) with a leading '_'.
    /// </summary>
    public static string Sanitize(string appName)
    {
        ArgumentNullException.ThrowIfNull(appName);

        var builder = new StringBuilder(appName.Length);
        foreach (var c in appName)
        {
            builder.Append(IsForbidden(c) ? Replacement : c);
        }

        var name = Trim(builder.ToString());
        if (name.Length > MaxLength)
        {
            name = Trim(name[..MaxLength]);
        }

        if (name.Length == 0 || name.All(c => c == Replacement))
        {
            throw new ArgumentException(
                $"Aus dem Namen «{appName}» lässt sich kein gültiger Dateiname für die Verknüpfung bilden.",
                nameof(appName));
        }

        // Windows treats "CON", "CON.txt" and "CON .txt" as the device; the part before the first dot counts.
        var stem = name.Split('.')[0].TrimEnd(' ');
        return ReservedNames.Contains(stem) ? Replacement + name : name;
    }

    private static bool IsForbidden(char c) => c < 32 || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';

    private static string Trim(string value) => value.TrimStart(' ').TrimEnd('.', ' ');
}
