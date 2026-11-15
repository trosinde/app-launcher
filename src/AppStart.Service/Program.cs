using AppStart.Core;
using AppStart.Service.Installation;
using AppStart.Service.Launch;
using AppStart.Service.Pipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Velopack;

// Velopack hooks (task 7.4, design D5): only the service is installed or removed here, no UI.
// Must run first; for a hook invocation Velopack calls the callback and exits the process.
VelopackApp.Build()
    .OnAfterInstallFastCallback(_ => ServiceInstaller.InstallAndStart())
    .OnAfterUpdateFastCallback(_ => ServiceInstaller.InstallAndStart())
    .OnBeforeUpdateFastCallback(_ => ServiceInstaller.StopAndWait())
    .OnBeforeUninstallFastCallback(_ => ServiceInstaller.StopAndDelete())
    .Run();

// Helper mode: started by the service itself as SYSTEM inside an operator session (design D2).
if (args is [DesktopAccess.HelperArgument])
{
    return DesktopAccess.GrantForAppAccountInCurrentSession();
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = AppStartConstants.ServiceName);

builder.Logging.ClearProviders();
builder.Logging.AddEventLog(o =>
{
    o.SourceName = AppStartConstants.EventSource;
    o.LogName = "Application";
});
builder.Logging.AddFilter<EventLogLoggerProvider>("AppStart", LogLevel.Information);
builder.Logging.AddFilter<EventLogLoggerProvider>("Microsoft", LogLevel.Warning);
if (!WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.AddSimpleConsole(); // only when started by hand on the test VM
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new SessionGate(sp.GetRequiredService<TimeProvider>(), SessionGate.DefaultCooldown));
builder.Services.AddSingleton<IConfigSource, FileConfigSource>();
builder.Services.AddSingleton<IChildProcessInspector, Win32ChildProcessInspector>();
builder.Services.AddSingleton<ILearnRecorder>(_ => new FileLearnRecorder());
builder.Services.AddSingleton<ILearnSource, FileLearnSource>();
builder.Services.AddSingleton<IAppStarter, VerifyingAppStarter>();
builder.Services.AddSingleton(sp => new LaunchRequestHandler(
    sp.GetRequiredService<IConfigSource>(),
    sp.GetRequiredService<IAppStarter>(),
    sp.GetRequiredService<SessionGate>(),
    sp.GetRequiredService<ILogger<LaunchRequestHandler>>(),
    incidentIds: null,
    sp.GetRequiredService<ILearnSource>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
await host.RunAsync();
return Environment.ExitCode;
