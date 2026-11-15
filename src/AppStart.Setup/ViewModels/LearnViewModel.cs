using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStart.Setup.ViewModels;

public enum LearnOutcome
{
    Running,
    Cancelled,
    Finished,
}

/// <summary>One recorded program in the dialog «Lernmodus läuft».</summary>
public sealed record LearnProgramRow(string FileName, string Exe, int Starts, string Last);

/// <summary>
/// Dialog «Lernmodus läuft» (docs/ui-designs/Learn, design D3): writes learn.json, shows remaining time and what
/// the service records. «Lernmodus beenden» (or the time running out) leads to the proposals, «Abbrechen» discards.
/// </summary>
public sealed partial class LearnViewModel(ISetupEngine engine, string appId, string appName, TimeProvider time) : ObservableObject
{
    private DateTimeOffset _expires;

    public event EventHandler? CloseRequested;

    public string Title => SetupTexts.Format(SetupTexts.LearnTitleFormat, appName);

    public string Intro => SetupTexts.Format(SetupTexts.LearnIntroFormat, appName);

    public string WarningTitle => SetupTexts.Format(SetupTexts.LearnWarningTitleFormat, appName);

    public ObservableCollection<LearnProgramRow> Programs { get; } = [];

    public bool HasPrograms => Programs.Count > 0;

    [ObservableProperty]
    private string _remaining = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public LearnOutcome Outcome { get; private set; } = LearnOutcome.Running;

    /// <summary>Starts the learn mode (default duration); false with <see cref="ErrorMessage"/> when it failed.</summary>
    public bool Start(int minutes = Core.Learning.LearnState.DefaultMinutes)
    {
        try
        {
            var results = engine.StartLearning(appId, minutes);
            if (results.Any(r => !r.Success))
            {
                ErrorMessage = SetupTexts.Format(SetupTexts.LearnStartFailedFormat, string.Join(" ", results.Where(r => !r.Success).Select(r => r.Text + (r.Detail is null ? string.Empty : ": " + r.Detail))));
                return false;
            }

            _expires = time.GetUtcNow().AddMinutes(minutes); // the engine started it just now with the same duration
            Refresh();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = SetupTexts.Format(SetupTexts.LearnStartFailedFormat, ex.Message);
            return false;
        }
    }

    /// <summary>Called by the view every few seconds: countdown and recording; ends by itself when the time is up.</summary>
    public void Refresh()
    {
        if (Outcome != LearnOutcome.Running)
        {
            return;
        }

        var left = _expires - time.GetUtcNow();
        if (left <= TimeSpan.Zero)
        {
            Remaining = "00:00";
            Finish();
            return;
        }

        Remaining = ((int)left.TotalMinutes).ToString("00", CultureInfo.InvariantCulture) + ":" + left.Seconds.ToString("00", CultureInfo.InvariantCulture);
        try
        {
            var recording = engine.GetRecording(appId);
            Programs.Clear();
            foreach (var p in recording?.Programs.OrderBy(p => p.FirstStartUtc) ?? Enumerable.Empty<Core.Learning.RecordedProgram>())
            {
                Programs.Add(new LearnProgramRow(Path.GetFileName(p.Exe), p.Exe, p.Starts,
                    p.LastStartUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
            }

            OnPropertyChanged(nameof(HasPrograms));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Finish()
    {
        if (Outcome != LearnOutcome.Running)
        {
            return;
        }

        Outcome = LearnOutcome.Finished;
        TryStop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>«Abbrechen, nichts übernehmen» (also closing the window): learn mode off, recording deleted.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (Outcome != LearnOutcome.Running)
        {
            return;
        }

        Outcome = LearnOutcome.Cancelled;
        TryStop();
        try
        {
            engine.DiscardRecording(appId);
        }
        catch (IOException)
        {
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void TryStop()
    {
        try
        {
            engine.StopLearning(appId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message; // verify reports a learn mode that is still active
        }
    }
}
