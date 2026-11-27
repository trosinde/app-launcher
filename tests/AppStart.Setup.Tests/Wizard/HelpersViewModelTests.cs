using AppStart.Core.Configuration;
using AppStart.Core.Learning;
using AppStart.Setup.Engine;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Tests.Wizard;

// add-allowed-child-processes, task 4.2: spec installation "Other programs in the wizard".
public sealed class HelpersViewModelTests
{
    private const string Export = @"C:\Program Files\Hersteller\AppA\Export.exe";
    private const string Updater = @"C:\Program Files\Hersteller\AppA\Updater.exe";
    private const string Reader = @"C:\Program Files\Adobe\Reader\AcroRd32.exe";
    private const string Cmd = @"C:\Windows\System32\cmd.exe";

    private readonly FakeSetupEngine _engine = new();
    private readonly FakeUi _ui = new();

    private static AppItemViewModel App(bool released = true, ChildProcessMode mode = ChildProcessMode.Blocked, params AllowedChild[] children) => new(
        new AppCandidate(@"C:\Program Files\Hersteller\AppA\AppA.exe", "Application A", "appa", IntegrityCheck.Signature, new string('A', 40), null, null, null,
            mode, children),
        isSelected: true,
        isReleased: released);

    private static AllowedChild Sha(string exe) => new() { Exe = exe, Check = IntegrityCheck.Sha256, Sha256 = new string('c', 64) };

    // ------------------------------------------------ step 1 (AppItemViewModel / WizardViewModel)

    [Fact]
    public void Mode_and_list_go_into_the_candidate()
    {
        var app = App();
        app.ChildModeIndex = 1; // "Allowed helper programs only"
        Assert.False(app.IsValid); // allowlist needs entries
        Assert.Equal(SetupTexts.ChildAllowlistEmpty, app.ChildError);

        app.AllowedChildren = [Sha(Export)];

        Assert.True(app.IsValid);
        Assert.Equal("1 allowed", app.ChildSummary);
        Assert.Equal("Helper programs (1)…", app.HelpersButtonText);
        var candidate = app.ToCandidate();
        Assert.Equal(ChildProcessMode.Allowlist, candidate.ChildProcesses);
        Assert.Equal(Export, Assert.Single(candidate.AllowedChildren!).Exe);
    }

    [Fact]
    public void Blocked_drops_the_list_from_the_candidate_but_keeps_it_for_switching_back()
    {
        var app = App(mode: ChildProcessMode.Allowlist, children: Sha(Export));
        app.ChildMode = ChildProcessMode.Blocked;

        Assert.Null(app.ToCandidate().AllowedChildren);
        Assert.Equal("blocked", app.ChildSummary);
        app.ChildMode = ChildProcessMode.Allowlist;
        Assert.Single(app.ToCandidate().AllowedChildren!);
    }

    // Spec scenario "All allowed": confirmation first.
    [Theory]
    [InlineData(false, ChildProcessMode.Blocked)]
    [InlineData(true, ChildProcessMode.All)]
    public async Task All_needs_a_confirmation(bool answer, ChildProcessMode expected)
    {
        _engine.Snapshot = _engine.Snapshot with { Apps = [FakeSetupEngine.Entry("Application A", @"C:\A\A.exe", IntegrityCheck.Sha256)] };
        var wizard = new WizardViewModel(_engine, _ui);
        await wizard.InitializeAsync();
        _ui.ConfirmAnswers.Enqueue(answer);

        wizard.SelectedApp!.ChildModeIndex = 2;

        Assert.Equal(SetupTexts.ConfirmAllHeading, Assert.Single(_ui.Confirmations));
        Assert.Equal(expected, wizard.SelectedApp.ChildMode);
    }

