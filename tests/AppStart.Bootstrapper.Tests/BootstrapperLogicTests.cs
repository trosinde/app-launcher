namespace AppStart.Bootstrapper.Tests;

public class BootstrapperLogicTests
{
    private const string Log = @"C:\ProgramData\AppStart\Logs\install-20261115-105100.log";

    // ------------------------------------------------------------ arguments

    [Fact]
    public void No_arguments_is_interactive() =>
        Assert.Equal(new ParsedArguments(RunMode.Interactive, true), BootstrapperLogic.ParseArguments([]));

    [Theory]
    [InlineData("/quiet")]
    [InlineData("/QUIET")]
    [InlineData("/q")]
    [InlineData("--quiet")]
    public void Quiet_installs_without_ui(string arg) =>
        Assert.Equal(new ParsedArguments(RunMode.Quiet, false), BootstrapperLogic.ParseArguments([arg]));

    [Theory]
    [InlineData("/?")]
    [InlineData("/help")]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_shows_dialog(string arg) =>
        Assert.Equal(new ParsedArguments(RunMode.Help, true), BootstrapperLogic.ParseArguments([arg]));

    [Fact]
    public void Unknown_argument_is_invalid_with_ui() =>
        Assert.Equal(new ParsedArguments(RunMode.Invalid, true, "/passive"), BootstrapperLogic.ParseArguments(["/passive"]));

    [Theory]
    [InlineData("/quiet", "/foo")]
    [InlineData("/foo", "/quiet")]
    [InlineData("/quiet", "/?")]
    public void Quiet_never_shows_ui_even_when_invalid(params string[] args)
    {
        var parsed = BootstrapperLogic.ParseArguments(args);
        Assert.Equal(RunMode.Invalid, parsed.Mode);
        Assert.False(parsed.ShowUi);
    }

    [Fact]
    public void Path_like_argument_is_rejected() =>
        Assert.Equal(RunMode.Invalid, BootstrapperLogic.ParseArguments([@"C:\Users\x\Downloads\evil.msi"]).Mode);

    // ------------------------------------------------------------ exit codes

