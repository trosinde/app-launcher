using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Tests.Wizard;

/// <summary>Scriptable ISetupEngine: no system changes, records what the wizard hands over.</summary>
internal sealed class FakeSetupEngine : ISetupEngine
{
    public SetupSnapshot Snapshot { get; set; } = new([], [], AppAccountExists: false, OperatorAccountExists: false, ServiceRunning: false);

    public List<SetupStepResult> ApplyResults { get; } =
    [
        new("Konten appstart-app und appstart-user, Gruppe AppStart Users angelegt", true),
        new("2 Applikationen freigegeben, Konfiguration geschützt", true),
        new("Datenordner und Registry-Keys gesperrt", true),
        new("Verknüpfungen mit App-Icon angelegt, 2 alte Verknüpfungen gesichert", true),
        new("Dienst AppStart läuft, Start automatisch", true),
    ];

    public Exception? ApplyException { get; set; }

    public Exception? VerifyException { get; set; }

    public VerifyReport Report { get; set; } = CreateReport(deviations: 0);

    public string ReportPath { get; set; } = @"C:\ProgramData\AppStart\Reports\verify-2026-11-15-104200.html";

    public SetupRequest? LastRequest { get; private set; }

    /// <summary>Password as handed to Apply (read while it is still alive).</summary>
    public string? LastPassword { get; private set; }

    public int VerifyCalls { get; private set; }

    public int WriteReportCalls { get; private set; }

    public List<string> Inspected { get; } = [];

    public SetupSnapshot LoadCurrent() => Snapshot;

    public AppCandidate Inspect(string exePath)
    {
        Inspected.Add(exePath);
        if (exePath.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Datei nicht lesbar");
        }

        var name = Path.GetFileNameWithoutExtension(exePath);
        return new AppCandidate(exePath, name, name.ToLowerInvariant(), IntegrityCheck.Sha256, null, new string('a', 64), null, null);
    }

    public IReadOnlyList<SetupStepResult> Apply(SetupRequest request, IProgress<string>? progress = null)
    {
        LastRequest = request;
        LastPassword = request.OperatorPassword is null ? null : Plain(request.OperatorPassword);
        progress?.Report("Konten werden angelegt…");
        if (ApplyException is not null)
        {
            throw ApplyException;
        }

        return ApplyResults;
    }

    public VerifyReport Verify()
    {
        VerifyCalls++;
        if (VerifyException is not null)
        {
            throw VerifyException;
        }

        return Report;
    }

    public string WriteReport(VerifyReport report)
    {
        WriteReportCalls++;
        return ReportPath;
    }

    public static VerifyReport CreateReport(int deviations)
    {
        (string Title, string Expected)[] points =
        [
            ("Dienst", "läuft, Start automatisch, LocalSystem"),
            ("Programmordner", "nur SYSTEM und Administratoren schreiben"),
            ("Konten", "appstart-user ohne Admin-Rechte, appstart-app ohne Anmeldung"),
            ("Pipe", "Rechte gemäss SDDL, keine Fernzugriffe"),
            ("apps.json", "geschützt, jede EXE besteht die Prüfung"),
            ("Datenordner je App", "nur appstart-app, SYSTEM, Administratoren"),
            ("Registry-Key je App", "für Bediener nicht lesbar"),
            ("Verknüpfungen", "zeigen auf den Launcher"),
        ];
        var checks = points.Select((p, i) =>
        {
            var deviation = (i == 2 && deviations > 0) || (i == 6 && deviations > 1);
            return new CheckResult(
                $"check-{i}",
                p.Title,
                p.Expected,
                deviation ? CheckOutcome.Deviation : CheckOutcome.Passed,
                deviation ? (i == 2 ? ["appstart-user ist Mitglied der Administratoren"] : ["LABOR\\bediener01 kann den Key lesen"]) : []);
        }).ToList();
        return new VerifyReport(new DateTimeOffset(2026, 11, 15, 10, 42, 0, TimeSpan.Zero), "LAB-PC-014", "LABOR\\service", "0.1.0", checks);
    }

    public static string Plain(SecureString s)
    {
        var p = Marshal.SecureStringToGlobalAllocUnicode(s);
        try
        {
            return Marshal.PtrToStringUni(p, s.Length);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(p);
        }
    }

    public static AppEntry Entry(string name, string exe, IntegrityCheck check, string? dataDir = null, string? key = null) => new()
    {
        Id = name.ToLowerInvariant().Replace(' ', '-'),
        Name = name,
        Exe = exe,
        WorkingDir = Path.GetDirectoryName(exe) ?? string.Empty,
        Check = check,
        SignerThumbprint = check == IntegrityCheck.Signature ? new string('B', 40) : null,
        Sha256 = check == IntegrityCheck.Sha256 ? new string('a', 64) : null,
        DataDir = dataDir,
        RegistryKey = key,
    };
}

/// <summary>Dialog answers queued by the test; records shell and print calls.</summary>
internal sealed class FakeUi : IUiServices
{
    public Queue<IReadOnlyList<string>> ExeAnswers { get; } = new();

    public string? FolderAnswer { get; set; }

    public string? SaveAnswer { get; set; }

    public List<string> Opened { get; } = [];

    public List<VerifyReport> Printed { get; } = [];

    public IReadOnlyList<string> PickExeFiles() => ExeAnswers.Count > 0 ? ExeAnswers.Dequeue() : [];

    public string? PickFolder(string? initialFolder) => FolderAnswer;

    public string? PickReportSavePath(string defaultFileName) => SaveAnswer;

    public void OpenWithShell(string path) => Opened.Add(path);

    public bool PrintReport(VerifyReport report)
    {
        Printed.Add(report);
        return true;
    }
}

internal static class SecureText
{
    public static SecureString Of(string text)
    {
        var s = new SecureString();
        foreach (var c in text)
        {
            s.AppendChar(c);
        }

        return s;
    }
}
