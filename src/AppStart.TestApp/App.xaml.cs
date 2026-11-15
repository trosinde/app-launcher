using System.Windows;
using AppStart.TestApp.Services;
using AppStart.TestApp.ViewModels;

namespace AppStart.TestApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var env = new TestEnvironment();

        // The manifest already demands elevation. This check also covers bypasses such as
        // __COMPAT_LAYER=RunAsInvoker: without admin rights the app stops, like the real fat clients.
        if (!env.IsElevatedAdministrator)
        {
            MessageBox.Show(Texts.NoAdminMessage, Texts.NoAdminTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel(env) };
        MainWindow = window;
        window.Show();
    }
}
