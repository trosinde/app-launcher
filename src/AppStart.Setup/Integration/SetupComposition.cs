using AppStart.Setup.Engine;

namespace AppStart.Setup.Integration;

/// <summary>Composition root: wires the real services into the engine (filled in when the areas are merged).</summary>
internal static class SetupComposition
{
    public static SetupEngine CreateEngine() => throw new NotImplementedException("Integration pending.");

    /// <summary>(lnkPath, target, arguments, icon, workingDir, description)</summary>
    public static Action<string, string, string, string, string, string> CreateShortcutWriter()
    {
        var service = Engine.Shortcuts.ShortcutService.CreateDefault();
        return (lnk, target, args, icon, dir, description) => service.CreateShortcut(lnk, target, args, icon, dir, description);
    }
}
