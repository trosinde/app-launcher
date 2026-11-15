using AppStart.Core.Configuration;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Tests.Wizard;

public sealed class WizardViewModelTests
{
    private readonly FakeSetupEngine _engine = new();
    private readonly FakeUi _ui = new();

    private async Task<WizardViewModel> CreateAsync()
    {
        var vm = new WizardViewModel(_engine, _ui);
        await vm.InitializeAsync();
        return vm;
    }

    private static bool CanNext(WizardViewModel vm) => vm.NextCommand.CanExecute(null);

    [Fact]
    public async Task Next_IsEnabledOnlyWithAtLeastOneValidSelectedApp()
    {
        var vm = await CreateAsync();
        Assert.True(vm.IsAppsStep);
        Assert.False(CanNext(vm));

        _ui.ExeAnswers.Enqueue([@"C:\Program Files\Hersteller\AppA\AppA.exe", @"C:\Program Files\Hersteller\AppB\AppB.exe"]);
        await vm.AddExeCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Apps.Count);
        Assert.All(vm.Apps, a => Assert.True(a.IsSelected));
        Assert.Same(vm.Apps[1], vm.SelectedApp);
        Assert.True(CanNext(vm));

        vm.Apps[0].IsSelected = false;
        vm.Apps[1].IsSelected = false;
        Assert.False(CanNext(vm));
        Assert.Equal(SetupTexts.NoAppSelected, vm.AppsHint);

        vm.Apps[0].IsSelected = true;
        Assert.True(CanNext(vm));

        vm.Apps[0].RegistryKey = @"HKCU\Software\Hersteller";
        Assert.Equal(SetupTexts.RegistryKeyInvalid, vm.Apps[0].RegistryKeyError);
        Assert.False(CanNext(vm));
        vm.Apps[0].RegistryKey = @"HKLM\SOFTWARE\Hersteller\AppA";
        Assert.Null(vm.Apps[0].RegistryKeyError);
        Assert.True(CanNext(vm));

        vm.Apps[0].DataDir = @"relative\folder";
        Assert.False(CanNext(vm));
        vm.Apps[0].DataDir = @"C:\ProgramData\Hersteller\AppA";
        Assert.True(CanNext(vm));

        // An invalid key on an app that is not selected does not block.
        vm.Apps[1].RegistryKey = "nonsense";
        Assert.True(CanNext(vm));
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Hersteller\AppA", true)]
    [InlineData(@"hklm\software\x", true)]
    [InlineData("", true)]
    [InlineData(@"HKLM\", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software", false)]
    [InlineData(@"SOFTWARE\Hersteller", false)]
    [InlineData(@"HKLM\\SOFTWARE", false)]
    public void RegistryKey_MustStartWithHklm(string key, bool valid) =>
        Assert.Equal(valid, AppItemViewModel.IsValidRegistryKey(key));

