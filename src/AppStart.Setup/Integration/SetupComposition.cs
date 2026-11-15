using AppStart.Setup.Engine;

namespace AppStart.Setup.Integration;

/// <summary>Composition root: wires the real services into the engine (filled in when the areas are merged).</summary>
internal static class SetupComposition
{
    public static SetupEngine CreateEngine()
    {
        var paths = SetupPaths.Default;
        var accounts = new Engine.Accounts.AccountService();
        var protection = new Engine.Protection.ProtectionService();
        var shortcuts = Engine.Shortcuts.ShortcutService.CreateDefault();
        var verifier = new Engine.Verification.Verifier(
            accounts, protection, shortcuts, new Engine.Verification.SystemProbe(),
            Engine.Verification.Verifier.DefaultInstallationFolder, paths.LauncherPath, paths.ConfigPath);
        return new SetupEngine(accounts, protection, shortcuts, verifier, new Engine.Protection.SetupStateStore(protection), paths);
    }

    /// <summary>(lnkPath, target, arguments, icon, workingDir, description)</summary>
    public static Action<string, string, string, string, string, string> CreateShortcutWriter()
    {
        var service = Engine.Shortcuts.ShortcutService.CreateDefault();
        return (lnk, target, args, icon, dir, description) => service.CreateShortcut(lnk, target, args, icon, dir, description);
    }
}
