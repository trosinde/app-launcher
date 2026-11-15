namespace AppStart.Launcher;

/// <summary>
/// All texts the launcher shows (German, UI design "Launcher-Meldungen"). The manual completeness
/// test (task 10.4) checks that each message is described in chapter "Meldungen und Fehlerbehebung".
/// </summary>
public static class Texts
{
    public const string DefaultTitle = "AppStart";

    public const string UnreachableHeading = "Start-Dienst nicht erreichbar";
    public const string UnreachableText = "Der Dienst AppStart läuft nicht. Bitte wenden Sie sich an Ihren Administrator oder den Service.";

    public const string NoPermissionHeading = "Keine Berechtigung";
    public const string NoPermissionText = "Ihr Benutzerkonto darf Applikationen nicht über AppStart starten. Bitte wenden Sie sich an Ihren Administrator.";

    public const string InvalidShortcutHeading = "Ungültige Verknüpfung";
    public const string InvalidShortcutText = "Diese Verknüpfung ist nicht korrekt eingerichtet. Bitte wenden Sie sich an den Service.";

    public const string DeniedHeading = "Start verweigert";
    public const string DeniedIntegrityText = "Die Applikation wurde verändert und darf nicht gestartet werden. Der Vorfall wurde protokolliert. Bitte wenden Sie sich an den Service.";
    public const string DeniedUnknownAppText = "Diese Applikation ist für den Start über AppStart nicht freigegeben. Der Vorfall wurde protokolliert. Bitte wenden Sie sich an den Service.";
    public const string DeniedConfigText = "Die Einrichtung von AppStart ist nicht in Ordnung. Der Start wurde verweigert und protokolliert. Bitte wenden Sie sich an den Service.";
    public const string DeniedErrorText = "Die Applikation konnte nicht gestartet werden. Der Vorfall wurde protokolliert. Bitte wenden Sie sich an den Service.";

    public const string BusyHeading = "Start läuft bereits";
    public const string BusyText = "Die Applikation wird gerade gestartet. Bitte warten Sie einen Moment und versuchen Sie es dann erneut.";

    public const string ProtocolHeading = "Unerwartete Antwort";
    public const string ProtocolText = "Der Dienst AppStart hat unerwartet geantwortet. Bitte wenden Sie sich an den Service.";

    /// <summary>Footer: «Ereignis-ID 4711 · 15.11.2026 10:51».</summary>
    public const string IncidentFooter = "Ereignis-ID {0} · {1:dd.MM.yyyy HH:mm}";
}
