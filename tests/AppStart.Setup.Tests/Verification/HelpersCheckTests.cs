using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Core.Learning;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Verification;

namespace AppStart.Setup.Tests.Verification;

// add-allowed-child-processes, task 4.5: spec verification «Prüfung der Hilfsprogramme».
public sealed class HelpersCheckTests
{
    private const string Export = @"C:\Program Files\Hersteller\AppA\Export.exe";
    private const string Updater = @"C:\Program Files\Hersteller\AppA\Updater.exe";

    private static Machine WithHelpers(ChildProcessMode mode = ChildProcessMode.Allowlist)
    {
        var machine = new Machine();
        var app = Machine.AppA with
        {
            ChildProcesses = mode,
            AllowedChildren = mode == ChildProcessMode.Allowlist
                ?
                [
                    new AllowedChild { Exe = Export, Check = IntegrityCheck.Signature, SignerThumbprint = new string('B', 40) },
                    new AllowedChild { Exe = Updater, Check = IntegrityCheck.Sha256, Sha256 = new string('C', 64) },
                ]
                : null,
        };
        machine.Probe.Config = ConfigLoadResult.Ok(new AppsConfig { Version = 2, Apps = [app] });
        machine.Probe.Files.Add(Export);
        machine.Probe.Files.Add(Updater);
        return machine;
    }

    private static CheckResult Run(Machine machine) => machine.Run(Verifier.HelpersCheckId);

    [Fact]
    public void Intact_helpers_pass()
    {
        var check = Run(WithHelpers());
        Assert.Equal(CheckOutcome.Passed, check.Outcome);
        Assert.Empty(check.Findings);
        Assert.Equal("Hilfsprogramme je App", check.Title);
    }

    // Spec scenario «Hilfsprogramm verändert»: deviation with path and the rehash hint.
    [Fact]
    public void Changed_helper_is_a_deviation_with_rehash_hint()
    {
        var machine = WithHelpers();
        machine.Probe.Integrity[Updater] = IntegrityResult.Fail("SHA-256 weicht ab.");

        var check = Run(machine);

        Assert.Equal(CheckOutcome.Deviation, check.Outcome);
        var finding = Assert.Single(check.Findings);
        Assert.Contains(Updater, finding, StringComparison.Ordinal);
        Assert.Contains($"rehash --app app-a --child \"{Updater}\"", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_helper_is_a_deviation()
    {
        var machine = WithHelpers();
        machine.Probe.Files.Remove(Export);

        Assert.Contains("app-a: Hilfsprogramm " + Export + " fehlt", Run(machine).Findings);
    }

    [Fact]
    public void Writable_helper_folder_is_a_deviation()
    {
        var machine = WithHelpers();
        machine.Probe.WriteViolations[@"C:\Program Files\Hersteller\AppA"] = ["Benutzer hat Schreibrechte."];

        var check = Run(machine);

        Assert.Equal(CheckOutcome.Deviation, check.Outcome);
        Assert.Equal(2, check.Findings.Count(f => f.Contains("beschreibbar", StringComparison.Ordinal)));
    }

    // Spec scenario «Alle weiteren Programme erlaubt»: passed, with the hint and the AppId.
    [Fact]
    public void Mode_all_passes_with_a_note()
    {
        var check = Run(WithHelpers(ChildProcessMode.All));

        Assert.Equal(CheckOutcome.Passed, check.Outcome);
        Assert.Equal("Hinweis: Alle weiteren Programme erlaubt (app-a)", Assert.Single(check.Findings));
    }

    [Fact]
    public void Mode_all_note_is_visible_in_view_and_report()
    {
        var report = WithHelpers(ChildProcessMode.All).Run();

        Assert.Contains("Alle weiteren Programme erlaubt (app-a)", VerifyReportWriter.RenderHtml(report), StringComparison.Ordinal);
        var row = ViewModels.CheckRow.From(report.Checks.Single(c => c.Id == Verifier.HelpersCheckId));
        Assert.False(row.IsDeviation);
        Assert.Contains("Alle weiteren Programme erlaubt", row.Text, StringComparison.Ordinal);
    }

    // Spec scenario «Lernmodus vergessen»: deviation with expiry and the learn stop hint.
    [Fact]
    public void Active_learn_mode_is_a_deviation()
    {
        var machine = WithHelpers();
        machine.Probe.LearnState = new LearnStateLoadResult(
            LearnStateSerializer.Empty.Start("app-a", 30, Machine.Now.AddMinutes(-5)), null, false);

        var check = Run(machine);

        Assert.Equal(CheckOutcome.Deviation, check.Outcome);
        var finding = Assert.Single(check.Findings);
        Assert.Contains("15.11.2026 11:07", finding, StringComparison.Ordinal);
        Assert.Contains("learn stop --app app-a", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Expired_learn_mode_is_fine()
    {
        var machine = WithHelpers();
        machine.Probe.LearnState = new LearnStateLoadResult(
            LearnStateSerializer.Empty.Start("app-a", 5, Machine.Now.AddMinutes(-10)), null, false);

        Assert.Equal(CheckOutcome.Passed, Run(machine).Outcome);
    }

    [Fact]
    public void Unprotected_learn_file_is_a_deviation()
    {
        var machine = WithHelpers();
        machine.Probe.LearnState = new LearnStateLoadResult(LearnStateSerializer.Empty, "learn.json ist nicht geschützt", true);

        Assert.Contains(Run(machine).Findings, f => f.Contains("ignoriert", StringComparison.Ordinal));
    }

    [Fact]
    public void Blocked_app_needs_no_helpers() =>
        Assert.Equal(CheckOutcome.Passed, Run(WithHelpers(ChildProcessMode.Blocked)).Outcome);
}
