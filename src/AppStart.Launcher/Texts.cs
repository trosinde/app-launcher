namespace AppStart.Launcher;

/// <summary>
/// All texts the launcher shows (English, UI design "Launcher messages"). The manual completeness
/// test (task 10.4) checks that each message is described in the chapter on messages and troubleshooting.
/// </summary>
public static class Texts
{
    public const string DefaultTitle = "AppStart";

    public const string UnreachableHeading = "Launch service not reachable";
    public const string UnreachableText = "The AppStart service is not running. Please contact your administrator or service support.";

    public const string NoPermissionHeading = "No permission";
    public const string NoPermissionText = "Your user account may not start applications through AppStart. Please contact your administrator.";

    public const string InvalidShortcutHeading = "Invalid shortcut";
    public const string InvalidShortcutText = "This shortcut is not set up correctly. Please contact service support.";

    public const string DeniedHeading = "Launch denied";
    public const string DeniedIntegrityText = "The application has been modified and may not be started. The incident has been logged. Please contact service support.";
    public const string DeniedUnknownAppText = "This application is not approved for starting through AppStart. The incident has been logged. Please contact service support.";
    public const string DeniedConfigText = "The AppStart setup is not in order. The start was denied and logged. Please contact service support.";
    public const string DeniedErrorText = "The application could not be started. The incident has been logged. Please contact service support.";

    public const string BusyHeading = "Start already in progress";
    public const string BusyText = "The application is being started. Please wait a moment and then try again.";

    public const string ProtocolHeading = "Unexpected response";
    public const string ProtocolText = "The AppStart service responded unexpectedly. Please contact service support.";

    /// <summary>Footer: "Event ID 4711 · 15.11.2026 10:51".</summary>
    public const string IncidentFooter = "Event ID {0} · {1:dd.MM.yyyy HH:mm}";
}
