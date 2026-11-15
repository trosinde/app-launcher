using AppStart.Core;
using AppStart.Core.Protocol;

namespace AppStart.Launcher;

public enum MessageIcon
{
    Information,
    Warning,
    Error,
}

/// <summary>What the dialog shows (title = shortcut name).</summary>
public sealed record LauncherMessage(MessageIcon Icon, string Heading, string Text, string? Footer = null);

/// <summary>Result of talking to the service.</summary>
public enum ClientStatus
{
    Answered,
    Unreachable,
    AccessDenied,
    ProtocolError,
}

public sealed record ClientResult(ClientStatus Status, LaunchResponse? Response = null);

/// <summary>Pure launcher logic, unit tested; Program.cs only wires it to the pipe and the dialog.</summary>
public static class LauncherLogic
{
    public const int ExitOk = 0;
    public const int ExitInvalidShortcut = 1;
    public const int ExitUnreachable = 2;
    public const int ExitDenied = 3;
    public const int ExitNoPermission = 4;
    public const int ExitProtocol = 5;

    /// <summary>Accepts exactly "--app &lt;AppId&gt;" with a valid AppId; anything else is rejected.</summary>
    public static string? ParseAppId(IReadOnlyList<string> args) =>
        args is ["--app", var id] && AppId.IsValid(id) ? id : null;

    /// <summary>Message and exit code for an outcome; message null = success, show nothing.</summary>
    public static (LauncherMessage? Message, int ExitCode) Evaluate(ClientResult result, DateTimeOffset now) => result.Status switch
    {
        ClientStatus.Unreachable => (new(MessageIcon.Warning, Texts.UnreachableHeading, Texts.UnreachableText), ExitUnreachable),
        ClientStatus.AccessDenied => (new(MessageIcon.Error, Texts.NoPermissionHeading, Texts.NoPermissionText), ExitNoPermission),
        ClientStatus.ProtocolError => (new(MessageIcon.Error, Texts.ProtocolHeading, Texts.ProtocolText), ExitProtocol),
        _ when result.Response is { Success: true } => (null, ExitOk),
        _ when result.Response is { Reason: DenyReason.Busy } => (new(MessageIcon.Information, Texts.BusyHeading, Texts.BusyText), ExitDenied),
        _ when result.Response is { } denied => (new(MessageIcon.Error, Texts.DeniedHeading, DeniedText(denied.Reason),
            string.Format(System.Globalization.CultureInfo.InvariantCulture, Texts.IncidentFooter, denied.IncidentId, now.LocalDateTime)), ExitDenied),
        _ => (new(MessageIcon.Error, Texts.ProtocolHeading, Texts.ProtocolText), ExitProtocol),
    };

    public static LauncherMessage InvalidShortcut { get; } = new(MessageIcon.Error, Texts.InvalidShortcutHeading, Texts.InvalidShortcutText);

    /// <summary>Window title: name of the shortcut that started the launcher, else "AppStart".</summary>
    public static string TitleFromShortcut(string? linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath) || !linkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return Texts.DefaultTitle;
        }

        var name = Path.GetFileNameWithoutExtension(linkPath);
        return string.IsNullOrWhiteSpace(name) ? Texts.DefaultTitle : name;
    }

    private static string DeniedText(DenyReason? reason) => reason switch
    {
        DenyReason.Integrity => Texts.DeniedIntegrityText,
        DenyReason.UnknownApp => Texts.DeniedUnknownAppText,
        DenyReason.Config => Texts.DeniedConfigText,
        _ => Texts.DeniedErrorText,
    };
}
