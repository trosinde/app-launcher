using System.IO;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppStart.Setup.ViewModels;

/// <summary>One application row of wizard step 1 with its protection settings.</summary>
public sealed partial class AppItemViewModel : ObservableObject
{
    public AppItemViewModel(AppCandidate candidate, bool isSelected, bool isReleased)
    {
        Candidate = candidate;
        _isSelected = isSelected;
        IsReleased = isReleased;
        _dataDir = candidate.DataDir ?? string.Empty;
        _registryKey = candidate.RegistryKey ?? string.Empty;
    }

    public static AppItemViewModel FromEntry(AppEntry entry) => new(
        new AppCandidate(entry.Exe, entry.Name, entry.Id, entry.Check, entry.SignerThumbprint, entry.Sha256,
            entry.DataDir, entry.RegistryKey, entry.ChildProcessMode, entry.AllowedChildren),
        isSelected: true,
        isReleased: true);

    public AppCandidate Candidate { get; }

    /// <summary>Already released in apps.json (pre-filled from LoadCurrent).</summary>
    public bool IsReleased { get; }

    public string Name => Candidate.Name;

    public string ExePath => Candidate.ExePath;

    public string CheckText => SetupTexts.CheckText(Candidate.Check);

    public string ProtectionHeading => SetupTexts.Format(SetupTexts.ProtectionHeadingFormat, Name);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataDirError), nameof(IsValid))]
    private string _dataDir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegistryKeyError), nameof(IsValid))]
    private string _registryKey;

    /// <summary>Validation message for the data folder, null when valid (empty is allowed: no folder to protect).</summary>
    public string? DataDirError => IsValidDataDir(DataDir) ? null : SetupTexts.DataDirInvalid;

    /// <summary>Validation message for the registry key, null when valid (empty is allowed).</summary>
    public string? RegistryKeyError => IsValidRegistryKey(RegistryKey) ? null : SetupTexts.RegistryKeyInvalid;

    public bool IsValid => DataDirError is null && RegistryKeyError is null;

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
    };
}
