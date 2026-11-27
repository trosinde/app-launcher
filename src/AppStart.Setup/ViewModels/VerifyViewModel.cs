using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStart.Setup.ViewModels;

/// <summary>One row of the verify table: check point, target (or findings on deviation), result badge.</summary>
public sealed record CheckRow(string Title, string Text, bool IsDeviation)
{
    public static CheckRow From(CheckResult check)
    {
        var deviation = check.Outcome == CheckOutcome.Deviation;
        // Passed checks may carry notes (e.g. "All other programs allowed"): shown below the expected state.
        var text = check.Findings.Count == 0 ? check.Expected
            : deviation ? string.Join(Environment.NewLine, check.Findings)
            : check.Expected + Environment.NewLine + string.Join(Environment.NewLine, check.Findings);
        return new CheckRow(check.Title, text, deviation);
    }

    public string ResultText => IsDeviation ? SetupTexts.ResultDeviation : SetupTexts.ResultPassed;
}

/// <summary>
/// Verify view (task 9.1, Start menu "AppStart – Verify setup"): runs verify, shows the result per
/// check point, saves the report automatically, offers print and "save as".
/// </summary>
public sealed partial class VerifyViewModel(ISetupEngine engine, IUiServices ui) : ObservableObject
{
    /// <summary>Raised when the window should close; the argument is the exit code.</summary>
    public event EventHandler<int>? CloseRequested;

    public ObservableCollection<CheckRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RepeatCommand), nameof(CloseCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(Computer), nameof(Date), nameof(Technician), nameof(Version),
        nameof(DeviationBadge), nameof(HasDeviations), nameof(IsPassed), nameof(PassedText), nameof(ExitCode))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private VerifyReport? _report;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    private string? _reportPath;

    /// <summary>Status line: report saved / copied / errors.</summary>
    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _errorText;

    public bool HasReport => Report is not null;

    public string Computer => Report?.Computer ?? string.Empty;

    public string Date => Report?.CreatedAt.ToLocalTime().ToString(SetupTexts.DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    public string Technician => Report?.Technician ?? string.Empty;

    public string Version => Report is null ? string.Empty : SetupTexts.Format(SetupTexts.VersionFormat, Report.Version);

    public string DeviationBadge => Report is null ? string.Empty : SetupTexts.DeviationBadge(Report.Deviations);

    public bool HasDeviations => Report is { Deviations: > 0 };

    public bool IsPassed => Report is { Deviations: 0 };

    public string PassedText => Report is null ? string.Empty : SetupTexts.Format(SetupTexts.PassedBoxFormat, Report.Checks.Count);

    /// <summary>Exit code of the verify run (0 only when every check passed; 1 when verify failed to run).</summary>
    public int ExitCode => Report?.ExitCode ?? 1;

    public async Task RunAsync()
    {
        IsBusy = true;
        ErrorText = null;
        StatusText = SetupTexts.VerifyRunning;
        try
        {
            var report = await Task.Run(engine.Verify);
            Rows.Clear();
            foreach (var check in report.Checks)
            {
                Rows.Add(CheckRow.From(check));
            }

            Report = report;
            StatusText = null;
            try
            {
                ReportPath = await Task.Run(() => engine.WriteReport(report));
                StatusText = SetupTexts.Format(SetupTexts.ReportSavedFormat, ReportPath);
            }
            catch (Exception ex)
            {
                ReportPath = null;
                ErrorText = SetupTexts.Format(SetupTexts.ReportWriteFailedFormat, ex.Message);
            }
        }
        catch (Exception ex)
        {
            StatusText = null;
            ErrorText = SetupTexts.Format(SetupTexts.VerifyErrorFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRepeat => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private Task RepeatAsync() => RunAsync();

    [RelayCommand(CanExecute = nameof(HasReport))]
    private void Print()
    {
        if (Report is null)
        {
            return;
        }

        try
        {
            ui.PrintReport(Report);
        }
        catch (Exception ex)
        {
            ErrorText = SetupTexts.Format(SetupTexts.PrintFailedFormat, ex.Message);
        }
    }

    private bool CanSaveAs => ReportPath is not null;

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private void SaveAs()
    {
        if (ReportPath is null)
        {
            return;
        }

        var target = ui.PickReportSavePath(Path.GetFileName(ReportPath));
        if (target is null)
        {
            return;
        }

        try
        {
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(ReportPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(ReportPath, target, overwrite: true);
            }

            ErrorText = null;
            StatusText = SetupTexts.Format(SetupTexts.ReportCopiedFormat, target);
        }
        catch (Exception ex)
        {
            ErrorText = SetupTexts.Format(SetupTexts.SaveAsFailedFormat, ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private void Close() => CloseRequested?.Invoke(this, ExitCode);
}
