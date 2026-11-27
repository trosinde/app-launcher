using AppStart.Core.Protocol;

namespace AppStart.Launcher.Tests;

public class LauncherLogicTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 51, 0, TimeSpan.Zero);

    [Fact]
    public void Requested_pipe_rights_are_within_what_operators_are_granted()
    {
        const int Granted = 0x12018b;            // design D9, PipeFactory.ClientRights
        const int FileCreatePipeInstance = 0x4;
        var requested = (int)PipeClient.ClientRights;
        Assert.Equal(0, requested & ~Granted);
        Assert.Equal(0, requested & FileCreatePipeInstance);
    }

    [Fact]
    public void Only_app_argument_with_valid_id_is_accepted() =>
        Assert.Equal("app-a", LauncherLogic.ParseAppId(["--app", "app-a"]));

    [Theory]
    [InlineData]
    [InlineData("app-a")]
    [InlineData("--app")]
    [InlineData("--app", "App-A")]
    [InlineData("--app", "app-a", "--extra")]
    [InlineData("--app", "app-a C:\\Windows\\System32\\cmd.exe")]
    [InlineData("--APP", "app-a")]
    [InlineData("--exe", "C:\\Windows\\System32\\cmd.exe")]
    public void Anything_else_is_an_invalid_shortcut(params string[] args) =>
        Assert.Null(LauncherLogic.ParseAppId(args));

    [Fact]
    public void Success_shows_nothing()
    {
        var (message, code) = LauncherLogic.Evaluate(new ClientResult(ClientStatus.Answered, LaunchResponse.Ok), Now);
        Assert.Null(message);
        Assert.Equal(LauncherLogic.ExitOk, code);
    }

    [Fact]
    public void Unreachable_service_matches_ui_design()
    {
        var (message, code) = LauncherLogic.Evaluate(new ClientResult(ClientStatus.Unreachable), Now);
        Assert.Equal(new LauncherMessage(MessageIcon.Warning, "Launch service not reachable",
            "The AppStart service is not running. Please contact your administrator or service support."), message);
        Assert.Equal(LauncherLogic.ExitUnreachable, code);
    }

    [Fact]
    public void Changed_app_matches_ui_design_with_incident_footer()
    {
        var (message, code) = LauncherLogic.Evaluate(
            new ClientResult(ClientStatus.Answered, LaunchResponse.Denied(DenyReason.Integrity, 4711)), Now);

        Assert.NotNull(message);
        Assert.Equal(MessageIcon.Error, message.Icon);
        Assert.Equal("Launch denied", message.Heading);
        Assert.StartsWith("The application has been modified and may not be started.", message.Text);
        Assert.Equal($"Event ID 4711 · {Now.LocalDateTime:dd.MM.yyyy HH:mm}", message.Footer);
        Assert.Equal(LauncherLogic.ExitDenied, code);
    }

    [Theory]
    [InlineData(DenyReason.UnknownApp, Texts.DeniedUnknownAppText)]
    [InlineData(DenyReason.Config, Texts.DeniedConfigText)]
    [InlineData(DenyReason.Error, Texts.DeniedErrorText)]
    public void Every_denial_has_its_own_text_and_footer(DenyReason reason, string text)
    {
        var (message, _) = LauncherLogic.Evaluate(new ClientResult(ClientStatus.Answered, LaunchResponse.Denied(reason, 12)), Now);
        Assert.Equal(text, message!.Text);
        Assert.StartsWith("Event ID 12 · ", message.Footer);
    }

    [Fact]
    public void Busy_is_only_an_information()
    {
        var (message, _) = LauncherLogic.Evaluate(new ClientResult(ClientStatus.Answered, LaunchResponse.Denied(DenyReason.Busy, 5)), Now);
        Assert.Equal(MessageIcon.Information, message!.Icon);
        Assert.Equal(Texts.BusyHeading, message.Heading);
        Assert.Null(message.Footer);
    }

    [Fact]
    public void No_permission_and_protocol_errors_have_messages()
    {
        Assert.Equal(Texts.NoPermissionHeading, LauncherLogic.Evaluate(new ClientResult(ClientStatus.AccessDenied), Now).Message!.Heading);
        Assert.Equal(Texts.ProtocolHeading, LauncherLogic.Evaluate(new ClientResult(ClientStatus.ProtocolError), Now).Message!.Heading);
    }

    [Theory]
    [InlineData(@"C:\Users\Public\Desktop\Application A.lnk", "Application A")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\AppStart TestApp.lnk", "AppStart TestApp")]
    [InlineData(null, "AppStart")]
    [InlineData("", "AppStart")]
    [InlineData(@"C:\Program Files\AppStart\current\AppStart.Launcher.exe", "AppStart")]
    public void Title_comes_from_the_shortcut(string? link, string title) =>
        Assert.Equal(title, LauncherLogic.TitleFromShortcut(link));
}
