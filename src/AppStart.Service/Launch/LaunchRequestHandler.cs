using AppStart.Core.Configuration;
using AppStart.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace AppStart.Service.Launch;

/// <summary>Who sent the request (from the pipe, never from the message).</summary>
public sealed record RequestContext(uint SessionId, string User);

public enum StartOutcome
{
    Started,
    IntegrityFailed,
    Failed,
}

/// <summary>Result of verifying and starting one application. Detail is for the event log only.</summary>
public sealed record StartResult(StartOutcome Outcome, string Detail, int ProcessId = 0);

/// <summary>Opens, verifies and starts an application in a session (Win32 part, see <see cref="SessionLauncher"/>).</summary>
public interface IAppStarter
{
    StartResult Start(AppEntry app, uint sessionId);
}

/// <summary>Source of apps.json; read fresh on every request.</summary>
public interface IConfigSource
{
    ConfigLoadResult Load();
}

/// <summary>apps.json from C:\ProgramData\AppStart with the ACL check of <see cref="ConfigStore"/>.</summary>
public sealed class FileConfigSource : IConfigSource
{
    public ConfigLoadResult Load() => ConfigStore.Load();
}

/// <summary>
/// Handles one pipe message: parse, throttle per session, load config, find app, verify and start.
/// Every request is written to the event log with time, user, session, AppId and result (concept).
/// The answer to the client carries only a reason code and an incident id (spec pipe-security).
/// </summary>
public sealed class LaunchRequestHandler(
    IConfigSource config,
    IAppStarter starter,
    SessionGate gate,
    ILogger<LaunchRequestHandler> log,
    Func<long>? incidentIds = null)
{
    private readonly Func<long> _nextIncidentId = incidentIds ?? (() => Random.Shared.NextInt64(1_000_000_000, PipeProtocol.MaxIncidentId + 1));

    /// <summary>Returns the response bytes, or null when the request is malformed and must be dropped without answer.</summary>
    public byte[]? Handle(ReadOnlySpan<byte> message, RequestContext ctx)
    {
        if (!PipeProtocol.TryParseRequest(message, out var appId))
        {
            log.LogWarning(EventIds.InvalidRequest,
                "Ungültige Anfrage verworfen. Benutzer: {User}, Session: {Session}, Länge: {Length} Byte.",
                ctx.User, ctx.SessionId, message.Length);
            return null;
        }

        using var lease = gate.TryEnter(ctx.SessionId);
        if (lease is null)
        {
            return Deny(DenyReason.Busy, EventIds.DeniedBusy, ctx, appId, "Session hat bereits eine Anfrage in Arbeit oder ist in der Sperrzeit.");
        }

        var loaded = config.Load();
        if (!loaded.IsValid)
        {
            return Deny(DenyReason.Config, EventIds.DeniedConfig, ctx, appId, loaded.Error!);
        }

        var app = loaded.Config!.Find(appId);
        if (app is null)
        {
            return Deny(DenyReason.UnknownApp, EventIds.DeniedUnknownApp, ctx, appId, "AppId ist nicht in apps.json freigegeben.");
        }

        StartResult result;
        try
        {
            result = starter.Start(app, ctx.SessionId);
        }
        catch (Exception ex)
        {
            // Last line of defence: an unexpected fault must still end in a logged, generic refusal.
            result = new StartResult(StartOutcome.Failed, ex.ToString());
        }

        switch (result.Outcome)
        {
            case StartOutcome.Started:
                log.LogInformation(EventIds.LaunchSucceeded,
                    "Start ausgeführt. Benutzer: {User}, Session: {Session}, AppId: {AppId}, EXE: {Exe}, Prozess: {ProcessId}. {Detail}",
                    ctx.User, ctx.SessionId, appId, app.Exe, result.ProcessId, result.Detail);
                return PipeProtocol.FormatOk();
            case StartOutcome.IntegrityFailed:
                return Deny(DenyReason.Integrity, EventIds.DeniedIntegrity, ctx, appId, $"{app.Exe}: {result.Detail}");
            default:
                return Deny(DenyReason.Error, EventIds.LaunchFailed, ctx, appId, $"{app.Exe}: {result.Detail}");
        }
    }

    private byte[] Deny(DenyReason reason, EventId eventId, RequestContext ctx, string appId, string detail)
    {
        var incident = _nextIncidentId();
        log.LogWarning(eventId,
            "Start verweigert ({Reason}). Vorgang: {Incident}, Benutzer: {User}, Session: {Session}, AppId: {AppId}. {Detail}",
            PipeProtocol.ToWire(reason), incident, ctx.User, ctx.SessionId, appId, detail);
        return PipeProtocol.FormatDenied(reason, incident);
    }
}
