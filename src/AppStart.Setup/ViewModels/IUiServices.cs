using AppStart.Setup.Engine;

namespace AppStart.Setup.ViewModels;

/// <summary>
/// Everything the view models need from the UI layer (dialogs, shell, printer). The WPF implementation
/// lives in Views/UiServices.cs; tests use a fake.
/// </summary>
public interface IUiServices
{
    /// <summary>OpenFileDialog, multi-select *.exe. Empty when cancelled.</summary>
    IReadOnlyList<string> PickExeFiles();

    /// <summary>OpenFolderDialog. Null when cancelled.</summary>
    string? PickFolder(string? initialFolder);

    /// <summary>SaveFileDialog for the HTML report. Null when cancelled.</summary>
    string? PickReportSavePath(string defaultFileName);

    /// <summary>Opens a file with its associated program (shell execute).</summary>
    void OpenWithShell(string path);

    /// <summary>Shows the print dialog and prints the report. False when the user cancelled.</summary>
    bool PrintReport(VerifyReport report);

    /// <summary>Opens the verify view as a dialog (button "Verify setup" in the wizard, task 9.2).</summary>
    void ShowVerifyView(ISetupEngine engine);

    /// <summary>Warning with "Yes"/"No"; true only for "Yes" (add-allowed-child-processes: "All allowed", shells).</summary>
    bool Confirm(string heading, string text);

    /// <summary>Dialog "Allowed helper programs"; true after "Apply".</summary>
    bool ShowHelpers(HelpersViewModel viewModel);

    /// <summary>Dialog "Learning mode running" (modal until finished or cancelled).</summary>
    void ShowLearn(LearnViewModel viewModel);

    /// <summary>Dialog "Apply suggestions"; true after "Apply … programs".</summary>
    bool ShowLearnResult(LearnResultViewModel viewModel);
}
