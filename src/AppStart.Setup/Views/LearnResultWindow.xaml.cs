using System.Windows;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>Dialog "Apply suggestions"; DialogResult true after "Apply … programs".</summary>
public partial class LearnResultWindow : Window
{
    public LearnResultWindow(LearnResultViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, accepted) => DialogResult = accepted;
    }
}
