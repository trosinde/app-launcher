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
    public const string PipeMissing = "Die Pipe des Dienstes ist nicht erreichbar. Ereignisprotokoll (Quelle AppStart) prüfen.";

    // add-allowed-child-processes: helper programs (design D4)
    public const string ModeSet = "Weitere Programme für «{0}»: {1}";
    public const string ModeBlocked = "gesperrt";
    public const string ModeAllowlist = "nur erlaubte Hilfsprogramme";
    public const string ModeAll = "alle erlaubt (unsicher)";
    public const string AllowlistNeedsEntries = "«nur erlaubte Hilfsprogramme» braucht mindestens ein Hilfsprogramm. Zuerst allow-child ausführen.";
    public const string ChildAllowed = "Hilfsprogramm {0} für «{1}» erlaubt ({2})";
    public const string ChildDenied = "Hilfsprogramm {0} für «{1}» entfernt";
    public const string ChildUnknown = "Hilfsprogramm {0} steht nicht in der Liste von «{1}»";
    public const string LastChildRemoved = "Letztes Hilfsprogramm entfernt: weitere Programme für «{0}» wieder gesperrt";
    public const string ChildNotExe = "«{0}» ist kein Pfad auf eine EXE.";
    public const string ChildMissing = "{0} existiert nicht.";
    public const string ChildIsShell = "{0} ist eine Shell oder ein Skript-Host. Nur mit ausdrücklicher Bestätigung (--allow-shell) erlaubt.";
    public const string ChildNotSigned = "{0} ist nicht gültig signiert. Prüfung «signature» nicht möglich, «sha256» verwenden.";
    public const string ChildFolderWritable = "Hilfsprogramm nicht erlaubt: {0} liegt in einem Ordner, den Bediener beschreiben können";
    public const string ChildrenRehashed = "Prüfsummen der Hilfsprogramme von «{0}» neu erfasst ({1})";

    public const string IdInvalid = "AppId «{0}» ist ungültig. Erlaubt sind a-z, 0-9 und -, 1 bis {1} Zeichen.";
    public const string IdFixed = "Die Applikation ist bereits als «{0}» freigegeben; die AppId lässt sich nicht ändern (remove, dann add).";
    public const string IdTaken = "AppId «{0}» ist bereits vergeben.";

    // Learn mode (design D3)
    public const string LearnStarted = "Lernmodus für «{0}» gestartet, endet automatisch um {1} Uhr";
    public const string LearnStopped = "Lernmodus für «{0}» beendet";
    public const string LearnNotActive = "Lernmodus für «{0}» war nicht aktiv";
    public const string LearnMinutesInvalid = "Dauer des Lernmodus: {0} bis {1} Minuten.";
    public const string LearnNothingRecorded = "Für «{0}» wurde nichts aufgezeichnet. Zuerst learn start und die Applikation benutzen.";
    public const string LearnNotRecorded = "{0} steht nicht in der Aufzeichnung.";
    public const string LearnShellNotSelectable = "{0} ist eine Shell oder ein Skript-Host und wird nie übernommen. Falls nötig von Hand: allow-child --allow-shell.";
    public const string LearnNothingToApply = "Für «{0}» gibt es keine übernehmbaren Vorschläge (nur Shells oder Skript-Hosts aufgezeichnet).";
    public const string LearnApplied = "{0} Hilfsprogramm(e) aus dem Lernmodus für «{1}» übernommen, Aufzeichnung gelöscht";

    public const string UninstallShortcuts = "Verknüpfungen zurückgestellt, Datenordner und Registry-Keys zurückgesetzt";
    public const string UninstallAccounts = "Konten und Gruppe entfernt";
    public const string UninstallConfig = "Konfiguration entfernt (Prüfberichte bleiben erhalten)";
}
