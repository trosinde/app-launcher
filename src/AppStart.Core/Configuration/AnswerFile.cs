using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppStart.Core.Configuration;

/// <summary>
/// Answer file for the unattended setup: AppStart.Setup.exe apply --config &lt;file&gt;
/// (add-allowed-child-processes, design D5). Describes the wanted state; check values (thumbprint,
/// SHA-256) are not in the file but read from the files on the target machine. No passwords.
/// </summary>
public sealed record AnswerFile
{
    public const int CurrentVersion = 1;

    [JsonRequired]
    public int Version { get; init; }

    [JsonRequired]
    public IReadOnlyList<AnswerApp> Apps { get; init; } = [];

    /// <summary>Accounts or groups added to "AppStart Users" (e.g. DOMAIN\Operator-Group).</summary>
    public IReadOnlyList<string>? Operators { get; init; }
}

/// <summary>One application in the answer file.</summary>
public sealed record AnswerApp
{
    [JsonRequired]
    public string Exe { get; init; } = string.Empty;

    /// <summary>Optional; without it the setup derives the AppId from the name as today.</summary>
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? DataDir { get; init; }

    public string? RegistryKey { get; init; }

    /// <summary>Optional; without it signature when validly signed, else SHA-256.</summary>
    public IntegrityCheck? Check { get; init; }

    /// <summary>Optional; blocked when missing.</summary>
    public ChildProcessMode? ChildProcesses { get; init; }

    public IReadOnlyList<AnswerChild>? AllowedChildren { get; init; }
}

/// <summary>One helper program in the answer file.</summary>
public sealed record AnswerChild
{
    [JsonRequired]
    public string Exe { get; init; } = string.Empty;

    public IntegrityCheck? Check { get; init; }

    /// <summary>Shells and script hosts only with this confirmation (design D5).</summary>
    public bool AllowShell { get; init; }
}

public static class AnswerFileSerializer
{
    /// <summary>
    /// Strict: unknown fields, comments and trailing commas are errors. Returns every rule violation
    /// (field and reason) so the installer can fix the file in one go; nothing may be changed then.
    /// </summary>
    public static (AnswerFile? File, IReadOnlyList<string> Errors) Parse(string json)
    {
        AnswerFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, AnswerFileJsonContext.Default.AnswerFile);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $"line {line + 1}, {ex.Path}" : ex.Path;
            return (null, [$"Answer file is not valid JSON ({where}): {ex.Message}"]);
        }

        if (file is null)
        {
            return (null, ["Answer file is empty."]);
        }

        var errors = Validate(file);
        return errors.Count == 0 ? (file, errors) : (null, errors);
    }

    public static IReadOnlyList<string> Validate(AnswerFile file)
    {
        var errors = new List<string>();
        if (file.Version != AnswerFile.CurrentVersion)
        {
            errors.Add($"version: {file.Version} is not supported, expected {AnswerFile.CurrentVersion}.");
        }

        var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < file.Apps.Count; i++)
        {
            ValidateApp(file.Apps[i], $"apps[{i}]", exes, ids, errors);
        }

        if (file.Operators is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < file.Operators.Count; i++)
            {
                var op = file.Operators[i];
                if (string.IsNullOrWhiteSpace(op) || op != op.Trim() || op.Count(c => c == '\\') > 1)
                {
                    errors.Add($"operators[{i}]: \"{op}\" is not an account or group name (NAME or DOMAIN\\NAME).");
                }
                else if (!seen.Add(op))
                {
                    errors.Add($"operators[{i}]: \"{op}\" occurs more than once.");
                }
            }
        }

        return errors;
    }

    private static void ValidateApp(AnswerApp app, string at, HashSet<string> exes, HashSet<string> ids, List<string> errors)
    {
        if (!IsExePath(app.Exe))
        {
            errors.Add($"{at}.exe: \"{app.Exe}\" is not an absolute path to an EXE.");
        }
        else if (!exes.Add(app.Exe))
        {
            errors.Add($"{at}.exe: \"{app.Exe}\" occurs more than once.");
        }

        if (app.Id is not null)
        {
            if (!AppId.IsValid(app.Id))
            {
                errors.Add($"{at}.id: \"{app.Id}\" is invalid. Allowed are a-z, 0-9 and -, 1 to {AppId.MaxLength} characters.");
            }
            else if (!ids.Add(app.Id))
            {
                errors.Add($"{at}.id: \"{app.Id}\" occurs more than once.");
            }
        }

        if (app.Name is not null && string.IsNullOrWhiteSpace(app.Name))
        {
            errors.Add($"{at}.name: leer.");
        }

        if (app.DataDir is not null && !AppsConfigSerializer.IsAbsoluteWindowsPath(app.DataDir))
        {
            errors.Add($"{at}.dataDir: \"{app.DataDir}\" is not an absolute path.");
        }

        if (app.RegistryKey is not null && !app.RegistryKey.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{at}.registryKey: \"{app.RegistryKey}\" must lie under HKLM\\.");
        }

        var mode = app.ChildProcesses ?? ChildProcessMode.Blocked;
        if (mode != ChildProcessMode.Allowlist)
        {
            if (app.AllowedChildren is not null)
            {
                errors.Add($"{at}.allowedChildren: only allowed with childProcesses \"allowlist\".");
            }

            return;
        }

        if (app.AllowedChildren is not { Count: > 0 })
        {
            errors.Add($"{at}.allowedChildren: childProcesses \"allowlist\" needs at least one entry.");
            return;
        }

        var children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < app.AllowedChildren.Count; j++)
        {
            var child = app.AllowedChildren[j];
            var cat = $"{at}.allowedChildren[{j}]";
            if (!IsExePath(child.Exe))
            {
                errors.Add($"{cat}.exe: \"{child.Exe}\" is not an absolute path to an EXE (without placeholders).");
                continue;
            }

            if (!children.Add(child.Exe))
            {
                errors.Add($"{cat}.exe: \"{child.Exe}\" occurs more than once.");
            }

            if (ChildProcessRules.IsShellOrScriptHost(child.Exe) && !child.AllowShell)
            {
                errors.Add($"{cat}.allowShell: \"{Path.GetFileName(child.Exe)}\" is a shell or a script host and only allowed with \"allowShell\": true.");
            }
        }
    }

    private static bool IsExePath(string? path) =>
        AppsConfigSerializer.IsAbsoluteWindowsPath(path)
        && !path!.Contains('%', StringComparison.Ordinal)
        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false)]
[JsonSerializable(typeof(AnswerFile))]
internal sealed partial class AnswerFileJsonContext : JsonSerializerContext;
