namespace AppStart.TestApp;

/// <summary>
/// All user-visible texts of the test app in one place (German UI, see openspec/project.md).
/// The manual completeness test (task 10.4) reads messages from here.
/// </summary>
public static class Texts
{
    public const string WindowTitle = "AppStart TestApp";
    public const string Heading = "Zustand der Testapplikation";
    public const string SubHeading = "Prüft, ob der Start über AppStart wie vorgesehen funktioniert.";

    public const string RowAccount = "Laufendes Konto";
    public const string RowAdmin = "Admin-Rechte";
    public const string RowRegistry = "Registry lesen/schreiben";
    public const string RowFiles = "Dateien lesen/schreiben";
    public const string RowChild = "Kindprozess (cmd.exe)";

    public const string Yes = "Ja";
    public const string No = "Nein";

    public const string BadgeOk = "OK";
    public const string BadgeError = "Fehler";
    public const string BadgePending = "Offen";
    public const string BadgeCheck = "Prüfen";
    public const string BadgeBreakout = "Ausbruch";
    public const string BadgeNotViaAppStart = "Nicht via AppStart";

    public const string ChildNotTested = "Noch nicht geprüft";
    public const string ChildBlocked = "Start blockiert durch Job-Objekt";
    public const string ChildStarted = "Gestartet – Ausbruch möglich";
    public const string ChildFailed = "Start fehlgeschlagen: {0}";

    public const string TestValueLabel = "Testwert";
    public const string DefaultTestValue = "Messung 42 – 12.3456 g";
    public const string SaveButton = "In Registry und Datei speichern";
    public const string CommandPromptButton = "Eingabeaufforderung starten";
    public const string RecheckButton = "Erneut prüfen";

    public const string CheckedAt = "Geprüft um {0:HH:mm:ss}";
    public const string SavedAt = "Gespeichert um {0:HH:mm:ss}";
    public const string SaveFailed = "Speichern fehlgeschlagen: {0}";

    public const string NoAdminTitle = "AppStart TestApp";
    public const string NoAdminMessage =
        "Die Testapplikation benötigt Administratorrechte und wird beendet.\n\n" +
        "Bitte über die Verknüpfung von AppStart starten.";
}
