namespace AppStart.Setup.Engine;

/// <summary>Result and progress texts of the setup engine (wizard step 3 and command line). Checked by task 10.4.</summary>
public static class SetupEngineTexts
{
    public const string ProgressAccounts = "Konten und Gruppe werden eingerichtet …";
    public const string ProgressConfig = "Konfiguration wird geschrieben …";
    public const string ProgressProtection = "Datenordner und Registry-Keys werden gesperrt …";
    public const string ProgressShortcuts = "Verknüpfungen werden angelegt …";
    public const string ProgressService = "Dienst wird geprüft …";

    public const string AccountsWithOperator = "Konten appstart-app und appstart-user, Gruppe AppStart Users angelegt";
    public const string AccountsOnly = "Konto appstart-app und Gruppe AppStart Users angelegt";
    public const string AccountsFailed = "Konten und Gruppe konnten nicht eingerichtet werden";

    public const string AppsReleased = "{0} Applikationen freigegeben, Konfiguration geschützt";
    public const string ConfigFailed = "Konfiguration konnte nicht geschrieben werden";
    public const string ConfigUnreadable = "apps.json ist ungültig: {0}";
    public const string UnknownApp = "Applikation «{0}» ist nicht freigegeben";

    public const string DataProtected = "Datenordner und Registry-Keys gesperrt";
    public const string DataProtectionFailed = "Datenordner oder Registry-Keys konnten nicht gesperrt werden";

    public const string ShortcutsCreated = "Verknüpfungen mit App-Icon angelegt, {0} alte Verknüpfungen gesichert";
    public const string ShortcutsFailed = "Verknüpfungen konnten nicht angelegt werden";

    public const string ServiceRunning = "Dienst AppStart läuft, Start automatisch";
    public const string ServiceFailed = "Dienst AppStart läuft nicht";

    public const string UninstallShortcuts = "Verknüpfungen zurückgestellt, Datenordner und Registry-Keys zurückgesetzt";
    public const string UninstallAccounts = "Konten und Gruppe entfernt";
    public const string UninstallConfig = "Konfiguration entfernt (Prüfberichte bleiben erhalten)";
}
