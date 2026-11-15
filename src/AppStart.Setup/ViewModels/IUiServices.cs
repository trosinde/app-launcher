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

    /// <summary>Opens the verify view as a dialog (button «Einrichtung prüfen» in the wizard, task 9.2).</summary>
    void ShowVerifyView(ISetupEngine engine);

    /// <summary>Warning with «Ja»/«Nein»; true only for «Ja» (add-allowed-child-processes: «Alle erlaubt», shells).</summary>
    bool Confirm(string heading, string text);

    /// <summary>Dialog «Erlaubte Hilfsprogramme»; true after «Übernehmen».</summary>
    bool ShowHelpers(HelpersViewModel viewModel);

    /// <summary>Dialog «Lernmodus läuft» (modal until finished or cancelled).</summary>
    void ShowLearn(LearnViewModel viewModel);

    /// <summary>Dialog «Vorschläge übernehmen»; true after «… Programme übernehmen».</summary>
    bool ShowLearnResult(LearnResultViewModel viewModel);
}
