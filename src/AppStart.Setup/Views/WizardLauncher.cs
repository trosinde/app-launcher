using System.Windows;
using AppStart.Setup.Engine;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>
/// Entry points for Program.cs: open the wizard or the verify view and return the exit code.
/// Creates the WPF <see cref="App"/> (Fluent light theme, Views/Styles.xaml) when none exists and runs on
/// an STA thread (a new one when the caller is not STA).
/// </summary>
public static class WizardLauncher
{
    /// <summary>Wizard: 0 = set up and verify passed, 1 = errors or deviations, 2 = cancelled.</summary>
    public static int ShowWizard(ISetupEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return RunOnSta(() =>
        {
            var ui = new UiServices();
            var window = new WizardWindow(new WizardViewModel(engine, ui));
            ui.Owner = window;
            ShowWindow(window);
            return window.ExitCode;
        });
    }

    /// <summary>Verify view (Start menu "AppStart – Verify setup"): 0 = all checks passed, otherwise 1.</summary>
    public static int ShowVerify(ISetupEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return RunOnSta(() =>
        {
            var ui = new UiServices();
            var window = new VerifyWindow(new VerifyViewModel(engine, ui));
            ui.Owner = window;
            ShowWindow(window);
            return window.ExitCode;
        });
    }

    /// <summary>Makes sure the App with its resources exists (tests and both entry points share it).</summary>
    internal static Application EnsureApplication()
    {
        if (Application.Current is { } current)
        {
            return current;
        }

        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return app;
    }

    /// <summary>ShowDialog runs its own message loop, so no Application.Run is needed and both views can open in turn.</summary>
    private static void ShowWindow(Window window) => window.ShowDialog();

    private static int RunOnSta(Func<int> show)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            EnsureApplication();
            return show();
        }

        var result = 1;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                result = show();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException(error.Message, error);
        }

        return result;
    }
}
