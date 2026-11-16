namespace AppStart.Core;

/// <summary>Constants shared by service, launcher and setup.</summary>
public static class AppStartConstants
{
    /// <summary>Name of the named pipe (\\.\pipe\AppStart).</summary>
    public const string PipeName = "AppStart";

    /// <summary>Windows event log source.</summary>
    public const string EventSource = "AppStart";

    public const string ServiceName = "AppStart";

    public const string AppAccountName = "appstart-app";

    public const string OperatorAccountName = "appstart-user";

    public const string OperatorGroupName = "AppStart Users";

    /// <summary>C:\ProgramData\AppStart</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppStart");

    /// <summary>C:\ProgramData\AppStart\apps.json</summary>
    public static string ConfigPath { get; } = Path.Combine(DataDirectory, "apps.json");

    /// <summary>C:\ProgramData\AppStart\learn.json (learn mode, add-allowed-child-processes D3)</summary>
    public static string LearnStatePath { get; } = Path.Combine(DataDirectory, "learn.json");

    /// <summary>C:\ProgramData\AppStart\Learn (recordings of the learn mode)</summary>
    public static string LearnDirectory { get; } = Path.Combine(DataDirectory, "Learn");

    /// <summary>C:\ProgramData\AppStart\Reports</summary>
    public static string ReportDirectory { get; } = Path.Combine(DataDirectory, "Reports");
}