    // Spec scenario "Allow helper program via dialog": allowlist + Export.exe reach the engine.
    [Fact]
    public async Task Helper_added_in_the_dialog_reaches_apply()
    {
        _engine.Snapshot = _engine.Snapshot with { Apps = [FakeSetupEngine.Entry("Application A", @"C:\A\A.exe", IntegrityCheck.Sha256)] };
        var wizard = new WizardViewModel(_engine, _ui);
        await wizard.InitializeAsync();
        wizard.SelectedApp!.ChildModeIndex = 1;
        _ui.ExeAnswers.Enqueue([Export]);
        _ui.OnHelpers = vm =>
        {
            vm.AddExeCommand.Execute(null);
            vm.ApplyCommand.Execute(null);
            return true;
        };

        wizard.OpenHelpersCommand.Execute(null);
        await wizard.NextCommand.ExecuteAsync(null); // step 2
        await wizard.NextCommand.ExecuteAsync(null); // apply

        var app = Assert.Single(_engine.LastRequest!.Apps);
        Assert.Equal(ChildProcessMode.Allowlist, app.ChildProcesses);
        Assert.Equal(Export, Assert.Single(app.AllowedChildren!).Exe);
    }

    [Fact]
    public void Cancel_in_the_dialog_keeps_the_old_list()
    {
        var app = App(mode: ChildProcessMode.Allowlist, children: Sha(Export));
        var vm = new HelpersViewModel(_engine, _ui, app);
        _ui.ExeAnswers.Enqueue([Updater]);
        vm.AddExeCommand.Execute(null);
        vm.CancelCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.Single(app.AllowedChildren);
    }

    // ------------------------------------------------ dialog "Allowed helper programs"

    [Fact]
    public void States_and_unsigned_warning_follow_the_design()
    {
        _engine.Changed.Add(Updater);
        var vm = new HelpersViewModel(_engine, _ui, App(mode: ChildProcessMode.Allowlist,
            children: [new AllowedChild { Exe = Export, Check = IntegrityCheck.Signature, SignerThumbprint = new string('B', 40) }, Sha(Updater)]));

        Assert.Equal(SetupTexts.HelperStateOk, vm.Items[0].State);
        Assert.Equal(SetupTexts.HelperStateRenew, vm.Items[1].State);
        Assert.Equal("Updater.exe is not signed", vm.UnsignedWarning);
    }

    [Fact]
    public void Rehash_renews_the_checked_sha256_entry()
    {
        _engine.Changed.Add(Updater);
        var vm = new HelpersViewModel(_engine, _ui, App(mode: ChildProcessMode.Allowlist, children: Sha(Updater)));
        vm.Items[0].IsChecked = true;

        vm.RehashCommand.Execute(null);

        Assert.Equal(SetupTexts.HelperStateOk, vm.Items[0].State);
        Assert.Equal(new string('d', 64), vm.Items[0].Entry.Sha256);
    }

    [Fact]
    public void Remove_deletes_checked_entries()
    {
        var vm = new HelpersViewModel(_engine, _ui, App(mode: ChildProcessMode.Allowlist, children: [Sha(Export), Sha(Updater)]));
        vm.Items[0].IsChecked = true;
        vm.RemoveCommand.Execute(null);
        vm.ApplyCommand.Execute(null);

        Assert.Equal(Updater, Assert.Single(vm.Result!).Exe);
    }

