using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace AppStart.TestApp.Services;

/// <summary>Result of a single check shown in the status list.</summary>
public sealed record CheckResult(bool Success, string Detail);

/// <summary>Outcome of the attempt to start cmd.exe.</summary>
public enum ChildProcessOutcome
{
    /// <summary>cmd.exe was started: an operator could break out with admin rights.</summary>
    Started,

    /// <summary>The job object refused the process (ERROR_NOT_ENOUGH_QUOTA).</summary>
    BlockedByJob,

    /// <summary>Start failed for another reason.</summary>
    Failed,

    /// <summary>AppStart terminated the process at once (mode allowlist, not on the list): exit code 0xC0DE or access denied at start.</summary>
    TerminatedByService,

    /// <summary>The program ran and ended normally (helper allowed).</summary>
    Ran,
}

/// <summary>Abstraction of everything the test app touches on the machine, so the view model stays testable.</summary>
public interface ITestEnvironment
{
    string AccountName { get; }

    bool IsElevatedAdministrator { get; }

    string RegistryPath { get; }

    string DataDirectory { get; }

    string? ReadStoredTestValue();

    CheckResult CheckRegistry();

    CheckResult CheckFiles();

    void Save(string testValue);

    (ChildProcessOutcome Outcome, string Detail) TryStartCommandPrompt();

    /// <summary>Starts AppStart.TestHelper.exe next to the TestApp and waits for its end.</summary>
    (ChildProcessOutcome Outcome, string Detail) TryStartHelper();
}

/// <summary>
/// Real implementation. Mimics an affected fat client: stores values including a clear-text
/// password in HKLM\SOFTWARE\AppStartTest and exchange files in C:\ProgramData\AppStartTest.
/// </summary>
public sealed class TestEnvironment : ITestEnvironment
{
    public const string RegistrySubKey = @"SOFTWARE\AppStartTest";

    /// <summary>Deliberately stored in clear text, like the affected applications do. Test value only.</summary>
    public const string TestPassword = "AppStartTest-Passwort-0815";

    private const int ErrorNotEnoughQuota = 1816;
    private const int ErrorAccessDenied = 5;

    // Terminated while CreateProcess was still setting the process up (seen on the test VM).
    private const int ErrorPartialCopy = 299;

    private static bool TerminatedDuringStart(Win32Exception ex) =>
        ex.NativeErrorCode is ErrorAccessDenied or ErrorPartialCopy;

    /// <summary>Exit code the AppStart service uses when it terminates a process that is not allowed (JobMonitor).</summary>
    private const int TerminatedByAppStart = 0xC0DE;

    public const string HelperFileName = "AppStart.TestHelper.exe";

    public string AccountName { get; } = WindowsIdentity.GetCurrent().Name;

    public bool IsElevatedAdministrator { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public string RegistryPath => $@"HKLM\{RegistrySubKey}";

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppStartTest");

    private string ExchangeFile => Path.Combine(DataDirectory, "exchange.txt");

    public string? ReadStoredTestValue()
    {
        try
        {
            using var key = OpenKey(writable: false);
            return key?.GetValue("TestValue") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    public CheckResult CheckRegistry()
    {
        try
        {
            var probe = Guid.NewGuid().ToString("N");
            using (var key = OpenKey(writable: true)!)
            {
                key.SetValue("Probe", probe);
                key.SetValue("Password", TestPassword);
            }

            using var read = OpenKey(writable: false)!;
            var ok = Equals(read.GetValue("Probe"), probe) && Equals(read.GetValue("Password"), TestPassword);
            return new CheckResult(ok, RegistryPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return new CheckResult(false, $"{RegistryPath}: {ex.Message}");
        }
    }

    public CheckResult CheckFiles()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var probe = Guid.NewGuid().ToString("N");
            var file = Path.Combine(DataDirectory, "probe.txt");
            File.WriteAllText(file, probe);
            var ok = File.ReadAllText(file) == probe;
            return new CheckResult(ok, DataDirectory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new CheckResult(false, $"{DataDirectory}: {ex.Message}");
        }
    }

    public void Save(string testValue)
    {
        var timestamp = DateTimeOffset.Now.ToString("O");
        using (var key = OpenKey(writable: true)!)
        {
            key.SetValue("TestValue", testValue);
            key.SetValue("Password", TestPassword);
            key.SetValue("LastWrite", timestamp);
            key.SetValue("LastWriter", AccountName);
        }

        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(ExchangeFile,
            $"TestValue={testValue}{Environment.NewLine}Timestamp={timestamp}{Environment.NewLine}Writer={AccountName}{Environment.NewLine}");
    }

    public (ChildProcessOutcome Outcome, string Detail) TryStartCommandPrompt()
    {
        try
        {
            // Visible console on purpose: this is exactly the breakout an operator could attempt.
            using var process = Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = false })!;
            // In mode allowlist the service terminates it within milliseconds (Spike 1.1).
            if (process.WaitForExit(1500) && process.ExitCode == TerminatedByAppStart)
            {
                return (ChildProcessOutcome.TerminatedByService, $"cmd.exe beendet (Exit-Code 0x{process.ExitCode:X})");
            }

            return (ChildProcessOutcome.Started, $"cmd.exe gestartet (PID {process.Id})");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNotEnoughQuota)
        {
            return (ChildProcessOutcome.BlockedByJob, ex.Message);
        }
        catch (Win32Exception ex) when (TerminatedDuringStart(ex))
        {
            return (ChildProcessOutcome.TerminatedByService, ex.Message); // terminated before CreateProcess returned
        }
        catch (Win32Exception ex)
        {
            return (ChildProcessOutcome.Failed, ex.Message);
        }
    }

    public (ChildProcessOutcome Outcome, string Detail) TryStartHelper()
    {
        var helper = Path.Combine(AppContext.BaseDirectory, HelperFileName);
        if (!File.Exists(helper))
        {
            return (ChildProcessOutcome.Failed, $"{helper} fehlt");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, // the helper writes UTF-8
            })!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(10_000))
            {
                return (ChildProcessOutcome.Failed, "Keine Antwort nach 10 s");
            }

            return process.ExitCode switch
            {
                0 => (ChildProcessOutcome.Ran, output),
                TerminatedByAppStart => (ChildProcessOutcome.TerminatedByService, $"Exit-Code 0x{process.ExitCode:X}"),
                _ => (ChildProcessOutcome.Failed, $"Exit-Code {process.ExitCode}"),
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNotEnoughQuota)
        {
            return (ChildProcessOutcome.BlockedByJob, ex.Message);
        }
        catch (Win32Exception ex) when (TerminatedDuringStart(ex))
        {
            return (ChildProcessOutcome.TerminatedByService, ex.Message);
        }
        catch (Win32Exception ex)
        {
            return (ChildProcessOutcome.Failed, ex.Message);
        }
    }

    private static RegistryKey? OpenKey(bool writable)
    {
        var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return writable ? hklm.CreateSubKey(RegistrySubKey, writable: true) : hklm.OpenSubKey(RegistrySubKey, writable: false);
    }
}
