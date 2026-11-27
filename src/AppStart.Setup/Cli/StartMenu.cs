namespace AppStart.Setup.Cli;

/// <summary>
/// AppStart's own Start menu entries in "AppStart" (public Start menu), created/removed by the Velopack
/// hooks of the service via "AppStart.Setup.exe startmenu --create|--remove" (design D5, tasks 6.7, 9.2).
/// </summary>
internal static class StartMenu
{
    public const string FolderName = "AppStart";
    public const string SetupEntry = "AppStart – Setup";
    public const string VerifyEntry = "AppStart – Verify setup";
    public const string ManualEntry = "AppStart – Manual";
    public const string ManualFile = "AppStart-Manual.pdf";

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
        shortcuts(Path.Combine(Folder, SetupEntry + ".lnk"), setup, string.Empty, setup + ",0", AppContext.BaseDirectory, "Set up AppStart: applications, accounts and group");
        shortcuts(Path.Combine(Folder, VerifyEntry + ".lnk"), setup, "verify --ui", setup + ",0", AppContext.BaseDirectory, "Verify the setup and create a verification report");

        var manual = Path.Combine(AppContext.BaseDirectory, ManualFile);
        if (File.Exists(manual))
        {
            shortcuts(Path.Combine(Folder, ManualEntry + ".lnk"), manual, string.Empty, string.Empty, AppContext.BaseDirectory, "Manual for installers and service technicians");
        }
        return 0;
    }
}
