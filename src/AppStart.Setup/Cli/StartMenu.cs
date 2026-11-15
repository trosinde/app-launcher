namespace AppStart.Setup.Cli;

/// <summary>
/// AppStart's own Start menu entries in "AppStart" (public Start menu), created/removed by the Velopack
/// hooks of the service via "AppStart.Setup.exe startmenu --create|--remove" (design D5, tasks 6.7, 9.2).
/// </summary>
internal static class StartMenu
{
    public const string FolderName = "AppStart";
    public const string SetupEntry = "AppStart – Einrichtung";
    public const string VerifyEntry = "AppStart – Einrichtung prüfen";

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), FolderName);

    public static int Apply(bool create, bool remove)
    {
        if (create == remove)
        {
            return 2;
        }

        if (remove)
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }

            return 0;
        }

        var setup = Path.Combine(AppContext.BaseDirectory, "AppStart.Setup.exe");
        var shortcuts = Integration.SetupComposition.CreateShortcutWriter();
        shortcuts(Path.Combine(Folder, SetupEntry + ".lnk"), setup, string.Empty, setup + ",0", AppContext.BaseDirectory, "AppStart einrichten: Applikationen, Konten und Gruppe");
        shortcuts(Path.Combine(Folder, VerifyEntry + ".lnk"), setup, "verify --ui", setup + ",0", AppContext.BaseDirectory, "Einrichtung prüfen und Prüfbericht erstellen");
        return 0;
    }
}
