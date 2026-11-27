using System.IO;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Tests.Wizard;

public sealed class VerifyViewModelTests : IDisposable
{
    private readonly FakeSetupEngine _engine = new();
    private readonly FakeUi _ui = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "appstart-verify-" + Guid.NewGuid().ToString("N"));

    public VerifyViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Run_ShowsRowsBadgeAndSavesReport()
    {
        _engine.Report = FakeSetupEngine.CreateReport(deviations: 1);
        var vm = new VerifyViewModel(_engine, _ui);
        await vm.RunAsync();

        Assert.Equal(8, vm.Rows.Count);
        Assert.Equal("1 deviation", vm.DeviationBadge);
        Assert.True(vm.HasDeviations);
        Assert.False(vm.IsPassed);
        var konten = vm.Rows[2];
        Assert.True(konten.IsDeviation);
        Assert.Equal("appstart-user is a member of Administrators", konten.Text);
        Assert.Equal("Deviation", konten.ResultText);
        Assert.Equal("Passed", vm.Rows[0].ResultText);
        Assert.Equal("running, starts automatically, LocalSystem", vm.Rows[0].Text);
        Assert.Equal("LAB-PC-014", vm.Computer);
        Assert.Equal("AppStart 0.1.0", vm.Version);
        Assert.Equal(1, _engine.WriteReportCalls);
        Assert.Equal(_engine.ReportPath, vm.ReportPath);
        Assert.Equal($"Report saved: {_engine.ReportPath}", vm.StatusText);
        Assert.Equal(1, vm.ExitCode);

        _engine.Report = FakeSetupEngine.CreateReport(deviations: 0);
        await vm.RepeatCommand.ExecuteAsync(null);
        Assert.Equal("No deviation", vm.DeviationBadge);
        Assert.True(vm.IsPassed);
        Assert.Equal("All 8 checks passed. The service visit can be closed.", vm.PassedText);
        Assert.Equal(0, vm.ExitCode);
        Assert.Equal(2, _engine.WriteReportCalls);
        Assert.Equal("3 deviations", SetupTexts.DeviationBadge(3));
    }

    [Fact]
    public async Task SaveAs_CopiesTheHtmlAndPrintUsesTheReport()
    {
        var source = Path.Combine(_dir, "verify-2026-11-15-104200.html");
        File.WriteAllText(source, "<html>report</html>");
        _engine.ReportPath = source;
        var vm = new VerifyViewModel(_engine, _ui);
        await vm.RunAsync();

        _ui.SaveAnswer = Path.Combine(_dir, "kopie.html");
        vm.SaveAsCommand.Execute(null);
        Assert.Equal("<html>report</html>", File.ReadAllText(_ui.SaveAnswer));
        Assert.Equal($"Report copied to {_ui.SaveAnswer}", vm.StatusText);

        _ui.SaveAnswer = Path.Combine(_dir, "missing-dir", "x.html");
        vm.SaveAsCommand.Execute(null);
        Assert.StartsWith("The report could not be copied", vm.ErrorText, StringComparison.Ordinal);

        vm.PrintCommand.Execute(null);
        Assert.Same(_engine.Report, Assert.Single(_ui.Printed));

        int? closed = null;
        vm.CloseRequested += (_, code) => closed = code;
        vm.CloseCommand.Execute(null);
        Assert.Equal(0, closed);
    }

    [Fact]
    public async Task Run_VerifyErrorIsShown()
    {
        _engine.VerifyException = new UnauthorizedAccessException("not an admin");
        var vm = new VerifyViewModel(_engine, _ui);
        await vm.RunAsync();
        Assert.Equal("Verification could not be run: not an admin", vm.ErrorText);
        Assert.False(vm.HasReport);
        Assert.False(vm.PrintCommand.CanExecute(null));
        Assert.False(vm.SaveAsCommand.CanExecute(null));
        Assert.Equal(1, vm.ExitCode);
    }
}
