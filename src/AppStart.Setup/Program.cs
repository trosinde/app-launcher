using System.Windows;

namespace AppStart.Setup;

/// <summary>
/// Entry point: with arguments the command line runs (silent installation, verify for the service
/// technician); without arguments the wizard opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return Cli.CommandLine.Run(args);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
