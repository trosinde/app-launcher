using CommunityToolkit.Mvvm.ComponentModel;

namespace AppStart.Setup.ViewModels;

/// <summary>One entry of the left navigation (active highlighted, done with ✓).</summary>
public sealed partial class StepItem(int number, string title) : ObservableObject
{
    public int Number { get; } = number;

    public string Title { get; } = title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isDone;

    public string DisplayText => $"{Number}  {Title}{(IsDone ? SetupTexts.NavDoneMark : string.Empty)}";
}

/// <summary>A chip in "Weitere Mitglieder": existing members cannot be removed here (Apply does not remove them).</summary>
public sealed record MemberItem(string Account, bool IsNew);

/// <summary>One result line of step 3 (✓ or ✗).</summary>
public sealed record ResultLine(string Text, bool Success, string? Detail)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}
