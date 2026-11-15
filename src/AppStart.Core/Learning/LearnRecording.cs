using System.Text.Json;
using System.Text.Json.Serialization;
using AppStart.Core.Configuration;
using AppStart.Core.Security;

namespace AppStart.Core.Learning;

/// <summary>
/// Content of C:\ProgramData\AppStart\Learn\&lt;AppId&gt;.json: the programs an application started
/// during learn mode (add-allowed-child-processes, design D3). Written by the service, read by the
/// setup for «learn show» and «learn apply».
/// </summary>
public sealed record LearnRecording
{
    public const int CurrentVersion = 1;

    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;

    [JsonRequired]
    public string AppId { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<RecordedProgram> Programs { get; init; } = [];

    /// <summary>Adds one start: a new program, or count and last start of a known one (path compared without case).</summary>
    public LearnRecording Record(string exe, string? signerThumbprint, string sha256, string? parentExe, DateTimeOffset nowUtc)
    {
        var existing = Programs.FirstOrDefault(p => string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase));
        RecordedProgram updated;
        if (existing is null)
        {
            updated = new RecordedProgram
            {
                Exe = exe,
                SignatureValid = signerThumbprint is not null,
                SignerThumbprint = signerThumbprint,
                Sha256 = sha256,
                Starts = 1,
                FirstStartUtc = nowUtc,
                LastStartUtc = nowUtc,
                ParentExes = parentExe is null ? [] : [parentExe],
            };
        }
        else
        {
            var parents = existing.ParentExes;
            if (parentExe is not null && !parents.Contains(parentExe, StringComparer.OrdinalIgnoreCase))
            {
                parents = [.. parents, parentExe];
            }

            // The latest file content counts: a helper replaced during learning is proposed as it is now.
            updated = existing with
            {
                SignatureValid = signerThumbprint is not null,
                SignerThumbprint = signerThumbprint,
                Sha256 = sha256,
                Starts = existing.Starts + 1,
                LastStartUtc = nowUtc,
                ParentExes = parents,
            };
        }

        return this with { Programs = [.. Programs.Where(p => !ReferenceEquals(p, existing)), updated] };
    }
}

/// <summary>One program seen during learn mode.</summary>
public sealed record RecordedProgram
{
    [JsonRequired]
    public string Exe { get; init; } = string.Empty;

    [JsonRequired]
    public bool SignatureValid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignerThumbprint { get; init; }

    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;

    [JsonRequired]
    public int Starts { get; init; }

    [JsonRequired]
    public DateTimeOffset FirstStartUtc { get; init; }

    [JsonRequired]
    public DateTimeOffset LastStartUtc { get; init; }

    [JsonRequired]
    public IReadOnlyList<string> ParentExes { get; init; } = [];

    /// <summary>Shells and script hosts are recorded but never proposed (design D3).</summary>
    [JsonIgnore]
    public bool IsShellOrScriptHost => ChildProcessRules.IsShellOrScriptHost(Exe);

    /// <summary>Proposed check: signature when validly signed, else SHA-256 (design D3).</summary>
    [JsonIgnore]
    public IntegrityCheck SuggestedCheck => SignatureValid ? IntegrityCheck.Signature : IntegrityCheck.Sha256;

    /// <summary>The allowlist entry this recording proposes.</summary>
    public AllowedChild ToAllowedChild() => SuggestedCheck == IntegrityCheck.Signature
        ? new AllowedChild { Exe = Exe, Check = IntegrityCheck.Signature, SignerThumbprint = SignerThumbprint }
        : new AllowedChild { Exe = Exe, Check = IntegrityCheck.Sha256, Sha256 = Sha256 };
}

public static class LearnRecordingSerializer
{
    public static (LearnRecording? Recording, string? Error) Parse(string json)
    {
        LearnRecording? recording;
        try
        {
            recording = JsonSerializer.Deserialize(json, LearningJsonContext.Default.LearnRecording);
        }
        catch (JsonException ex)
        {
            return (null, $"Aufzeichnung ist kein gültiges JSON ({ex.Path}): {ex.Message}");
        }

        if (recording is null)
        {
            return (null, "Aufzeichnung ist leer.");
        }

        var error = Validate(recording);
        return error is null ? (recording, null) : (null, error);
    }

    public static string Serialize(LearnRecording recording)
    {
        var error = Validate(recording);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(recording));
        }

        return JsonSerializer.Serialize(recording, LearningJsonContext.Default.LearnRecording);
    }

    public static string? Validate(LearnRecording recording)
    {
        if (recording.Version != LearnRecording.CurrentVersion)
        {
            return $"version: {recording.Version} wird nicht unterstützt, erwartet {LearnRecording.CurrentVersion}.";
        }

        if (!AppId.IsValid(recording.AppId))
        {
            return $"appId: «{recording.AppId}» ist ungültig.";
        }

        for (var i = 0; i < recording.Programs.Count; i++)
        {
            var p = recording.Programs[i];
            var at = $"programs[{i}]";
            if (!AppsConfigSerializer.IsAbsoluteWindowsPath(p.Exe))
            {
                return $"{at}.exe: «{p.Exe}» ist kein absoluter Pfad.";
            }

            if (!AppsConfigSerializer.IsHex(p.Sha256, 64))
            {
                return $"{at}.sha256: 64 Hex-Zeichen nötig.";
            }

            if (p.SignatureValid != AppsConfigSerializer.IsHex(p.SignerThumbprint, 40))
            {
                return $"{at}.signerThumbprint: bei signatureValid true genau 40 Hex-Zeichen, sonst keiner.";
            }

            if (p.Starts < 1 || p.LastStartUtc < p.FirstStartUtc)
            {
                return $"{at}: Anzahl oder Zeitpunkte ungültig.";
            }
        }

        return null;
    }
}

/// <summary>Reads and writes recordings (folder and file protected like apps.json).</summary>
public static class LearnRecordingStore
{
    public static string PathFor(string appId, string? directory = null) =>
        Path.Combine(directory ?? AppStartConstants.LearnDirectory, appId + ".json");

    /// <summary>
    /// Null recording and no error when nothing was recorded. A recording operators could have changed
    /// is refused: «learn apply» turns it into allowlist entries.
    /// </summary>
    /// <param name="writeViolations">Violations of the file and its folder (default: <see cref="WriteAccessPolicy"/>); injectable for tests.</param>
    public static (LearnRecording? Recording, string? Error) Load(string appId, string? directory = null,
        Func<string, IReadOnlyList<string>>? writeViolations = null)
    {
        var path = PathFor(appId, directory);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        var found = writeViolations?.Invoke(path)
                    ?? [.. WriteAccessPolicy.FindViolations(Path.GetDirectoryName(path)!), .. WriteAccessPolicy.FindViolations(path)];
        if (found.Count > 0)
        {
            return (null, $"{path} ist nicht geschützt: {string.Join(" ", found)}");
        }

        var (recording, error) = LearnRecordingSerializer.Parse(File.ReadAllText(path));
        if (recording is not null && recording.AppId != appId)
        {
            return (null, $"{path} gehört zu «{recording.AppId}», erwartet «{appId}».");
        }

        return (recording, error);
    }
}
