using Microsoft.Extensions.Logging;

namespace AppStart.Service;

/// <summary>
/// Event log ids of the service (source "AppStart", log "Application"). Every id must be described
/// in the manual, chapter "Meldungen und Fehlerbehebung" (task 10.4 checks this list).
/// </summary>
public static class EventIds
{
    public static readonly EventId ServiceStarted = new(1000, nameof(ServiceStarted));
    public static readonly EventId ServiceStopped = new(1001, nameof(ServiceStopped));

    public static readonly EventId LaunchSucceeded = new(1100, nameof(LaunchSucceeded));

    public static readonly EventId DeniedUnknownApp = new(1200, nameof(DeniedUnknownApp));
    public static readonly EventId DeniedIntegrity = new(1201, nameof(DeniedIntegrity));
    public static readonly EventId DeniedConfig = new(1202, nameof(DeniedConfig));
    public static readonly EventId DeniedBusy = new(1203, nameof(DeniedBusy));
    public static readonly EventId InvalidRequest = new(1204, nameof(InvalidRequest));
    public static readonly EventId LaunchFailed = new(1205, nameof(LaunchFailed));

    public static readonly EventId PipeAlreadyExists = new(1300, nameof(PipeAlreadyExists));
    public static readonly EventId PipeSetupFailed = new(1301, nameof(PipeSetupFailed));
    public static readonly EventId PipeError = new(1302, nameof(PipeError));

    public static IReadOnlyList<EventId> All { get; } =
    [
        ServiceStarted, ServiceStopped, LaunchSucceeded,
        DeniedUnknownApp, DeniedIntegrity, DeniedConfig, DeniedBusy, InvalidRequest, LaunchFailed,
        PipeAlreadyExists, PipeSetupFailed, PipeError,
    ];
}
