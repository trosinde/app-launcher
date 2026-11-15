namespace AppStart.Setup.Engine.Protection;

/// <summary>Parses the registry key of an app ("HKLM\SOFTWARE\Vendor\App", Registry64 view).</summary>
internal static class RegistryPath
{
    // Shared keys that must never be locked, even when entered by mistake.
    private static readonly HashSet<string> ForbiddenBelowSoftware = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft",
        "Classes",
        "Policies",
        "WOW6432Node",
        "Clients",
        "RegisteredApplications",
    };

    /// <summary>Returns the normalized path ("HKLM\SOFTWARE\...") and the sub key below HKLM.</summary>
    public static (string Normalized, string SubKey) Parse(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var segments = key.Trim().Split('\\');

        if (segments.Length == 0
            || !(segments[0].Equals("HKLM", StringComparison.OrdinalIgnoreCase)
                 || segments[0].Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Registry-Key «{key}» muss mit HKLM\\SOFTWARE\\ beginnen.", nameof(key));
        }

        if (segments.Length < 3 || !segments[1].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Registry-Key «{key}» muss unterhalb von HKLM\\SOFTWARE\\ liegen.", nameof(key));
        }

        if (segments.Any(s => s.Trim().Length == 0 || s.Contains('/', StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Registry-Key «{key}» enthält einen leeren oder ungültigen Teil.", nameof(key));
        }

        if (segments.Length == 3 && ForbiddenBelowSoftware.Contains(segments[2]))
        {
            throw new ArgumentException($"Registry-Key «{key}» ist ein gemeinsamer Systemschlüssel und darf nicht gesperrt werden.", nameof(key));
        }

        var subKey = string.Join('\\', segments.Skip(1));
        return ("HKLM\\" + subKey, subKey);
    }
}
