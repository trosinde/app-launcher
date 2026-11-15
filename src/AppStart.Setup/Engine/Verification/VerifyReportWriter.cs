using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace AppStart.Setup.Engine.Verification;

/// <summary>
/// Writes the inspection report (design docs/ui-designs/Verify.dc.html) as self-contained HTML (inline CSS,
/// no script, no external resources, every value HTML-encoded, A4 print layout) and as JSON.
/// </summary>
public static class VerifyReportWriter
{
    private static readonly VerifyReportJsonContext Json = new(new JsonSerializerOptions
    {
        // Readable umlauts; HTML-sensitive characters stay escaped.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<CheckOutcome>() },
    });

    /// <summary>verify-yyyy-MM-dd-HHmmss (local time of the report).</summary>
    public static string FileBaseName(VerifyReport report) =>
        "verify-" + report.CreatedAt.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>Writes .html and .json into <paramref name="directory"/> (created if missing); returns the HTML path.</summary>
    public static string Write(VerifyReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var baseName = Path.Combine(directory, FileBaseName(report));
        var html = baseName + ".html";
        File.WriteAllText(html, RenderHtml(report), new UTF8Encoding(false));
        File.WriteAllText(baseName + ".json", RenderJson(report), new UTF8Encoding(false));
        return html;
    }

    public static string RenderJson(VerifyReport report) => JsonSerializer.Serialize(report, Json.VerifyReport);

    public static VerifyReport? ParseJson(string json) => JsonSerializer.Deserialize(json, Json.VerifyReport);

