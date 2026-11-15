using System.Collections.ObjectModel;
using System.IO;
using AppStart.Core.Configuration;
using AppStart.Core.Learning;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStart.Setup.ViewModels;

/// <summary>One proposal of the learn mode.</summary>
public sealed partial class LearnProposal : ObservableObject
{
    public LearnProposal(RecordedProgram program, bool selectable, string hint, bool hintIsWarning, bool isChecked)
    {
        Program = program;
        IsSelectable = selectable;
        Hint = hint;
        HintIsWarning = hintIsWarning;
        _isChecked = selectable && isChecked;
    }

    public RecordedProgram Program { get; }

    public string FileName => Path.GetFileName(Program.Exe);

    public string Exe => Program.Exe;

    public string CheckText => SetupTexts.CheckText(Program.SuggestedCheck);

    /// <summary>Shells and script hosts, and helpers in operator-writable folders, cannot be taken (design D3).</summary>
    public bool IsSelectable { get; }

    public bool IsBlocked => !IsSelectable;

    public string Hint { get; }

    public bool HintIsWarning { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// Dialog «Vorschläge übernehmen» (docs/ui-designs/LearnResult, spec installation «Shell im Lernergebnis»).
/// Both buttons delete the recording; «übernehmen» hands the chosen entries to the helpers dialog.
/// </summary>
public sealed partial class LearnResultViewModel : ObservableObject
{
    // Programs that can open and save arbitrary files with admin rights: proposed, but not pre-selected.
    private static readonly HashSet<string> OpensFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "acrord32.exe", "acrobat.exe", "msedge.exe", "chrome.exe", "firefox.exe", "iexplore.exe",
        "notepad.exe", "wordpad.exe", "write.exe", "mspaint.exe", "winword.exe", "excel.exe",
    };

    private readonly ISetupEngine _engine;
    private readonly string _appId;

    public LearnResultViewModel(ISetupEngine engine, string appId, string appName, LearnRecording? recording)
    {
        _engine = engine;
        _appId = appId;
        Intro = SetupTexts.Format(SetupTexts.LearnResultIntroFormat, appName);
        Title = SetupTexts.Format(SetupTexts.LearnTitleFormat, appName);
        foreach (var program in recording?.Programs.OrderBy(p => p.FirstStartUtc) ?? Enumerable.Empty<RecordedProgram>())
        {
            var proposal = ToProposal(program);
            proposal.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TakeOverText));
            Proposals.Add(proposal);
        }
    }

    public event EventHandler<bool>? CloseRequested;

    public string Title { get; }

    public string Intro { get; }

    public ObservableCollection<LearnProposal> Proposals { get; } = [];

    public bool IsEmpty => Proposals.Count == 0;

    public string TakeOverText => SetupTexts.Format(SetupTexts.ButtonTakeOverFormat, Proposals.Count(p => p.IsChecked));

    /// <summary>The chosen entries after «übernehmen»; null after «Verwerfen».</summary>
    public IReadOnlyList<AllowedChild>? Selected { get; private set; }

    [RelayCommand]
    private void TakeOver()
    {
        Selected = [.. Proposals.Where(p => p.IsChecked && p.IsSelectable).Select(p => p.Program.ToAllowedChild())];
        Done(true);
    }

    [RelayCommand]
    private void Discard() => Done(false);

    private void Done(bool accepted)
    {
        try
        {
            _engine.DiscardRecording(_appId);
        }
        catch (IOException)
        {
        }

        CloseRequested?.Invoke(this, accepted);
    }

    private LearnProposal ToProposal(RecordedProgram program)
    {
        if (program.IsShellOrScriptHost)
        {
            var cmd = string.Equals(Path.GetFileName(program.Exe), "cmd.exe", StringComparison.OrdinalIgnoreCase);
            return new LearnProposal(program, false, cmd ? SetupTexts.LearnHintCmd : SetupTexts.LearnHintShell, true, false);
        }

        if (SafeWriteViolations(program.Exe))
        {
            return new LearnProposal(program, false, SetupTexts.LearnHintWritable, true, false);
        }

        if (OpensFiles.Contains(Path.GetFileName(program.Exe)))
        {
            return new LearnProposal(program, true, SetupTexts.LearnHintOpensFiles, true, false);
        }

        return program.SignatureValid
            ? new LearnProposal(program, true, "–", false, true)
            : new LearnProposal(program, true, SetupTexts.LearnHintUnsigned, true, true);
    }

    private bool SafeWriteViolations(string exe)
    {
        try
        {
            return _engine.WriteViolations(exe).Count > 0;
        }
        catch (Exception)
        {
            return true; // fail-closed: not selectable
        }
    }
}
