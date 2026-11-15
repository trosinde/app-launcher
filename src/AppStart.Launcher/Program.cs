using AppStart.Launcher;

// AppStart.Launcher.exe --app <AppId>
// Sends "START <AppId>" to the service. Success: no window. Failure: one dialog, exit code != 0.
var title = LauncherLogic.TitleFromShortcut(Dialog.StartingShortcut());

var appId = LauncherLogic.ParseAppId(args);
if (appId is null)
{
    Dialog.Show(title, LauncherLogic.InvalidShortcut);
    return LauncherLogic.ExitInvalidShortcut;
}

var (message, exitCode) = LauncherLogic.Evaluate(PipeClient.SendStart(appId), DateTimeOffset.Now);
if (message is not null)
{
    Dialog.Show(title, message);
}

return exitCode;
