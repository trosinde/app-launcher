using System.Diagnostics;
using AppStart.Core;

namespace AppStart.Service.Installation;

/// <summary>
/// Registers and removes the Windows service from the Velopack hooks (task 7.4, design D5). Runs with the
/// installer's admin rights, without UI and within Velopack's 30 s limit. Touches only the service and the
/// event source and AppStart's own Start menu folder; apps.json, accounts, app shortcuts and ACLs belong to
/// "AppStart.Setup.exe uninstall".
/// </summary>
internal static class ServiceInstaller
{
    private const string DisplayName = "AppStart";
    private const string Description = "Starts approved applications for operators with admin rights (AppStart).";

    /// <summary>Install hook (also after an MSI upgrade): create or re-point the service, then start it.</summary>
    public static void InstallAndStart()
    {
        var bin = $"\"{Environment.ProcessPath}\"";
        var exists = Sc($"query {AppStartConstants.ServiceName}") == 0;
        Sc(exists
            ? $"config {AppStartConstants.ServiceName} binPath= \"{Escape(bin)}\" start= auto obj= LocalSystem"
            : $"create {AppStartConstants.ServiceName} binPath= \"{Escape(bin)}\" start= auto obj= LocalSystem DisplayName= \"{DisplayName}\"");
        Sc($"description {AppStartConstants.ServiceName} \"{Description}\"");
        Sc($"failure {AppStartConstants.ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000");

        if (!EventLog.SourceExists(AppStartConstants.EventSource))
        {
            EventLog.CreateEventSource(AppStartConstants.EventSource, "Application");
        }

        Sc($"start {AppStartConstants.ServiceName}");
        RunSetup("startmenu --create", TimeSpan.FromSeconds(10));

        // Task 9.3: automatic check after every installation and update; report in ProgramData\AppStart\Reports.
        RunSetup("verify", TimeSpan.FromSeconds(15));
    }

    /// <summary>Uninstall hook (also before an MSI upgrade): stop and delete the service only.</summary>
    public static void StopAndDelete()
    {
        StopAndWait();
        Sc($"delete {AppStartConstants.ServiceName}");
        RunSetup("startmenu --remove", TimeSpan.FromSeconds(10));
    }

    // Start menu entries and the automatic check are done by the setup EXE next to this one, so that code
    // exists only once. Bounded waits: the whole hook has 30 s (10 s + 15 s + service start).
    private static void RunSetup(string arguments, TimeSpan timeout)
    {
        var setup = Path.Combine(AppContext.BaseDirectory, "AppStart.Setup.exe");
        if (!File.Exists(setup))
        {
            return;
        }

        using var p = Process.Start(new ProcessStartInfo(setup, arguments) { CreateNoWindow = true, UseShellExecute = false });
        if (p is not null && !p.WaitForExit(timeout))
        {
            p.Kill();
        }
    }

    /// <summary>Before an update through Update.exe: the files of "current" are replaced, the service must not run.</summary>
    public static void StopAndWait()
    {
        Sc($"stop {AppStartConstants.ServiceName}");
        for (var i = 0; i < 40; i++)
        {
            if (!QueryOutput().Contains("RUNNING", StringComparison.Ordinal) && !QueryOutput().Contains("PENDING", StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(250);
        }
    }

    private static string QueryOutput()
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", $"query {AppStartConstants.ServiceName}")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private static int Sc(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string Escape(string quoted) => quoted.Replace("\"", "\\\"", StringComparison.Ordinal);
}
