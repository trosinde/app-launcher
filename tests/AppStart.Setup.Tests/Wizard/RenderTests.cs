using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using AppStart.Setup.ViewModels;
using AppStart.Setup.Views;

namespace AppStart.Setup.Tests.Wizard;

/// <summary>
/// Renders the wizard steps and the verify view with a fake engine to PNG (%USERPROFILE%\AppStartTests\wizard-*.png)
/// for the visual comparison with docs/ui-designs. Windows are shown off-screen, never activated.
/// </summary>
public sealed class RenderTests
{
    private const string Skip = TestPlatform.WindowsOnly;

    private static void Offscreen(Window window, FrameworkElement content, double width, double height)
    {
        content.Width = width;
        content.Height = height;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    private static FakeSetupEngine DesignEngine()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return new FakeSetupEngine
        {
            Snapshot = new SetupSnapshot(
                [
                    FakeSetupEngine.Entry("Applikation A", Path.Combine(system, "notepad.exe"), IntegrityCheck.Signature,
                        @"C:\ProgramData\Hersteller\AppA", @"HKLM\SOFTWARE\Hersteller\AppA"),
                    FakeSetupEngine.Entry("Applikation B", Path.Combine(system, "calc.exe"), IntegrityCheck.Sha256),
                ],
                [@"LABOR\bediener01", @"LABOR\bediener02"],
                AppAccountExists: false,
                OperatorAccountExists: false,
                ServiceRunning: false),
        };
    }

    private static async Task<(WizardWindow Window, WizardView View, WizardViewModel Vm)> OpenWizardAsync(FakeSetupEngine engine)
    {
        var vm = new WizardViewModel(engine, new FakeUi());
        var window = new WizardWindow(vm);
        var view = (WizardView)window.Content;
        Offscreen(window, view, 960, 600);
        window.Show();
        await WpfHost.SettleAsync();
        Assert.Equal(2, vm.Apps.Count); // InitializeAsync ran on Loaded
        vm.Apps.Add(new AppItemViewModel(
            new AppCandidate(@"C:\Program Files\AppStart\TestApp\AppStart.TestApp.exe", "AppStart TestApp", "appstart-testapp",
                IntegrityCheck.Signature, new string('C', 40), null, null, null),
            isSelected: false,
            isReleased: false));
        await WpfHost.SettleAsync();
        return (window, view, vm);
    }

    private static async Task FillPasswordsAsync(WizardView view)
    {
        ((PasswordBox)view.FindName("PasswordBox1")).Password = "Bediener-2026";
        ((PasswordBox)view.FindName("PasswordBox2")).Password = "Bediener-2026";
        await WpfHost.SettleAsync();
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = Skip)]
    public Task Wizard_AllStepsRender() => WpfHost.RunAsync(async () =>
    {
        var engine = DesignEngine();
        var (window, view, vm) = await OpenWizardAsync(engine);
        try
        {
            Assert.True(vm.IsAppsStep);
            var next = FindButton(view, SetupTexts.ButtonNext);
            Assert.True(next.IsEnabled);
            WpfHost.SavePng(view, "wizard-1-apps.png");

            // Invalid registry key disables «Weiter» in the real view as well.
            vm.SelectedApp!.RegistryKey = @"HKCU\Software";
            await WpfHost.SettleAsync();
            Assert.False(next.IsEnabled);
            WpfHost.SavePng(view, "wizard-1-apps-invalid.png");
            vm.SelectedApp.RegistryKey = @"HKLM\SOFTWARE\Hersteller\AppA";
            await WpfHost.SettleAsync();
            Assert.True(next.IsEnabled);

            await vm.NextCommand.ExecuteAsync(null);
            vm.CreateOperatorAccount = true;
            await WpfHost.SettleAsync();
            Assert.False(next.IsEnabled); // password missing
            await FillPasswordsAsync(view);
            Assert.True(next.IsEnabled);
            WpfHost.SavePng(view, "wizard-2-accounts.png");

            await vm.NextCommand.ExecuteAsync(null);
            await WpfHost.SettleAsync();
            Assert.True(vm.IsFinished);
            Assert.Equal("Bediener-2026", engine.LastPassword);
            WpfHost.SavePng(view, "wizard-3-summary.png");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = Skip)]
    public Task Wizard_SummaryWithDeviationRenders() => WpfHost.RunAsync(async () =>
    {
        var engine = DesignEngine();
        engine.Snapshot = engine.Snapshot with { OperatorAccountExists = true, AppAccountExists = true };
        engine.Report = FakeSetupEngine.CreateReport(deviations: 1);
        engine.ApplyResults[3] = new SetupStepResult("Verknüpfungen anlegen", false, "Zugriff auf C:\\Users\\Public\\Desktop verweigert");
        var (window, view, vm) = await OpenWizardAsync(engine);
        try
        {
            await vm.NextCommand.ExecuteAsync(null);
            await WpfHost.SettleAsync();
            Assert.False(vm.ShowCreateOperator);
            WpfHost.SavePng(view, "wizard-2-accounts-existing.png");
            await vm.NextCommand.ExecuteAsync(null);
            await WpfHost.SettleAsync();
            Assert.Equal(WizardViewModel.ExitFailed, vm.ExitCode);
            WpfHost.SavePng(view, "wizard-3-summary-deviation.png");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = Skip)]
    public Task Verify_Renders() => WpfHost.RunAsync(async () =>
    {
        var engine = new FakeSetupEngine { Report = FakeSetupEngine.CreateReport(deviations: 1) };
        var vm = new VerifyViewModel(engine, new FakeUi());
        var window = new VerifyWindow(vm);
        var view = (VerifyView)window.Content;
        Offscreen(window, view, 960, 780);
        window.Show();
        try
        {
            await WpfHost.SettleAsync();
            Assert.True(vm.HasReport);
            Assert.Equal(8, vm.Rows.Count);
            WpfHost.SavePng(view, "wizard-verify.png");

            engine.Report = FakeSetupEngine.CreateReport(deviations: 0);
            await vm.RepeatCommand.ExecuteAsync(null);
            await WpfHost.SettleAsync();
            WpfHost.SavePng(view, "wizard-verify-passed.png");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = Skip)]
    public Task PrintDocument_Builds() => WpfHost.RunAsync(() =>
    {
        var doc = ReportDocument.Build(FakeSetupEngine.CreateReport(deviations: 2));
        Assert.True(doc.Blocks.Count >= 5);
        return Task.CompletedTask;
    });

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = Skip)]
    public Task ExeIcon_IsExtracted() => WpfHost.RunAsync(() =>
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        Assert.NotNull(ExeIconConverter.Load(notepad));
        Assert.Null(ExeIconConverter.Load(@"C:\does\not\exist.exe"));
        return Task.CompletedTask;
    });

    private static Button FindButton(DependencyObject root, string content) =>
        TryFind(root, content) ?? throw new InvalidOperationException($"Button «{content}» not found");

    private static Button? TryFind(DependencyObject root, string content)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button { Content: string text, IsVisible: true } button && text == content)
            {
                return button;
            }

            if (TryFind(child, content) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