    [Fact]
    public void Exit_0_is_success_without_message()
    {
        var outcome = BootstrapperLogic.EvaluateMsiexec(0, Log);
        Assert.True(outcome.Success);
        Assert.False(outcome.RestartRequired);
        Assert.Null(outcome.Message);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public void Exit_3010_is_success_with_restart_note()
    {
        var outcome = BootstrapperLogic.EvaluateMsiexec(3010, Log);
        Assert.True(outcome.Success);
        Assert.True(outcome.RestartRequired);
        Assert.Equal(BootstrapperTexts.RestartHeading, outcome.Message!.Heading);
        Assert.Equal(MessageIcon.Information, outcome.Message.Icon);
    }

    [Fact]
    public void Exit_1602_is_cancelled()
    {
        var outcome = BootstrapperLogic.EvaluateMsiexec(1602, Log);
        Assert.False(outcome.Success);
        Assert.Equal(BootstrapperTexts.CancelledHeading, outcome.Message!.Heading);
        Assert.Equal(1602, outcome.ExitCode);
    }

    [Fact]
    public void Exit_1618_asks_to_wait_and_retry()
    {
        var outcome = BootstrapperLogic.EvaluateMsiexec(1618, Log);
        Assert.False(outcome.Success);
        Assert.Contains("Eine andere Installation läuft gerade. Bitte warten und erneut versuchen.", outcome.Message!.Text, StringComparison.Ordinal);
        Assert.Equal(BootstrapperTexts.BusyMeasure, outcome.Message.Measure);
        Assert.Equal(Log, outcome.Message.LogPath);
        Assert.Equal(1618, outcome.ExitCode);
    }

    [Theory]
    [InlineData(1603)]
    [InlineData(1638)]
    [InlineData(1625)]
    [InlineData(-1)]
    public void Other_codes_are_errors_with_cause_measure_and_log(int code)
    {
        var outcome = BootstrapperLogic.EvaluateMsiexec(code, Log);
        Assert.False(outcome.Success);
        Assert.NotEqual(0, outcome.ExitCode);
        Assert.Equal(code, outcome.ExitCode);
        Assert.Equal(MessageIcon.Error, outcome.Message!.Icon);
        Assert.Equal(BootstrapperTexts.FailedMeasure, outcome.Message.Measure);
        Assert.Contains(Log, outcome.Message.Footer, StringComparison.Ordinal);
        Assert.Contains(BootstrapperTexts.MeasureLabel, outcome.Message.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_1603_names_the_fatal_error() =>
        Assert.Equal(BootstrapperTexts.FailedFatalText, BootstrapperLogic.EvaluateMsiexec(1603, Log).Message!.Text);

    [Fact]
    public void Other_code_is_shown_in_the_text() =>
        Assert.Contains("1638", BootstrapperLogic.EvaluateMsiexec(1638, Log).Message!.Text, StringComparison.Ordinal);

    // ------------------------------------------------------------ msiexec, log names

    [Fact]
    public void Log_file_name_has_timestamp() =>
        Assert.Equal("install-20261115-105107.log", BootstrapperLogic.InstallLogFileName(new DateTime(2026, 11, 15, 10, 51, 7)));

    [Fact]
    public void Bootstrapper_log_file_name_has_timestamp() =>
        Assert.Equal("bootstrapper-20260102-030405.log", BootstrapperLogic.BootstrapperLogFileName(new DateTime(2026, 1, 2, 3, 4, 5)));

    [Fact]
    public void Msiexec_arguments_are_silent_without_restart_and_with_verbose_log() =>
        Assert.Equal(
            @"/i ""C:\ProgramData\AppStart\Installer\x\AppStart.msi"" /qn /norestart /l*v ""C:\ProgramData\AppStart\Logs\install-1.log""",
            BootstrapperLogic.MsiexecArguments(@"C:\ProgramData\AppStart\Installer\x\AppStart.msi", @"C:\ProgramData\AppStart\Logs\install-1.log"));

    [Fact]
    public void Msiexec_arguments_reject_quotes() =>
        Assert.Throws<ArgumentException>(() => BootstrapperLogic.MsiexecArguments("C:\\a\" /x \"b.msi", "C:\\log.log"));

    // ------------------------------------------------------------ versions

    [Theory]
    [InlineData("0.1.0", "0.1.0", true)]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1", "0.1.0.0", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("1.9.0", "1.10.0", false)]
    [InlineData(null, "0.1.0", false)]
    [InlineData("0.1.0", null, false)]
    [InlineData("abc", "0.1.0", false)]
    public void Install_is_skipped_only_when_same_or_newer_is_installed(string? installed, string? package, bool skip) =>
        Assert.Equal(skip, BootstrapperLogic.ShouldSkipInstall(installed, package));

    // ------------------------------------------------------------ signature

    [Fact]
    public void Unsigned_bootstrapper_skips_the_check() =>
        Assert.Equal(SignatureDecision.SkippedUnsignedBootstrapper, BootstrapperLogic.DecideSignature(null, null));

    [Fact]
    public void Same_thumbprint_is_trusted() =>
        Assert.Equal(SignatureDecision.Trusted, BootstrapperLogic.DecideSignature("AB12", "ab12"));

    [Theory]
    [InlineData(null)]
    [InlineData("CD34")]
    public void Unsigned_or_foreign_setup_is_untrusted_when_bootstrapper_is_signed(string? setup) =>
        Assert.Equal(SignatureDecision.Untrusted, BootstrapperLogic.DecideSignature("AB12", setup));

    // ------------------------------------------------------------ texts

    [Fact]
    public void All_texts_are_set_and_use_swiss_spelling()
    {
        var texts = typeof(BootstrapperTexts).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetRawConstantValue()!))
            .ToList();

        Assert.True(texts.Count > 30);
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t.Value), t.Name));
        Assert.All(texts, t => Assert.DoesNotContain("ß", t.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void Progress_heading_matches_design() =>
        Assert.Equal("AppStart wird installiert …", BootstrapperTexts.ProgressHeading);

    [Fact]
    public void No_package_message_is_clear() =>
        Assert.Equal("Kein Installationspaket enthalten", BootstrapperLogic.NoPackage.Heading);

    [Fact]
    public void Message_without_measure_or_log_has_plain_content()
    {
        Assert.Equal(BootstrapperTexts.HelpText, BootstrapperLogic.Help.Content);
        Assert.Null(BootstrapperLogic.Help.Footer);
    }
}
