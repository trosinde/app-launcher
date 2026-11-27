namespace AppStart.TestApp;

/// <summary>
/// All user-visible texts of the test app in one place (English UI, see openspec/project.md).
/// The manual completeness test (task 10.4) reads messages from here.
/// </summary>
public static class Texts
{
    public const string WindowTitle = "AppStart TestApp";
    public const string Heading = "State of the test application";
    public const string SubHeading = "Checks whether starting through AppStart works as intended.";

    public const string RowAccount = "Running account";
    public const string RowAdmin = "Admin rights";
    public const string RowRegistry = "Read/write registry";
    public const string RowFiles = "Read/write files";
    public const string RowChild = "Child process (cmd.exe)";

    public const string Yes = "Yes";
    public const string No = "No";

    public const string BadgeOk = "OK";
    public const string BadgeError = "Error";
    public const string BadgePending = "Open";
    public const string BadgeCheck = "Check";
    public const string BadgeBreakout = "Breakout";
    public const string BadgeNotViaAppStart = "Not via AppStart";

    public const string ChildNotTested = "Not checked yet";
    public const string ChildBlocked = "Start blocked by job object";
    public const string ChildStarted = "Started – breakout possible";
    public const string ChildFailed = "Start failed: {0}";
    public const string ChildTerminated = "Ended immediately by AppStart (not allowed)";

    // add-allowed-child-processes, task 6.1: own helper program next to the TestApp
    public const string RowHelper = "Helper program (TestHelper)";
    public const string HelperButton = "Start allowed helper program";
    public const string HelperRan = "Started and ended: {0}";
    public const string HelperBlocked = "Start blocked by job object (mode blocked)";
    public const string HelperTerminated = "Ended immediately by AppStart (not on the list)";
    public const string BadgeAllowed = "Allowed";
    public const string BadgeBlocked = "Blocked";

    public const string TestValueLabel = "Test value";
    public const string DefaultTestValue = "Test value 42";
    public const string SaveButton = "Save to registry and file";
    public const string CommandPromptButton = "Start command prompt";
    public const string RecheckButton = "Check again";

    public const string CheckedAt = "Checked at {0:HH:mm:ss}";
    public const string SavedAt = "Saved at {0:HH:mm:ss}";
    public const string SaveFailed = "Saving failed: {0}";

    public const string NoAdminTitle = "AppStart TestApp";
    public const string NoAdminMessage =
        "The test application needs administrator rights and will exit.\n\n" +
        "Please start it through the AppStart shortcut.";
}