    public static string RenderHtml(VerifyReport report)
    {
        var passed = report.Checks.Count - report.Deviations;
        var badge = report.Deviations switch
        {
            0 => VerificationTexts.BadgeNoDeviation,
            1 => VerificationTexts.BadgeOneDeviation,
            var n => string.Format(CultureInfo.InvariantCulture, VerificationTexts.BadgeDeviations, n),
        };

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"de-CH\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(E(VerificationTexts.ReportTitle)).Append(" – ").Append(E(report.Computer))
          .Append(' ').Append(E(FormatDate(report))).Append("</title>\n");
        sb.Append("<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n<main class=\"page\">\n");

        // Header
        sb.Append("<header class=\"head\">\n<div>\n<div class=\"kicker\">").Append(E(VerificationTexts.ReportKicker)).Append("</div>\n");
        sb.Append("<h1>").Append(E(VerificationTexts.ReportHeading)).Append("</h1>\n</div>\n");
        sb.Append("<div class=\"badge ").Append(report.Deviations == 0 ? "badge-ok" : "badge-fail").Append("\">")
          .Append(E(badge)).Append("</div>\n</header>\n");

        // Meta data
        sb.Append("<dl class=\"meta\">\n");
        Meta(sb, VerificationTexts.LabelComputer, report.Computer);
        Meta(sb, VerificationTexts.LabelDate, FormatDate(report));
        Meta(sb, VerificationTexts.LabelTechnician, report.Technician);
        Meta(sb, VerificationTexts.LabelVersion, string.Format(CultureInfo.InvariantCulture, VerificationTexts.VersionValue, report.Version));
        sb.Append("</dl>\n");

        // Check table
        sb.Append("<table>\n<thead>\n<tr><th>").Append(E(VerificationTexts.ColumnCheck)).Append("</th><th>")
          .Append(E(VerificationTexts.ColumnExpected)).Append("</th><th class=\"result\">")
          .Append(E(VerificationTexts.ColumnResult)).Append("</th></tr>\n</thead>\n<tbody>\n");
        foreach (var check in report.Checks)
        {
            var failed = check.Outcome == CheckOutcome.Deviation;
            sb.Append(failed ? "<tr class=\"fail\" id=\"" : "<tr id=\"").Append(E(check.Id)).Append("\">");
            sb.Append("<td class=\"title\">").Append(E(check.Title)).Append("</td>");
            if (failed)
            {
                sb.Append("<td><ul class=\"findings\">");
                foreach (var finding in check.Findings)
                {
                    sb.Append("<li>").Append(E(finding)).Append("</li>");
                }

                sb.Append("</ul><div class=\"expected\">").Append(E(VerificationTexts.ExpectedPrefix)).Append(' ')
                  .Append(E(check.Expected)).Append("</div></td>");
            }
            else
            {
                sb.Append("<td class=\"muted\">").Append(E(check.Expected)).Append("</td>");
            }

            sb.Append("<td><span class=\"pill ").Append(failed ? "pill-fail" : "pill-ok").Append("\">")
              .Append(E(failed ? VerificationTexts.ResultDeviation : VerificationTexts.ResultPassed))
              .Append("</span></td></tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");

        // Verdict
        if (report.Deviations > 0)
        {
            sb.Append("<section class=\"box box-fail\">\n<strong>").Append(E(VerificationTexts.FailedTitle)).Append("</strong>\n<ul>\n");
            foreach (var check in report.Checks.Where(c => c.Outcome == CheckOutcome.Deviation))
            {
                foreach (var finding in check.Findings)
                {
                    sb.Append("<li>").Append(E(check.Title)).Append(": ").Append(E(finding)).Append("</li>\n");
                }
            }

            sb.Append("</ul>\n<span>").Append(E(VerificationTexts.FailedHint)).Append(" <code>")
              .Append(E(VerificationTexts.VerifyCommand)).Append("</code></span>\n</section>\n");
        }
        else
        {
            sb.Append("<section class=\"box box-ok\"><strong>")
              .Append(E(string.Format(CultureInfo.InvariantCulture, VerificationTexts.PassedSummary, passed, report.Checks.Count)))
              .Append("</strong></section>\n");
        }

        // Signatures
        sb.Append("<footer class=\"signatures\">\n<div>").Append(E(VerificationTexts.SignatureTechnician)).Append("</div>\n<div>")
          .Append(E(VerificationTexts.SignatureCustomer)).Append("</div>\n</footer>\n");
        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string FormatDate(VerifyReport report) =>
        report.CreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static void Meta(StringBuilder sb, string label, string value) =>
        sb.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>\n");

    // Encodes the five HTML-significant characters (text and attribute values); umlauts stay readable.
    internal static string E(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }

    // Colours and spacing from docs/ui-designs/Verify.dc.html; @page/@media print for A4.
    private const string Css = """
        *{box-sizing:border-box}
        html{background:#ffffff}
        body{margin:0;font-family:"Segoe UI",system-ui,sans-serif;color:#1b1b1b;background:#ffffff}
        .page{max-width:960px;margin:0 auto;padding:36px 40px;display:flex;flex-direction:column;gap:18px}
        .head{display:flex;justify-content:space-between;align-items:flex-start;gap:24px}
        .kicker{font-size:12px;font-weight:600;letter-spacing:.06em;color:#4a4a4a}
        h1{margin:4px 0 0;font-size:26px;font-weight:600}
        .badge{font-weight:600;font-size:14px;padding:8px 12px;border-radius:4px;white-space:nowrap}
        .badge-fail{background:#fbe9e7;color:#9c1f17}
        .badge-ok{background:#e3f2e6;color:#1e6b33}
        .meta{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;font-size:13px;margin:0}
        .meta div{display:flex;flex-direction:column;gap:2px;min-width:0}
        .meta dt{color:#4a4a4a}
        .meta dd{margin:0;font-weight:600;overflow-wrap:anywhere}
        table{width:100%;border-collapse:collapse;font-size:14px}
        th{text-align:left;color:#4a4a4a;font-size:12px;padding:8px 10px;border-bottom:2px solid #d6d6d6;font-weight:600}
        th.result{width:130px}
        td{padding:9px 10px;border-bottom:1px solid #eeeeee;vertical-align:top;overflow-wrap:anywhere}
        td.title{white-space:nowrap}
        .muted{color:#4a4a4a}
        tr.fail{background:#fdf3f2}
        tr.fail td.title{font-weight:600}
        .findings{margin:0;padding:0;list-style:none;display:flex;flex-direction:column;gap:4px}
        .expected{margin-top:6px;font-size:12px;color:#4a4a4a}
        .pill{font-weight:600;padding:3px 8px;border-radius:4px;font-size:13px;white-space:nowrap}
        .pill-ok{background:#e3f2e6;color:#1e6b33}
        .pill-fail{background:#b3261e;color:#ffffff}
        .box{border-radius:4px;padding:12px 16px;font-size:14px;display:flex;flex-direction:column;gap:4px}
        .box ul{margin:0;padding-left:18px}
        .box-fail{background:#fbe9e7}
        .box-fail strong{font-weight:600;color:#9c1f17}
        .box-ok{background:#e3f2e6}
        .box-ok strong{font-weight:600;color:#1e6b33}
        code{font-family:Consolas,monospace;font-size:13px}
        .signatures{margin-top:48px;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px;font-size:13px;color:#4a4a4a}
        .signatures div{border-top:1px solid #1b1b1b;padding-top:6px}
        @media (max-width:640px){.page{padding:24px 16px}.meta{grid-template-columns:repeat(2,minmax(0,1fr))}td.title{white-space:normal}}
        @page{size:A4 portrait;margin:15mm}
        @media print{
        body{-webkit-print-color-adjust:exact;print-color-adjust:exact}
        .page{max-width:none;padding:0;gap:14px}
        h1{font-size:20pt}
        table{font-size:10pt}
        tr,.box,.signatures{break-inside:avoid}
        thead{display:table-header-group}
        .signatures{margin-top:20mm}
        }

        """;
}

[JsonSerializable(typeof(VerifyReport))]
internal sealed partial class VerifyReportJsonContext : JsonSerializerContext;
