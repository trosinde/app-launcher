using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>Dialog "Learning mode running": refreshes countdown and recording every 2 s; closing counts as "Cancel".</summary>
public partial class LearnWindow : Window
{
    private readonly LearnViewModel _viewModel;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public LearnWindow(LearnViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
        _timer.Tick += (_, _) => viewModel.Refresh();
        Loaded += (_, _) => _timer.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.Outcome == LearnOutcome.Running)
        {
            _viewModel.CancelCommand.Execute(null); // learn mode never stays on by accident
        }

        _timer.Stop();
        base.OnClosing(e);
    }
}
