using System.Windows;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>Dialog «Erlaubte Hilfsprogramme»; DialogResult true after «Übernehmen».</summary>
public partial class HelpersWindow : Window
{
    public HelpersWindow(HelpersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, accepted) => DialogResult = accepted;
    }
}
