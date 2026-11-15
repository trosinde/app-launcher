using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Security;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStart.Setup.ViewModels;

/// <summary>
/// The 3-step setup wizard (task 6.1): 1 applications, 2 accounts and group, 3 apply + verify.
/// Engine calls run on the thread pool; results come back on the caller's synchronization context.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject, IDisposable
{
    public const int StepApps = 0;
    public const int StepAccounts = 1;
    public const int StepSummary = 2;

    public const int ExitSuccess = 0;
    public const int ExitFailed = 1;
    public const int ExitCancelled = 2;

    private const string OperatorAccountName = "appstart-user";

    private readonly ISetupEngine _engine;
    private readonly IUiServices _ui;
    private SecureString? _password;
    private SecureString? _passwordConfirm;
    private bool _passwordTouched;

    public WizardViewModel(ISetupEngine engine, IUiServices ui)
    {
        _engine = engine;
        _ui = ui;
        Steps = [new StepItem(1, SetupTexts.NavApps), new StepItem(2, SetupTexts.NavAccounts), new StepItem(3, SetupTexts.NavSummary)];
        Apps.CollectionChanged += OnAppsChanged;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
        UpdateSteps();
    }

    /// <summary>Raised when the window should close; the argument is the exit code.</summary>
    public event EventHandler<int>? CloseRequested;

    public IReadOnlyList<StepItem> Steps { get; }

    // ------------------------------------------------------------------ Navigation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppsStep), nameof(IsAccountsStep), nameof(IsSummaryStep))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(CancelCommand))]
    private int _currentStep;

    public bool IsAppsStep => CurrentStep == StepApps;

    public bool IsAccountsStep => CurrentStep == StepAccounts;

    public bool IsSummaryStep => CurrentStep == StepSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(CancelCommand), nameof(AddExeCommand), nameof(RemoveAppCommand))]
    private bool _isBusy;

    /// <summary>Apply and verify have run (successfully or not); only «Fertig» remains.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryHeading), nameof(SummaryIntro))]
    [NotifyCanExecuteChangedFor(nameof(FinishCommand), nameof(CancelCommand))]
    private bool _isFinished;

    /// <summary>The window may close (not while the engine changes the system).</summary>
    public bool CanClose => !IsBusy;

    /// <summary>0 = set up and verify passed, 1 = errors or deviations, 2 = cancelled before applying.</summary>
    public int ExitCode => !IsFinished ? ExitCancelled : AllStepsSucceeded && VerifyPassed ? ExitSuccess : ExitFailed;

    partial void OnCurrentStepChanged(int value) => UpdateSteps();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanClose));

    private void UpdateSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActive = i == CurrentStep;
            Steps[i].IsDone = i < CurrentStep;
        }
    }

    public bool CanGoNext => !IsBusy && CurrentStep switch
    {
        StepApps => AppsStepValid,
        StepAccounts => AccountsStepValid,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentStep == StepApps)
        {
            CurrentStep = StepAccounts;
        }
        else if (CurrentStep == StepAccounts)
        {
            CurrentStep = StepSummary;
            await RunSetupAsync();
        }
    }

    private bool CanGoBack => !IsBusy && CurrentStep == StepAccounts;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStep = StepApps;

    private bool CanCancel => !IsBusy && !IsFinished && CurrentStep != StepSummary;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => CloseRequested?.Invoke(this, ExitCancelled);

    [RelayCommand(CanExecute = nameof(IsFinished))]
    private void Finish() => CloseRequested?.Invoke(this, ExitCode);

    private void RefreshNext()
    {
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ Loading (task 6.7: re-run shows the current configuration)

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppAccountText))]
    private bool _appAccountExists;

    public string AppAccountText => AppAccountExists ? SetupTexts.AppAccountExisting : SetupTexts.AppAccountNew;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var snapshot = await Task.Run(_engine.LoadCurrent);
            Apply(snapshot);
        }
        catch (Exception ex)
        {
            ErrorMessage = SetupTexts.Format(SetupTexts.LoadFailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            RefreshNext();
        }
    }

    private void Apply(SetupSnapshot snapshot)
    {
        foreach (var item in Apps)
        {
            item.PropertyChanged -= OnAppItemChanged;
        }

        Apps.Clear();
        foreach (var entry in snapshot.Apps)
        {
            Apps.Add(AppItemViewModel.FromEntry(entry));
        }

        SelectedApp = Apps.FirstOrDefault();
        AppAccountExists = snapshot.AppAccountExists;
        OperatorAccountExists = snapshot.OperatorAccountExists;
        CreateOperatorAccount = !snapshot.OperatorAccountExists && !snapshot.IsConfigured;

        Members.Clear();
        foreach (var op in snapshot.Operators.Where(o => !IsOperatorAccount(o)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Members.Add(new MemberItem(op, IsNew: false));
        }
    }

    private static bool IsOperatorAccount(string account) =>
        account.Equals(OperatorAccountName, StringComparison.OrdinalIgnoreCase)
        || account.EndsWith(@"\" + OperatorAccountName, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Step 1: applications

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedApp))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAppCommand), nameof(BrowseDataDirCommand))]
    private AppItemViewModel? _selectedApp;

    public bool HasSelectedApp => SelectedApp is not null;

    public bool HasApps => Apps.Count > 0;

    public bool AppsStepValid => Apps.Any(a => a.IsSelected) && Apps.Where(a => a.IsSelected).All(a => a.IsValid);

    /// <summary>Hint below the list (null when nothing to say).</summary>
    public string? AppsHint => Apps.Count > 0 && !Apps.Any(a => a.IsSelected) ? SetupTexts.NoAppSelected : null;

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AppItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnAppItemChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AppItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnAppItemChanged;
            }
        }

        OnPropertyChanged(nameof(HasApps));
        OnAppItemChanged(sender, new PropertyChangedEventArgs(null));
    }

    private void OnAppItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AppsStepValid));
        OnPropertyChanged(nameof(AppsHint));
        RefreshNext();
    }

    private bool CanEditApps => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private async Task AddExeAsync()
    {
        var files = _ui.PickExeFiles();
        if (files.Count == 0)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        var errors = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var existing = Apps.FirstOrDefault(a => string.Equals(a.ExePath, file, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    existing.IsSelected = true;
                    SelectedApp = existing;
                    continue;
                }

                try
                {
                    var candidate = await Task.Run(() => _engine.Inspect(file));
                    var item = new AppItemViewModel(candidate, isSelected: true, isReleased: false);
                    Apps.Add(item);
                    SelectedApp = item;
                }
                catch (Exception ex)
                {
                    errors.Add(SetupTexts.Format(SetupTexts.InspectFailedFormat, Path.GetFileName(file), ex.Message));
                }
            }
        }
        finally
        {
            IsBusy = false;
            ErrorMessage = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
            RefreshNext();
        }
    }

    private bool CanRemoveApp => !IsBusy && SelectedApp is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveApp))]
    private void RemoveApp()
    {
        if (SelectedApp is null)
        {
            return;
        }

        var index = Apps.IndexOf(SelectedApp);
        Apps.Remove(SelectedApp);
        SelectedApp = Apps.Count == 0 ? null : Apps[Math.Min(index, Apps.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void BrowseDataDir()
    {
        if (SelectedApp is null)
        {
            return;
        }

        var folder = _ui.PickFolder(string.IsNullOrWhiteSpace(SelectedApp.DataDir) ? null : SelectedApp.DataDir);
        if (folder is not null)
        {
            SelectedApp.DataDir = folder;
        }
    }

    // ------------------------------------------------------------------ Step 2: accounts and group

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCreateOperator), nameof(PasswordError), nameof(AccountsStepValid))]
    private bool _operatorAccountExists;

    /// <summary>Checkbox and password fields only when appstart-user does not exist yet.</summary>
    public bool ShowCreateOperator => !OperatorAccountExists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordError), nameof(AccountsStepValid))]
    private bool _createOperatorAccount;

    partial void OnCreateOperatorAccountChanged(bool value) => RefreshNext();

    partial void OnOperatorAccountExistsChanged(bool value) => RefreshNext();

    /// <summary>From PasswordBox.SecurePassword (a copy; the view model owns and disposes it).</summary>
    public void SetPassword(SecureString password)
    {
        _password?.Dispose();
        _password = password;
        PasswordChanged();
    }

    public void SetPasswordConfirm(SecureString password)
    {
        _passwordConfirm?.Dispose();
        _passwordConfirm = password;
        PasswordChanged();
    }

    private void PasswordChanged()
    {
        _passwordTouched = true;
        OnPropertyChanged(nameof(PasswordError));
        OnPropertyChanged(nameof(AccountsStepValid));
        RefreshNext();
    }

    private string? PasswordValidation()
    {
        if (!CreateOperatorAccount || OperatorAccountExists)
        {
            return null;
        }

        if (_password is null || _password.Length == 0)
        {
            return SetupTexts.PasswordEmpty;
        }

        return SecureStrings.AreEqual(_password, _passwordConfirm) ? null : SetupTexts.PasswordMismatch;
    }

    /// <summary>Shown only after the user typed in one of the fields.</summary>
    public string? PasswordError => _passwordTouched ? PasswordValidation() : null;

    public bool AccountsStepValid => PasswordValidation() is null;

    public ObservableCollection<MemberItem> Members { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    [NotifyPropertyChangedFor(nameof(MemberError))]
    private string _newMember = string.Empty;

    public string? MemberError
    {
        get
        {
            var account = NewMember.Trim();
            if (account.Length == 0)
            {
                return null;
            }

            if (!IsValidAccountName(account))
            {
                return SetupTexts.MemberInvalid;
            }

            return Members.Any(m => m.Account.Equals(account, StringComparison.OrdinalIgnoreCase)) ? SetupTexts.MemberDuplicate : null;
        }
    }

    public static bool IsValidAccountName(string account)
    {
        var parts = account.Split('\\');
        if (parts.Length > 2 || parts.Any(p => p.Length == 0 || p.Trim() != p))
        {
            return false;
        }

        const string invalid = "\"/[]:|<>+=;,?*@";
        return parts.All(p => p.IndexOfAny(invalid.ToCharArray()) < 0);
    }

    private bool CanAddMember => NewMember.Trim().Length > 0 && MemberError is null;

    [RelayCommand(CanExecute = nameof(CanAddMember))]
    private void AddMember()
    {
        Members.Add(new MemberItem(NewMember.Trim(), IsNew: true));
        NewMember = string.Empty;
    }

    [RelayCommand]
    private void RemoveMember(MemberItem? member)
    {
        if (member is { IsNew: true })
        {
            Members.Remove(member);
        }
    }

    // ------------------------------------------------------------------ Step 3: apply and verify

    public ObservableCollection<ResultLine> Results { get; } = [];

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryHeading), nameof(SummaryIntro), nameof(HasVerifyResult), nameof(VerifyPassed))]
    private VerifyReport? _report;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerifyResult))]
    private string? _verifyText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReportPath))]
    [NotifyCanExecuteChangedFor(nameof(OpenReportCommand))]
    private string? _reportPath;

    [ObservableProperty]
    private string? _reportText;

    public bool HasReportPath => ReportPath is not null;

    public bool HasResults => Results.Count > 0;

    public bool HasVerifyResult => VerifyText is not null;

    public bool AllStepsSucceeded => Results.All(r => r.Success);

    public bool VerifyPassed => Report is { Deviations: 0 };

    public string SummaryHeading => !IsFinished
        ? SetupTexts.SummaryRunningHeading
        : AllStepsSucceeded ? SetupTexts.SummaryDoneHeading : SetupTexts.SummaryFailedHeading;

    public string SummaryIntro => !IsFinished
        ? SetupTexts.SummaryRunningIntro
        : !AllStepsSucceeded ? SetupTexts.SummaryFailedIntro
        : VerifyPassed ? SetupTexts.SummaryDonePassedIntro : SetupTexts.SummaryDoneDeviationIntro;

    /// <summary>What step 3 hands to the engine (also used by tests).</summary>
    public SetupRequest BuildRequest()
    {
        var create = CreateOperatorAccount && !OperatorAccountExists;
        SecureString? password = null;
        if (create && _password is not null)
        {
            password = _password.Copy();
            password.MakeReadOnly();
        }

        return new SetupRequest(
            Apps.Where(a => a.IsSelected).Select(a => a.ToCandidate()).ToList(),
            create,
            password,
            Members.Select(m => m.Account).ToList());
    }

    private async Task RunSetupAsync()
    {
        IsBusy = true;
        IsFinished = false;
        Results.Clear();
        Report = null;
        VerifyText = null;
        ReportPath = null;
        ReportText = null;
        ProgressText = SetupTexts.ProgressStarting;
        var request = BuildRequest();
        try
        {
            try
            {
                var progress = new Progress<string>(text => ProgressText = text);
                var results = await Task.Run(() => _engine.Apply(request, progress));
                foreach (var r in results)
                {
                    Results.Add(new ResultLine(r.Text, r.Success, r.Detail));
                }
            }
            catch (Exception ex)
            {
                Results.Add(new ResultLine(SetupTexts.Format(SetupTexts.ApplyFailedFormat, ex.Message), false, null));
            }
            finally
            {
                request.OperatorPassword?.Dispose();
            }

            ProgressText = SetupTexts.ProgressVerifying;
            try
            {
                var report = await Task.Run(_engine.Verify);
                Report = report;
                VerifyText = SetupTexts.VerifySummary(report.Checks.Count - report.Deviations, report.Checks.Count);
                try
                {
                    ReportPath = await Task.Run(() => _engine.WriteReport(report));
                    ReportText = SetupTexts.Format(SetupTexts.ReportPathFormat, ReportPath);
                }
                catch (Exception ex)
                {
                    ReportText = SetupTexts.Format(SetupTexts.ReportWriteFailedFormat, ex.Message);
                }
            }
            catch (Exception ex)
            {
                VerifyText = SetupTexts.Format(SetupTexts.VerifyErrorFormat, ex.Message);
            }
        }
        finally
        {
            ProgressText = string.Empty;
            IsBusy = false;
            IsFinished = true;
            UpdateSteps();
            OnPropertyChanged(nameof(AllStepsSucceeded));
            OnPropertyChanged(nameof(VerifyPassed));
            OnPropertyChanged(nameof(ExitCode));
        }
    }

    [RelayCommand(CanExecute = nameof(HasReportPath))]
    private void OpenReport()
    {
        if (ReportPath is null)
        {
            return;
        }

        try
        {
            _ui.OpenWithShell(ReportPath);
        }
        catch (Exception ex)
        {
            ReportText = SetupTexts.Format(SetupTexts.OpenReportFailedFormat, ex.Message);
        }
    }

    public void Dispose()
    {
        _password?.Dispose();
        _passwordConfirm?.Dispose();
        _password = null;
        _passwordConfirm = null;
    }
}