    [Fact]
    public void Helper_in_writable_folder_is_not_added()
    {
        _engine.Writable.Add(Export);
        var vm = new HelpersViewModel(_engine, _ui, App());
        _ui.ExeAnswers.Enqueue([Export]);

        vm.AddExeCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.Contains("can write", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Shell_needs_confirmation(bool answer, int expected)
    {
        var vm = new HelpersViewModel(_engine, _ui, App());
        _ui.ExeAnswers.Enqueue([Cmd]);
        _ui.ConfirmAnswers.Enqueue(answer);

        vm.AddExeCommand.Execute(null);

        Assert.Equal("Allow cmd.exe?", Assert.Single(_ui.Confirmations));
        Assert.Equal(expected, vm.Items.Count);
        if (expected == 1)
        {
            Assert.True(vm.Items[0].Entry.AllowShell);
        }
    }

    // ------------------------------------------------ learn mode

    [Fact]
    public void Learn_mode_needs_a_released_app()
    {
        var vm = new HelpersViewModel(_engine, _ui, App(released: false));
        vm.StartLearningCommand.Execute(null);

        Assert.Empty(_engine.LearnCalls);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void Cancelled_learn_mode_stops_and_discards()
    {
        var vm = new HelpersViewModel(_engine, _ui, App());
        vm.StartLearningCommand.Execute(null);

        Assert.Equal(["start:appa", "stop:appa", "discard:appa"], _engine.LearnCalls);
        Assert.Empty(vm.Items);
    }

    // Spec scenario "Shell in the learning result": marked and not selectable; the rest is taken.
    [Fact]
    public void Learn_result_proposals_are_taken_except_shells_and_viewers()
    {
        var t = DateTimeOffset.UtcNow;
        _engine.Recording = new LearnRecording { AppId = "appa" }
            .Record(Export, new string('B', 40), new string('1', 64), null, t)
            .Record(Updater, null, new string('2', 64), null, t.AddSeconds(1))
            .Record(Reader, new string('E', 40), new string('3', 64), null, t.AddSeconds(2))
            .Record(Cmd, new string('F', 40), new string('4', 64), null, t.AddSeconds(3));
        LearnResultViewModel? seen = null;
        _ui.OnLearn = learn => learn.FinishCommand.Execute(null);
        _ui.OnLearnResult = result =>
        {
            seen = result;
            result.TakeOverCommand.Execute(null);
            return true;
        };
        var vm = new HelpersViewModel(_engine, _ui, App());

        vm.StartLearningCommand.Execute(null);

        Assert.NotNull(seen);
        var cmd = seen.Proposals.Single(p => p.FileName == "cmd.exe");
        Assert.False(cmd.IsSelectable);
        Assert.False(cmd.IsChecked);
        Assert.Equal(SetupTexts.LearnHintCmd, cmd.Hint);
        Assert.False(seen.Proposals.Single(p => p.FileName == "AcroRd32.exe").IsChecked); // "consider carefully"
        Assert.Equal(SetupTexts.LearnHintUnsigned, seen.Proposals.Single(p => p.FileName == "Updater.exe").Hint);
        Assert.Equal([Export, Updater], vm.Items.Select(i => i.Exe));
        Assert.Equal(IntegrityCheck.Signature, vm.Items[0].Entry.Check);
        Assert.Equal(IntegrityCheck.Sha256, vm.Items[1].Entry.Check);
        Assert.Equal(["start:appa", "stop:appa", "discard:appa"], _engine.LearnCalls);
    }

    [Fact]
    public void Take_over_button_counts_the_checked_proposals()
    {
        var rec = new LearnRecording { AppId = "appa" }.Record(Export, new string('B', 40), new string('1', 64), null, DateTimeOffset.UtcNow);
        var vm = new LearnResultViewModel(_engine, "appa", "Application A", rec);

        Assert.Equal("Apply 1 program", vm.TakeOverText);
        vm.Proposals[0].IsChecked = false;
        Assert.Equal("Apply 0 programs", vm.TakeOverText);
    }

    [Fact]
    public void Learn_countdown_ends_by_itself()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        var vm = new LearnViewModel(_engine, "appa", "Application A", time);
        Assert.True(vm.Start(5));
        Assert.Equal(LearnOutcome.Running, vm.Outcome);

        time.Advance(TimeSpan.FromMinutes(6));
        vm.Refresh();

        Assert.Equal(LearnOutcome.Finished, vm.Outcome);
        Assert.Contains("stop:appa", _engine.LearnCalls);
    }

    [Fact]
    public void Failed_start_shows_the_reason()
    {
        _engine.StartLearningFails = true;
        var vm = new LearnViewModel(_engine, "appa", "Application A", TimeProvider.System);

        Assert.False(vm.Start());
        Assert.Contains("not approved", vm.ErrorMessage, StringComparison.Ordinal);
    }
}
