using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Velopack;

namespace AppStart.Spike.Velopack;

internal static class Program
{
    private const string ServiceName = "AppStartVeloSpike";
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppStartVeloSpike", "hooks.log");
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);

    private static int Main(string[] args)
    {
        Log($"START args=[{string.Join(' ', args)}]");

        VelopackApp.Build()
            .OnAfterInstallFastCallback(v => { Log($"HOOK install {v}"); InstallService(); })
            .OnBeforeUpdateFastCallback(v => { Log($"HOOK obsolete {v}"); Sc($"stop {ServiceName}"); WaitStopped(); })
            .OnAfterUpdateFastCallback(v => { Log($"HOOK updated {v}"); InstallService(); })
            .OnBeforeUninstallFastCallback(v => { Log($"HOOK uninstall {v}"); Sc($"stop {ServiceName}"); WaitStopped(); Sc($"delete {ServiceName}"); })
            .OnFirstRun(v => Log($"HOOK firstrun {v}"))
            .Run();

        if (args.Contains("--service"))
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddWindowsService(o => o.ServiceName = ServiceName);
            builder.Services.AddHostedService<Heartbeat>();
            builder.Build().Run();
            return 0;
        }

        Log("normal start, nothing to do");
        return 0;
    }

    // Creates or re-points the service to the current EXE and starts it.
    private static void InstallService()
    {
        var bin = $"\"{Environment.ProcessPath}\" --service";
        var exists = Sc($"query {ServiceName}") == 0;
        Sc(exists
            ? $"config {ServiceName} binPath= \"{bin.Replace("\"", "\\\"")}\" start= auto"
            : $"create {ServiceName} binPath= \"{bin.Replace("\"", "\\\"")}\" start= auto obj= LocalSystem");
        Sc($"failure {ServiceName} reset= 86400 actions= restart/5000");
        Sc($"start {ServiceName}");
    }

    private static void WaitStopped()
    {
        for (var i = 0; i < 20; i++)
        {
            var p = Process.Start(new ProcessStartInfo("sc.exe", $"query {ServiceName}") { RedirectStandardOutput = true, CreateNoWindow = true })!;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
            if (!o.Contains("RUNNING") && !o.Contains("STOP_PENDING")) { Log("service stopped"); return; }
            Thread.Sleep(250);
        }
        Log("service did not stop within 5 s");
    }

    private static int Sc(string arguments)
    {
        var psi = new ProcessStartInfo("sc.exe", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Replace("\r", "").Replace("\n", " | ").Trim();
        p.WaitForExit();
        Log($"sc {arguments} -> {p.ExitCode} {output}");
        return p.ExitCode;
    }

    internal static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            using var id = WindowsIdentity.GetCurrent();
            var elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            File.AppendAllText(LogFile,
                $"{DateTime.Now:HH:mm:ss.fff} v{Version} pid {Environment.ProcessId} [{id.Name}, admin {elevated}] {Environment.ProcessPath} :: {msg}{Environment.NewLine}");
        }
        catch { /* spike: never fail on logging */ }
    }

    private sealed class Heartbeat : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            Log("SERVICE running");
            try { while (!ct.IsCancellationRequested) await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { }
            Log("SERVICE stopping");
        }
    }
}
