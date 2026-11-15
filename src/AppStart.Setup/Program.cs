namespace AppStart.Setup;

/// <summary>
/// Entry point: with arguments the command line runs (silent installation, verify for the service
/// technician); without arguments the wizard opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args) => args.Length > 0
        ? Cli.CommandLine.Run(args)
        : Views.WizardLauncher.ShowWizard(Integration.SetupComposition.CreateEngine());
}
