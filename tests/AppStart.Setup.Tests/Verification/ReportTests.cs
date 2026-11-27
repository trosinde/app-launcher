using System.IO;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Verification;

namespace AppStart.Setup.Tests.Verification;

public class ReportTests
{
    private static VerifyReport Sample(params CheckResult[] checks) =>
        new(new DateTimeOffset(2026, 11, 15, 10, 42, 7, TimeSpan.FromHours(1)), "LAB-PC-014", @"LAB-PC-014\techniker", "1.2.3", checks);

    private static CheckResult Ok(string id, string title) => new(id, title, "Expected " + title, CheckOutcome.Passed, []);

    private static VerifyReport OneDeviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] = machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] with { IsLocalAdministrator = true };
        return machine.Run();
    }

    [Fact]
    public void File_name_is_verify_with_local_date_and_time()
    {
        Assert.Equal("verify-2026-11-15-104207", VerifyReportWriter.FileBaseName(Sample()));
    }

    [Fact]
    public void Write_creates_html_and_json_and_returns_the_html_path()
    {
        var dir = Path.Combine(Machine.TempDirectory(), "Reports");
        try
        {
            var html = VerifyReportWriter.Write(Sample(Ok("dienst", "Service")), dir);

            Assert.Equal(Path.Combine(dir, "verify-2026-11-15-104207.html"), html);
            Assert.True(File.Exists(html));
            Assert.True(File.Exists(Path.Combine(dir, "verify-2026-11-15-104207.json")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Verifier_WriteReport_uses_the_writer()
    {
        var dir = Machine.TempDirectory();
        try
        {
            var machine = new Machine();
            var verifier = machine.CreateVerifier();
            var html = verifier.WriteReport(verifier.Run(), dir);
            Assert.Equal(Path.Combine(dir, "verify-2026-11-15-104207.html"), html);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Findings_are_html_encoded_and_the_report_has_no_script()
    {
        var report = Sample(new CheckResult("konten", "Accounts", "operators <without> admin", CheckOutcome.Deviation,
            ["<script>alert('x')</script> & \"Bob\" is a member of Administrators"]));

        var html = VerifyReportWriter.RenderHtml(report);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<without>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;Bob&quot; is a member of Administrators", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<link", html);
    }

    [Fact]
    public void Meta_data_values_are_encoded()
    {
        var report = Sample() with { Computer = "<b>PC</b>", Technician = "a&b" };
        var html = VerifyReportWriter.RenderHtml(report);
        Assert.Contains("&lt;b&gt;PC&lt;/b&gt;", html);
        Assert.Contains("a&amp;b", html);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Report_with_deviation_shows_red_box_with_findings_and_command()
    {
        var html = VerifyReportWriter.RenderHtml(OneDeviation());

        Assert.Contains("VERIFICATION REPORT", html);
        Assert.Contains("AppStart – Verification after a service visit", html);
        Assert.Contains(">1 deviation<", html);
        Assert.Contains("<dt>Computer</dt><dd>LAB-PC-014</dd>", html);
        Assert.Contains("<dt>Date</dt><dd>15.11.2026 10:42</dd>", html);
        Assert.Contains(@"<dt>Service technician</dt><dd>LAB-PC-014\techniker</dd>", html);
        Assert.Contains("<dt>Version</dt><dd>AppStart 1.2.3</dd>", html);
        Assert.Contains("Do not close the service visit", html);
        Assert.Contains(@"Accounts: LAB-PC-014\appstart-user is a member of Administrators", html);
        Assert.Contains("Correct and repeat the verification: <code>AppStart.Setup.exe verify</code>", html);
        Assert.Equal(9, CountOf(html, ">Passed<"));
        Assert.Equal(1, CountOf(html, ">Deviation<"));
        Assert.DoesNotContain("Verification passed –", html); // green summary box (the check "Helper programs" says "verification passed," in its expected state)
        Assert.Contains("Signature service technician", html);
        Assert.Contains("Signature customer", html);
        Assert.Contains("@page{size:A4", html);
        Assert.Contains("@media print", html);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Report_without_deviation_shows_green_box()
    {
        var html = VerifyReportWriter.RenderHtml(new Machine().Run());

        Assert.Contains("Verification passed – 10 of 10 checks passed", html);
        Assert.Contains(">No deviation<", html);
        Assert.DoesNotContain("Do not close the service visit", html);
        Assert.Equal(10, CountOf(html, ">Passed<"));
    }

    [Fact]
    public void Json_roundtrip_keeps_every_value()
    {
        var report = Sample(
            Ok("dienst", "Service"),
            new CheckResult("konten", "Accounts", "operators without admin rights", CheckOutcome.Deviation, ["appstart-user is a member of Administrators", "<x>"]));

        var json = VerifyReportWriter.RenderJson(report);
        var back = VerifyReportWriter.ParseJson(json)!;

        Assert.Contains("\"outcome\": \"Deviation\"", json);
        Assert.Contains("\"exitCode\": 1", json);
        Assert.Contains("appstart-user is a member of Administrators", json);
        Assert.Equal(report.CreatedAt, back.CreatedAt);
        Assert.Equal(report.CreatedAt.Offset, back.CreatedAt.Offset);
        Assert.Equal(report.Computer, back.Computer);
        Assert.Equal(report.Technician, back.Technician);
        Assert.Equal(report.Version, back.Version);
        Assert.Equal(report.ExitCode, back.ExitCode);
        Assert.Equal(report.Checks.Count, back.Checks.Count);
        for (var i = 0; i < report.Checks.Count; i++)
        {
            var (a, b) = (report.Checks[i], back.Checks[i]);
            Assert.Equal((a.Id, a.Title, a.Expected, a.Outcome), (b.Id, b.Title, b.Expected, b.Outcome));
            Assert.Equal(a.Findings, b.Findings);
        }
    }

    [Fact]
    public void Json_keeps_umlauts_readable()
    {
        var json = VerifyReportWriter.RenderJson(Sample(Ok("verknuepfungen", "Shortcuts")));
        Assert.Contains("Shortcuts", json);
    }

    /// <summary>
    /// Renders the sample report (1 deviation) for a visual comparison with docs/ui-designs/Verify.dc.html,
    /// only when APPSTART_VERIFY_SAMPLE names the target file.
    /// </summary>
    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Render_sample_report()
    {
        var target = Environment.GetEnvironmentVariable("APPSTART_VERIFY_SAMPLE");
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        File.WriteAllText(target, VerifyReportWriter.RenderHtml(OneDeviation()));
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
