namespace AppStart.Bootstrapper.Tests;

// add-allowed-child-processes, task 5.1 (design D6, spec installation "Bootstrapper with answer file").
public class ConfigArgumentTests
{
    private const string Config = @"C:\Deploy\appstart.json";

    [Theory]
    [InlineData("/quiet", "/config", Config)]
    [InlineData("/config", Config, "/quiet")]
    [InlineData("/q", "--config", Config)]
    public void Quiet_with_config_installs_and_applies(params string[] args) =>
        Assert.Equal(new ParsedArguments(RunMode.Quiet, false, null, Config), BootstrapperLogic.ParseArguments(args));

    // "/config without /quiet SHALL be invalid (exit code 87)".
    [Fact]
    public void Config_without_quiet_is_invalid_with_ui()
    {
        var parsed = BootstrapperLogic.ParseArguments(["/config", Config]);
        Assert.Equal(RunMode.Invalid, parsed.Mode);
        Assert.True(parsed.ShowUi);
    }

    [Theory]
    [InlineData("/quiet", "/config")]                                 // path missing
    [InlineData("/quiet", "/config", "appstart.json")]                // relative
    [InlineData("/quiet", "/config", @"\\server\share\a.json")]       // UNC
    [InlineData("/quiet", "/config", "C:\\a\" /x \"b.json")]           // quote injection
    [InlineData("/quiet", "/config", Config, "/config", Config)]      // twice
    [InlineData("/quiet", "/config", Config, "/?")]                   // help is UI
    [InlineData("/quiet", "/config", Config, "/foo")]
    public void Invalid_config_use_never_shows_ui(params string[] args)
    {
        var parsed = BootstrapperLogic.ParseArguments(args);
        Assert.Equal(RunMode.Invalid, parsed.Mode);
        Assert.False(parsed.ShowUi);
        Assert.Null(parsed.ConfigPath);
    }

    [Fact]
    public void Quiet_alone_has_no_config() =>
        Assert.Null(BootstrapperLogic.ParseArguments(["/quiet"]).ConfigPath);

    [Fact]
    public void Apply_arguments_quote_the_path() =>
        Assert.Equal("apply --config \"C:\\Deploy\\my app.json\"", BootstrapperLogic.ApplyArguments(@"C:\Deploy\my app.json"));

    [Fact]
    public void Apply_arguments_refuse_quotes() =>
        Assert.Throws<ArgumentException>(() => BootstrapperLogic.ApplyArguments("C:\\a\".json"));

    [Fact]
    public void Apply_log_is_named_with_the_time() =>
        Assert.Equal("apply-20261115-104200.log", BootstrapperLogic.ApplyLogFileName(new DateTime(2026, 11, 15, 10, 42, 0)));

    // Exit codes of design D6 and the spec scenarios "Software distribution in one step" / "Setup fails".
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 3010, 3010)]
    [InlineData(1, 0, 10)]
    [InlineData(1, 3010, 10)]
    [InlineData(2, 0, 11)]
    [InlineData(-1, 0, 10)]
    public void Apply_exit_codes_are_mapped(int apply, int msi, int expected) =>
        Assert.Equal(expected, BootstrapperLogic.MapApplyExit(apply, msi));

    [Fact]
    public void New_exit_codes_do_not_collide_with_existing_ones()
    {
        int[] existing =
        [
            BootstrapperLogic.ExitOk, BootstrapperLogic.ExitSetupStartFailed, BootstrapperLogic.ExitSetupMissing,
            BootstrapperLogic.ExitNotAdmin, BootstrapperLogic.ExitInvalidArgument, BootstrapperLogic.ExitSetupUntrusted,
            BootstrapperLogic.ExitInternalError, BootstrapperLogic.ExitNoPackage, BootstrapperLogic.ExitMsiSuccessRestart,
            BootstrapperLogic.ExitMsiCancelled, BootstrapperLogic.ExitMsiBusy,
        ];
        Assert.DoesNotContain(BootstrapperLogic.ExitApplyFailed, existing);
        Assert.DoesNotContain(BootstrapperLogic.ExitApplyInvalidFile, existing);
    }

    [Fact]
    public void Help_mentions_config() =>
        Assert.Contains("/quiet /config", BootstrapperTexts.HelpText, StringComparison.Ordinal);
}
