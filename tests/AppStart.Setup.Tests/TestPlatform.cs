namespace AppStart.Setup.Tests;

/// <summary>Skip conditions. Tests that change accounts, ACLs of system folders or services must not run here at all.</summary>
public static class TestPlatform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public const string WindowsOnly = "Braucht Windows.";
}
