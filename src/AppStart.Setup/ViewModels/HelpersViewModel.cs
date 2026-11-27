using System.Collections.ObjectModel;
using System.IO;
using AppStart.Core.Configuration;
using AppStart.Core.Learning;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppStart.Setup.ViewModels;

/// <summary>One allowed helper in the dialog "Allowed helper programs".</summary>
public sealed partial class HelperItem(AllowedChild entry, string state, bool stateOk) : ObservableObject
{
    public AllowedChild Entry { get; } = entry;

    public string FileName => Path.GetFileName(Entry.Exe);

    public string Exe => Entry.Exe;

    public string CheckText => SetupTexts.CheckText(Entry.Check);

    public string State { get; } = state;

    public bool StateOk { get; } = stateOk;

    public bool IsUnsigned => Entry.Check == IntegrityCheck.Sha256;

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// Dialog "Allowed helper programs" (docs/ui-designs/Helpers, spec installation "Other programs in the wizard"):
/// add, remove, renew checksum, learn mode. Changes go back to the application row only with "Apply".
/// </summary>
public sealed partial class HelpersViewModel : ObservableObject
{
    private readonly ISetupEngine _engine;
    private readonly AppItemViewModel _app;
    private IUiServices _ui;

    public HelpersViewModel(ISetupEngine engine, IUiServices ui, AppItemViewModel app)
    {
        _engine = engine;
        _ui = ui;
        _app = app;
        foreach (var child in app.AllowedChildren)
        {
            Items.Add(ToItem(child));
        }

        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(UnsignedWarning));
    }

    public event EventHandler<bool>? CloseRequested;

    /// <summary>Dialogs opened from this dialog are owned by its window (set by the view layer).</summary>
    public void SetUi(IUiServices ui) => _ui = ui;

    public string Title => SetupTexts.Format(SetupTexts.HelpersTitleFormat, _app.Name);

    public string Intro => SetupTexts.Format(SetupTexts.HelpersIntroFormat, _app.Name);

    public ObservableCollection<HelperItem> Items { get; } = [];

    /// <summary>Yellow box under the list: the first unsigned helper (design).</summary>
    public string? UnsignedWarning => Items.FirstOrDefault(i => i.IsUnsigned) is { } u ? SetupTexts.Format(SetupTexts.HelpersUnsignedFormat, u.FileName) : null;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>The list after "Apply" (null when cancelled).</summary>
    public IReadOnlyList<AllowedChild>? Result { get; private set; }

    [RelayCommand]
    private void AddExe()
    {
        ErrorMessage = null;
        var errors = new List<string>();
        foreach (var file in _ui.PickExeFiles())
        {
            try
            {
                var allowShell = ChildProcessRules.IsShellOrScriptHost(file)
                    && _ui.Confirm(SetupTexts.Format(SetupTexts.ConfirmShellHeadingFormat, Path.GetFileName(file)), SetupTexts.ConfirmShellText);
                if (ChildProcessRules.IsShellOrScriptHost(file) && !allowShell)
                {
                    continue;
                }

                if (_engine.WriteViolations(file).Count > 0)
                {
                    errors.Add(SetupTexts.Format(SetupTexts.HelperWritableFormat, Path.GetFileName(file)));
                    continue;
                }

                Upsert(_engine.InspectChild(file, allowShell));
            }
            catch (Exception ex)
            {
                errors.Add(SetupTexts.Format(SetupTexts.InspectFailedFormat, Path.GetFileName(file), ex.Message));
            }
        }

        ErrorMessage = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
    }

    [RelayCommand]
    private void Remove()
    {
        foreach (var item in Items.Where(i => i.IsChecked).ToList())
        {
            Items.Remove(item);
        }
    }

    /// <summary>"Update checksum" for the checked SHA-256 entries (all SHA-256 entries when none is checked).</summary>
    [RelayCommand]
    private void Rehash()
    {
        ErrorMessage = null;
        var targets = Items.Where(i => i.IsChecked).ToList();
        if (targets.Count == 0)
        {
            targets = [.. Items];
        }

        foreach (var item in targets.Where(i => i.IsUnsigned))
        {
            try
            {
                Upsert(_engine.RehashChild(item.Entry));
            }
            catch (Exception ex)
            {
                ErrorMessage = SetupTexts.Format(SetupTexts.InspectFailedFormat, item.FileName, ex.Message);
            }
        }
    }

    public bool CanLearn => _app.IsReleased;

    /// <summary>"Start learning mode…": learn dialog, then the proposals dialog; taken programs land in the list.</summary>
    [RelayCommand]
    private void StartLearning()
    {
        ErrorMessage = null;
        if (!_app.IsReleased)
        {
            ErrorMessage = SetupTexts.Format(SetupTexts.LearnNeedsReleaseFormat, _app.Name);
            return;
        }

        var learn = new LearnViewModel(_engine, _app.Id, _app.Name, TimeProvider.System);
        if (!learn.Start())
        {
            ErrorMessage = learn.ErrorMessage;
            return;
        }

        _ui.ShowLearn(learn);
        if (learn.Outcome != LearnOutcome.Finished)
        {
            return;
        }

        LearnRecording? recording;
        try
        {
            recording = _engine.GetRecording(_app.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        var result = new LearnResultViewModel(_engine, _app.Id, _app.Name, recording);
        if (_ui.ShowLearnResult(result) && result.Selected is { } selected)
        {
            foreach (var child in selected)
            {
                Upsert(child);
            }
        }
    }

    [RelayCommand]
    private void Apply()
    {
        Result = [.. Items.Select(i => i.Entry)];
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private void Upsert(AllowedChild entry)
    {
        var existing = Items.FirstOrDefault(i => ChildPrograms.SamePath(i.Exe, entry.Exe));
        var item = ToItem(entry);
        if (existing is null)
        {
            Items.Add(item);
        }
        else
        {
            Items[Items.IndexOf(existing)] = item;
        }
    }

    private HelperItem ToItem(AllowedChild entry)
    {
        try
        {
            var check = _engine.VerifyChild(entry);
            if (check.Success)
            {
                return new HelperItem(entry, SetupTexts.HelperStateOk, true);
            }

            return check.Detail == string.Format(SetupEngineTexts.ChildMissing, entry.Exe)
                ? new HelperItem(entry, SetupTexts.HelperStateMissing, false)
                : new HelperItem(entry, entry.Check == IntegrityCheck.Sha256 ? SetupTexts.HelperStateRenew : SetupTexts.HelperStateInvalid, false);
        }
        catch (Exception)
        {
            return new HelperItem(entry, SetupTexts.HelperStateInvalid, false);
        }
    }
}
