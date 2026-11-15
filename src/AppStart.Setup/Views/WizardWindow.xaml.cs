using System.ComponentModel;
using System.Windows;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>Hosts the wizard; closes on the view model's request and refuses to close while the engine works.</summary>
public partial class WizardWindow : Window
{
    private readonly WizardViewModel _viewModel;
    private int? _exitCode;

    public WizardWindow(WizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, code) =>
        {
            _exitCode = code;
            Close();
        };
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    /// <summary>Exit code after the window closed (X before the end counts as cancel).</summary>
    public int ExitCode => _exitCode ?? _viewModel.ExitCode;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.CanClose)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
