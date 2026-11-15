using System.ComponentModel;
using System.Windows;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>Hosts the verify view; starts the check when shown.</summary>
public partial class VerifyWindow : Window
{
    private readonly VerifyViewModel _viewModel;

    public VerifyWindow(VerifyViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Loaded += async (_, _) => await viewModel.RunAsync();
    }

    public int ExitCode => _viewModel.ExitCode;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
