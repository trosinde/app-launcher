using System.IO;
using System.Collections.ObjectModel;
using AppStart.TestApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStart.TestApp.ViewModels;

public enum CheckState
{
    Pending,
    Ok,
    Warning,
    Failed,
}

/// <summary>One row of the status list (label, value, badge).</summary>
public sealed partial class StatusRow(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private CheckState _state = CheckState.Pending;

    [ObservableProperty]
    private string _badge = Texts.BadgePending;

    public void Set(string value, CheckState state, string badge)
    {
        Value = value;
        State = state;
        Badge = badge;
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string AppAccountName = "appstart-app";

    private readonly ITestEnvironment _env;

    public MainViewModel(ITestEnvironment env)
    {
        _env = env;
        TestValue = env.ReadStoredTestValue() ?? Texts.DefaultTestValue;
        Rows = [Account, Admin, Registry, Files, ChildProcess, Helper];
        ChildProcess.Set(Texts.ChildNotTested, CheckState.Pending, Texts.BadgePending);
        Helper.Set(Texts.ChildNotTested, CheckState.Pending, Texts.BadgePending);
        Recheck();
    }

    public StatusRow Account { get; } = new(Texts.RowAccount);

    public StatusRow Admin { get; } = new(Texts.RowAdmin);

    public StatusRow Registry { get; } = new(Texts.RowRegistry);

    public StatusRow Files { get; } = new(Texts.RowFiles);

    public StatusRow ChildProcess { get; } = new(Texts.RowChild);

    public StatusRow Helper { get; } = new(Texts.RowHelper);

    public ObservableCollection<StatusRow> Rows { get; }

    [ObservableProperty]
    private string _testValue;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True when the process runs as the AppStart application account.</summary>
    public bool RunsAsAppAccount =>
        _env.AccountName.EndsWith(@"\" + AppAccountName, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void Recheck()
    {
        Account.Set(_env.AccountName,
            RunsAsAppAccount ? CheckState.Ok : CheckState.Warning,
            RunsAsAppAccount ? Texts.BadgeOk : Texts.BadgeNotViaAppStart);

        Admin.Set(_env.IsElevatedAdministrator ? Texts.Yes : Texts.No,
            _env.IsElevatedAdministrator ? CheckState.Ok : CheckState.Failed,
            _env.IsElevatedAdministrator ? Texts.BadgeOk : Texts.BadgeError);

        SetFromResult(Registry, _env.CheckRegistry());
        SetFromResult(Files, _env.CheckFiles());
        StatusMessage = string.Format(Texts.CheckedAt, DateTime.Now);
    }

    [RelayCommand]
    public void Save()
    {
        try
        {
            _env.Save(TestValue);
            StatusMessage = string.Format(Texts.SavedAt, DateTime.Now);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StatusMessage = string.Format(Texts.SaveFailed, ex.Message);
        }

        SetFromResult(Registry, _env.CheckRegistry());
        SetFromResult(Files, _env.CheckFiles());
    }

    [RelayCommand]
    public void StartCommandPrompt()
    {
        var (outcome, detail) = _env.TryStartCommandPrompt();
        switch (outcome)
        {
            case ChildProcessOutcome.BlockedByJob:
                ChildProcess.Set(Texts.ChildBlocked, CheckState.Ok, Texts.BadgeOk);
                break;
            case ChildProcessOutcome.TerminatedByService:
                ChildProcess.Set(Texts.ChildTerminated, CheckState.Ok, Texts.BadgeOk);
                break;
            case ChildProcessOutcome.Started:
                ChildProcess.Set(Texts.ChildStarted, CheckState.Failed, Texts.BadgeBreakout);
                break;
            default:
                ChildProcess.Set(string.Format(Texts.ChildFailed, detail), CheckState.Warning, Texts.BadgeCheck);
                break;
        }
    }

    /// <summary>"Start allowed helper program": shows what the configured mode does with a helper (task 6.1).</summary>
    [RelayCommand]
    public void StartHelper()
    {
        var (outcome, detail) = _env.TryStartHelper();
        switch (outcome)
        {
            case ChildProcessOutcome.Ran:
                Helper.Set(string.Format(Texts.HelperRan, detail), CheckState.Ok, Texts.BadgeAllowed);
                break;
            case ChildProcessOutcome.BlockedByJob:
                Helper.Set(Texts.HelperBlocked, CheckState.Warning, Texts.BadgeBlocked);
                break;
            case ChildProcessOutcome.TerminatedByService:
                Helper.Set(Texts.HelperTerminated, CheckState.Warning, Texts.BadgeBlocked);
                break;
            default:
                Helper.Set(string.Format(Texts.ChildFailed, detail), CheckState.Failed, Texts.BadgeCheck);
                break;
        }
    }

    private static void SetFromResult(StatusRow row, CheckResult result) =>
        row.Set(result.Detail, result.Success ? CheckState.Ok : CheckState.Failed, result.Success ? Texts.BadgeOk : Texts.BadgeError);
}