    [Fact]
    public async Task Navigation_NextBackAndStepMarks()
    {
        _engine.Snapshot = _engine.Snapshot with { Apps = [FakeSetupEngine.Entry("Applikation A", @"C:\A\A.exe", IntegrityCheck.Signature)] };
        _engine.Snapshot = _engine.Snapshot with { OperatorAccountExists = true, AppAccountExists = true };
        var vm = await CreateAsync();
        Assert.True(vm.Steps[0].IsActive);
        Assert.Equal("1  Applikationen", vm.Steps[0].DisplayText);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));

        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.IsAccountsStep);
        Assert.True(vm.Steps[1].IsActive);
        Assert.Equal("1  Applikationen ✓", vm.Steps[0].DisplayText);
        Assert.True(vm.BackCommand.CanExecute(null));

        vm.BackCommand.Execute(null);
        Assert.True(vm.IsAppsStep);
        Assert.False(vm.Steps[0].IsDone);

        int? closed = null;
        vm.CloseRequested += (_, code) => closed = code;
        vm.CancelCommand.Execute(null);
        Assert.Equal(WizardViewModel.ExitCancelled, closed);
        Assert.Null(_engine.LastRequest);
    }

    [Fact]
    public async Task PreFill_FromLoadCurrent()
    {
        _engine.Snapshot = new(
            [
                FakeSetupEngine.Entry("Applikation A", @"C:\Program Files\Hersteller\AppA\AppA.exe", IntegrityCheck.Signature,
                    @"C:\ProgramData\Hersteller\AppA", @"HKLM\SOFTWARE\Hersteller\AppA"),
                FakeSetupEngine.Entry("Applikation B", @"C:\Program Files\Hersteller\AppB\AppB.exe", IntegrityCheck.Sha256),
            ],
            [@"LABOR\bediener01", @"LAB-PC-014\appstart-user", @"LABOR\bediener02"],
            AppAccountExists: true,
            OperatorAccountExists: false,
            ServiceRunning: true);

        var vm = await CreateAsync();

        Assert.Equal(2, vm.Apps.Count);
        Assert.All(vm.Apps, a => Assert.True(a.IsSelected && a.IsReleased));
        Assert.Equal("Signatur", vm.Apps[0].CheckText);
        Assert.Equal("SHA-256", vm.Apps[1].CheckText);
        Assert.Equal(@"C:\ProgramData\Hersteller\AppA", vm.Apps[0].DataDir);
        Assert.Equal(@"HKLM\SOFTWARE\Hersteller\AppA", vm.Apps[0].RegistryKey);
        Assert.Same(vm.Apps[0], vm.SelectedApp);
        Assert.Equal("Schutz für Applikation A", vm.SelectedApp!.ProtectionHeading);
        Assert.Equal([@"LABOR\bediener01", @"LABOR\bediener02"], vm.Members.Select(m => m.Account));
        Assert.All(vm.Members, m => Assert.False(m.IsNew));
        Assert.Equal(SetupTexts.AppAccountExisting, vm.AppAccountText);
        Assert.False(vm.CreateOperatorAccount); // re-run: not ticked by default
        Assert.True(CanNext(vm));
    }

    [Fact]
    public async Task LoadFailure_IsShown()
    {
        var engine = new ThrowingLoadEngine();
        var vm = new WizardViewModel(engine, _ui);
        await vm.InitializeAsync();
        Assert.Contains("Zugriff verweigert", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task AddExe_InspectErrorIsShownAndDuplicatesAreNotAdded()
    {
        var vm = await CreateAsync();
        _ui.ExeAnswers.Enqueue([@"C:\A\A.exe", @"C:\X\broken.exe"]);
        await vm.AddExeCommand.ExecuteAsync(null);
        Assert.Single(vm.Apps);
        Assert.Contains("broken.exe kann nicht hinzugefügt werden", vm.ErrorMessage, StringComparison.Ordinal);

        vm.Apps[0].IsSelected = false;
        _ui.ExeAnswers.Enqueue([@"c:\a\a.exe"]);
        await vm.AddExeCommand.ExecuteAsync(null);
        Assert.Single(vm.Apps);
        Assert.True(vm.Apps[0].IsSelected);
        Assert.Null(vm.ErrorMessage);

        _ui.FolderAnswer = @"C:\ProgramData\A";
        vm.BrowseDataDirCommand.Execute(null);
        Assert.Equal(@"C:\ProgramData\A", vm.Apps[0].DataDir);

        vm.RemoveAppCommand.Execute(null);
        Assert.Empty(vm.Apps);
        Assert.Null(vm.SelectedApp);
        Assert.False(CanNext(vm));
    }

    [Fact]
    public async Task Password_MustBeNonEmptyAndConfirmed()
    {
        _engine.Snapshot = _engine.Snapshot with { Apps = [FakeSetupEngine.Entry("A", @"C:\A\A.exe", IntegrityCheck.Signature)], AppAccountExists = false };
        var vm = await CreateAsync();
        vm.CreateOperatorAccount = true;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.IsAccountsStep);
        Assert.True(vm.ShowCreateOperator);

        Assert.False(CanNext(vm));
        Assert.Null(vm.PasswordError); // nothing typed yet

        vm.SetPassword(SecureText.Of("Geheim-2026!"));
        Assert.Equal(SetupTexts.PasswordMismatch, vm.PasswordError);
        Assert.False(CanNext(vm));

        vm.SetPasswordConfirm(SecureText.Of("Geheim-2026?"));
        Assert.Equal(SetupTexts.PasswordMismatch, vm.PasswordError);
        Assert.False(CanNext(vm));

        vm.SetPasswordConfirm(SecureText.Of("Geheim-2026!"));
        Assert.Null(vm.PasswordError);
        Assert.True(CanNext(vm));

        vm.SetPassword(SecureText.Of(string.Empty));
        vm.SetPasswordConfirm(SecureText.Of(string.Empty));
        Assert.Equal(SetupTexts.PasswordEmpty, vm.PasswordError);
        Assert.False(CanNext(vm));

        vm.CreateOperatorAccount = false;
        Assert.Null(vm.PasswordError);
        Assert.True(CanNext(vm));

        vm.CreateOperatorAccount = true;
        vm.SetPassword(SecureText.Of("abc"));
        vm.SetPasswordConfirm(SecureText.Of("abc"));
        var request = vm.BuildRequest();
        Assert.True(request.CreateOperatorAccount);
        Assert.Equal("abc", FakeSetupEngine.Plain(request.OperatorPassword!));
        Assert.True(request.OperatorPassword!.IsReadOnly());
    }

    [Fact]
    public async Task ExistingOperatorAccount_NeedsNoPassword()
    {
        _engine.Snapshot = _engine.Snapshot with
        {
            Apps = [FakeSetupEngine.Entry("A", @"C:\A\A.exe", IntegrityCheck.Signature)],
            OperatorAccountExists = true,
        };
        var vm = await CreateAsync();
        vm.CreateOperatorAccount = true;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.False(vm.ShowCreateOperator);
        Assert.True(CanNext(vm));
        var request = vm.BuildRequest();
        Assert.False(request.CreateOperatorAccount);
        Assert.Null(request.OperatorPassword);
    }

    [Fact]
    public async Task Members_AddValidateRemove()
    {
        _engine.Snapshot = _engine.Snapshot with { Operators = [@"LABOR\bediener01"] };
        var vm = await CreateAsync();

        vm.NewMember = @"labor\BEDIENER01";
        Assert.Equal(SetupTexts.MemberDuplicate, vm.MemberError);
        Assert.False(vm.AddMemberCommand.CanExecute(null));

        vm.NewMember = @"LABOR\a\b";
        Assert.Equal(SetupTexts.MemberInvalid, vm.MemberError);
        vm.NewMember = @"LABOR\bad*name";
        Assert.False(vm.AddMemberCommand.CanExecute(null));

        vm.NewMember = @"  LABOR\bediener02 ";
        Assert.True(vm.AddMemberCommand.CanExecute(null));
        vm.AddMemberCommand.Execute(null);
        Assert.Equal(string.Empty, vm.NewMember);
        Assert.Equal([@"LABOR\bediener01", @"LABOR\bediener02"], vm.Members.Select(m => m.Account));

        vm.RemoveMemberCommand.Execute(vm.Members[0]); // existing: stays
        vm.RemoveMemberCommand.Execute(vm.Members[1]); // new: removed
        Assert.Equal([@"LABOR\bediener01"], vm.Members.Select(m => m.Account));
        Assert.Equal([@"LABOR\bediener01"], vm.BuildRequest().AdditionalOperators);
    }

    private async Task<WizardViewModel> RunToSummaryAsync()
    {
        _engine.Snapshot = _engine.Snapshot with { Apps = [FakeSetupEngine.Entry("A", @"C:\A\A.exe", IntegrityCheck.Signature)] };
        var vm = await CreateAsync();
        _ui.ExeAnswers.Enqueue([@"C:\B\B.exe"]);
        await vm.AddExeCommand.ExecuteAsync(null);
        vm.Apps[1].DataDir = @"  C:\ProgramData\B ";
        vm.Apps[1].RegistryKey = @"HKLM\SOFTWARE\B";
        await vm.NextCommand.ExecuteAsync(null);
        vm.CreateOperatorAccount = true;
        vm.SetPassword(SecureText.Of("pw-1"));
        vm.SetPasswordConfirm(SecureText.Of("pw-1"));
        vm.NewMember = @"LABOR\bediener01";
        vm.AddMemberCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task ApplyFlow_ShowsResultLinesVerifiesAndWritesReport()
    {
        var vm = await RunToSummaryAsync();

        Assert.True(vm.IsSummaryStep);
        Assert.True(vm.IsFinished);
        Assert.False(vm.IsBusy);
        Assert.True(vm.Steps[0].IsDone && vm.Steps[1].IsDone && vm.Steps[2].IsActive);

        var request = _engine.LastRequest!;
        Assert.Equal(["a", "b"], request.Apps.Select(a => a.Id));
        Assert.Equal(@"C:\ProgramData\B", request.Apps[1].DataDir);
        Assert.Equal(@"HKLM\SOFTWARE\B", request.Apps[1].RegistryKey);
        Assert.True(request.CreateOperatorAccount);
        Assert.Equal("pw-1", _engine.LastPassword);
        Assert.Equal([@"LABOR\bediener01"], request.AdditionalOperators);

        Assert.Equal(5, vm.Results.Count);
        Assert.All(vm.Results, r => Assert.True(r.Success));
        Assert.Equal("Dienst AppStart läuft, Start automatisch", vm.Results[4].Text);
        Assert.Equal(1, _engine.VerifyCalls);
        Assert.Equal(1, _engine.WriteReportCalls);

        Assert.Equal("Prüfung bestanden – 8 von 8 Prüfpunkten erfüllt", vm.VerifyText);
        Assert.True(vm.VerifyPassed);
        Assert.Equal(SetupTexts.SummaryDoneHeading, vm.SummaryHeading);
        Assert.Equal(SetupTexts.SummaryDonePassedIntro, vm.SummaryIntro);
        Assert.Equal(@"Bericht: C:\ProgramData\AppStart\Reports\verify-2026-11-15-104200.html", vm.ReportText);
        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));

        vm.OpenReportCommand.Execute(null);
        Assert.Equal([_engine.ReportPath], _ui.Opened);

        int? closed = null;
        vm.CloseRequested += (_, code) => closed = code;
        vm.FinishCommand.Execute(null);
        Assert.Equal(WizardViewModel.ExitSuccess, closed);
    }

    [Fact]
    public async Task ApplyFlow_DeviationIsShownInRed()
    {
        _engine.Report = FakeSetupEngine.CreateReport(deviations: 1);
        var vm = await RunToSummaryAsync();
        Assert.Equal("Prüfung nicht bestanden – 1 Abweichung, 7 von 8 Prüfpunkten erfüllt", vm.VerifyText);
        Assert.False(vm.VerifyPassed);
        Assert.Equal(SetupTexts.SummaryDoneDeviationIntro, vm.SummaryIntro);
        Assert.Equal(WizardViewModel.ExitFailed, vm.ExitCode);

        Assert.Equal("Prüfung nicht bestanden – 2 Abweichungen, 6 von 8 Prüfpunkten erfüllt", SetupTexts.VerifySummary(6, 8));
    }

    [Fact]
    public async Task ApplyFlow_FailedStepsAndExceptionsAreShown()
    {
        _engine.ApplyException = new InvalidOperationException("Dienst lässt sich nicht starten");
        _engine.VerifyException = new UnauthorizedAccessException("kein Admin");
        var vm = await RunToSummaryAsync();

        var line = Assert.Single(vm.Results);
        Assert.False(line.Success);
        Assert.Equal("Einrichtung abgebrochen: Dienst lässt sich nicht starten", line.Text);
        Assert.Equal("Prüfung konnte nicht ausgeführt werden: kein Admin", vm.VerifyText);
        Assert.False(vm.HasReportPath);
        Assert.Equal(SetupTexts.SummaryFailedHeading, vm.SummaryHeading);
        Assert.Equal(WizardViewModel.ExitFailed, vm.ExitCode);
        Assert.True(vm.FinishCommand.CanExecute(null));
    }

    private sealed class ThrowingLoadEngine : Engine.ISetupEngine
    {
        public Engine.SetupSnapshot LoadCurrent() => throw new UnauthorizedAccessException("Zugriff verweigert");

        public Engine.AppCandidate Inspect(string exePath) => throw new NotSupportedException();

        public IReadOnlyList<Engine.SetupStepResult> Apply(Engine.SetupRequest request, IProgress<string>? progress = null) => throw new NotSupportedException();

        public Engine.VerifyReport Verify() => throw new NotSupportedException();

        public string WriteReport(Engine.VerifyReport report) => throw new NotSupportedException();
    }
}
