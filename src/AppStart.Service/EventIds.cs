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

    // add-allowed-child-processes (design D2/D3)
    public static readonly EventId ChildAllowed = new(1101, nameof(ChildAllowed));
    public static readonly EventId ChildRecorded = new(1102, nameof(ChildRecorded));
    public static readonly EventId ChildTerminated = new(1206, nameof(ChildTerminated));
    public static readonly EventId LearnStateIgnored = new(1207, nameof(LearnStateIgnored));
    public static readonly EventId JobMonitorFailed = new(1208, nameof(JobMonitorFailed));
    public static readonly EventId LearnModeStarted = new(1400, nameof(LearnModeStarted));
    public static readonly EventId LearnModeEnded = new(1401, nameof(LearnModeEnded));

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
        ServiceStarted, ServiceStopped, LaunchSucceeded, ChildAllowed, ChildRecorded,
        DeniedUnknownApp, DeniedIntegrity, DeniedConfig, DeniedBusy, InvalidRequest, LaunchFailed,
        ChildTerminated, LearnStateIgnored, JobMonitorFailed,
        PipeAlreadyExists, PipeSetupFailed, PipeError,
        LearnModeStarted, LearnModeEnded,
    ];
}
