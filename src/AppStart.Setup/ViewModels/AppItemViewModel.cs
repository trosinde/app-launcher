using System.IO;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppStart.Setup.ViewModels;

/// <summary>One application row of wizard step 1 with its protection settings.</summary>
public sealed partial class AppItemViewModel : ObservableObject
{
    private ChildProcessMode _childMode;
    private IReadOnlyList<AllowedChild> _allowedChildren;

    public AppItemViewModel(AppCandidate candidate, bool isSelected, bool isReleased)
    {
        Candidate = candidate;
        _isSelected = isSelected;
        IsReleased = isReleased;
        _dataDir = candidate.DataDir ?? string.Empty;
        _registryKey = candidate.RegistryKey ?? string.Empty;
        _childMode = candidate.ChildProcesses;
        _allowedChildren = candidate.AllowedChildren ?? [];
    }

    public static AppItemViewModel FromEntry(AppEntry entry) => new(
        new AppCandidate(entry.Exe, entry.Name, entry.Id, entry.Check, entry.SignerThumbprint, entry.Sha256,
            entry.DataDir, entry.RegistryKey, entry.ChildProcessMode, entry.AllowedChildren),
        isSelected: true,
        isReleased: true);

    public AppCandidate Candidate { get; }

    /// <summary>Already released in apps.json (pre-filled from LoadCurrent).</summary>
    public bool IsReleased { get; }

    public string Id => Candidate.Id;

    public string Name => Candidate.Name;

    public string ExePath => Candidate.ExePath;

    public string CheckText => SetupTexts.CheckText(Candidate.Check);

    public string ProtectionHeading => SetupTexts.Format(SetupTexts.ProtectionHeadingFormat, Name);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataDirError), nameof(DataDirInProgramFolder), nameof(IsValid))]
    private string _dataDir;

    // A new folder needs a new confirmation.
    partial void OnDataDirChanged(string value) => DataDirRiskAccepted = false;

    /// <summary>spec fix-wizard-findings "Warning about data folder in the program folder".</summary>
    public bool DataDirInProgramFolder => IsValidDataDir(DataDir) && DataDirPolicy.OverlapsProgramFolder(DataDir, ExePath);

    /// <summary>Checkbox "I understand the risk …"; "Next" stays disabled while the warning is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private bool _dataDirRiskAccepted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegistryKeyError), nameof(IsValid))]
    private string _registryKey;

    // ------------------------------------------------------------------ further programs (add-allowed-child-processes)

    /// <summary>Asked before "All allowed (unsafe)" is taken (wizard sets it; false keeps the old mode).</summary>
    public Func<AppItemViewModel, bool>? ConfirmAllowAll { get; set; }

    public IReadOnlyList<string> ChildModeOptions { get; } = [SetupTexts.ChildModeBlocked, SetupTexts.ChildModeAllowlist, SetupTexts.ChildModeAll];

    public ChildProcessMode ChildMode
    {
        get => _childMode;
        set
        {
            if (value == _childMode)
            {
                return;
            }

            if (value == ChildProcessMode.All && ConfirmAllowAll is { } confirm && !confirm(this))
            {
                OnPropertyChanged(nameof(ChildModeIndex)); // the combo box shows the old mode again
                return;
            }

            _childMode = value;
            OnChildrenChanged();
        }
    }

    /// <summary>Index into <see cref="ChildModeOptions"/> (blocked, allowlist, all) for the combo box.</summary>
    public int ChildModeIndex
    {
        get => (int)ChildMode;
        set => ChildMode = (ChildProcessMode)Math.Clamp(value, 0, 2);
    }

    public IReadOnlyList<AllowedChild> AllowedChildren
    {
        get => _allowedChildren;
        set
        {
            _allowedChildren = value;
            OnChildrenChanged();
        }
    }

    public bool IsAllowlist => ChildMode == ChildProcessMode.Allowlist;

    /// <summary>Column "Other programs" of the list.</summary>
    public string ChildSummary => ChildMode switch
    {
        ChildProcessMode.Allowlist => SetupTexts.Format(SetupTexts.ChildSummaryAllowlistFormat, AllowedChildren.Count),
        ChildProcessMode.All => SetupTexts.ChildSummaryAll,
        _ => SetupTexts.ChildSummaryBlocked,
    };

    public string HelpersButtonText => SetupTexts.Format(SetupTexts.ButtonHelpersFormat, AllowedChildren.Count);

    public string ChildHint => ChildMode switch
    {
        ChildProcessMode.Allowlist => SetupTexts.Format(SetupTexts.ChildHintAllowlistFormat, Name),
        ChildProcessMode.All => SetupTexts.Format(SetupTexts.ChildHintAllFormat, Name),
        _ => SetupTexts.Format(SetupTexts.ChildHintBlocked, Name),
    };

    /// <summary>allowlist needs at least one entry (design D1).</summary>
    public string? ChildError => IsAllowlist && AllowedChildren.Count == 0 ? SetupTexts.ChildAllowlistEmpty : null;

    private void OnChildrenChanged()
    {
        OnPropertyChanged(nameof(ChildMode));
        OnPropertyChanged(nameof(ChildModeIndex));
        OnPropertyChanged(nameof(AllowedChildren));
        OnPropertyChanged(nameof(IsAllowlist));
        OnPropertyChanged(nameof(ChildSummary));
        OnPropertyChanged(nameof(HelpersButtonText));
        OnPropertyChanged(nameof(ChildHint));
        OnPropertyChanged(nameof(ChildError));
        OnPropertyChanged(nameof(IsValid));
    }

    // ------------------------------------------------------------------ validation

    /// <summary>Validation message for the data folder, null when valid (empty is allowed: no folder to protect).</summary>
    public string? DataDirError => IsValidDataDir(DataDir) ? null : SetupTexts.DataDirInvalid;

    /// <summary>Validation message for the registry key, null when valid (empty is allowed).</summary>
    public string? RegistryKeyError => IsValidRegistryKey(RegistryKey) ? null : SetupTexts.RegistryKeyInvalid;

    public bool IsValid => DataDirError is null && RegistryKeyError is null && ChildError is null
        && (!DataDirInProgramFolder || DataDirRiskAccepted);

    public static bool IsValidRegistryKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        key = key.Trim();
        return key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)
            && key.Length > 5
            && !key.Contains(@"\\", StringComparison.Ordinal);
    }

    public static bool IsValidDataDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        path = path.Trim();
        return Path.IsPathFullyQualified(path)
            && path.IndexOfAny(Path.GetInvalidPathChars()) < 0
            && !path.StartsWith(@"\\", StringComparison.Ordinal);
    }

    /// <summary>The candidate with the edited protection settings (empty → null).</summary>
    public AppCandidate ToCandidate() => Candidate with
    {
        DataDir = string.IsNullOrWhiteSpace(DataDir) ? null : DataDir.Trim(),
        RegistryKey = string.IsNullOrWhiteSpace(RegistryKey) ? null : RegistryKey.Trim(),
        ChildProcesses = ChildMode,
        AllowedChildren = ChildMode == ChildProcessMode.Allowlist ? AllowedChildren : null,
    };
}
