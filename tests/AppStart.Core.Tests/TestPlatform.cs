namespace AppStart.Core.Tests;

/// <summary>Skip conditions: ACL, Authenticode and file-sharing tests need real Windows.</summary>
public static class TestPlatform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public const string WindowsOnly = "Needs Windows (ACL, Authenticode or file lock).";
}
