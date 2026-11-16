namespace AppStart.Core.Configuration;

/// <summary>Rules for helper programs in mode allowlist (add-allowed-child-processes, design D2 and D3).</summary>
public static class ChildProcessRules
{
    /// <summary>
    /// Shells and script hosts: never proposed by the learn mode, not selectable in its result,
    /// allowed only with explicit confirmation (wizard) or --allow-shell (command line).
    /// Compared by file name, so a renamed copy elsewhere is caught as well.
    /// </summary>
    public static IReadOnlySet<string> ShellsAndScriptHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "wscript.exe",
        "cscript.exe",
        "mshta.exe",
        "rundll32.exe",
        "regsvr32.exe",
        "explorer.exe",
    };

    public static bool IsShellOrScriptHost(string exePath) =>
        ShellsAndScriptHosts.Contains(Path.GetFileName(exePath));
}
