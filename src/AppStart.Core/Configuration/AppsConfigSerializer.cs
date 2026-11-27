using System.Text.Json;

namespace AppStart.Core.Configuration;

/// <summary>Result of reading apps.json: either a valid configuration or the first error found.</summary>
public sealed record ConfigLoadResult(AppsConfig? Config, string? Error)
{
    public bool IsValid => Config is not null;

    public static ConfigLoadResult Ok(AppsConfig config) => new(config, null);

    public static ConfigLoadResult Fail(string error) => new(null, error);
}

/// <summary>Parses and validates apps.json (spec app-launch, requirement "Protected and valid configuration").</summary>
public static class AppsConfigSerializer
{
    public static ConfigLoadResult Parse(string json)
    {
        AppsConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, AppsConfigJsonContext.Default.AppsConfig);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $"line {line + 1}, {ex.Path}" : ex.Path;
            return ConfigLoadResult.Fail($"apps.json is not valid JSON ({where}): {ex.Message}");
        }

        if (config is null)
        {
            return ConfigLoadResult.Fail("apps.json is empty.");
        }

        var error = Validate(config);
        return error is null ? ConfigLoadResult.Ok(config) : ConfigLoadResult.Fail(error);
    }

    public static string Serialize(AppsConfig config)
    {
        var error = Validate(config);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(config));
        }

        return JsonSerializer.Serialize(config, AppsConfigJsonContext.Default.AppsConfig);
    }

    /// <summary>Returns the first rule violation, or null when the configuration is valid.</summary>
    public static string? Validate(AppsConfig config)
    {
        if (config.Version is < AppsConfig.OldestReadableVersion or > AppsConfig.CurrentVersion)
        {
            return $"version: {config.Version} is not supported, expected {AppsConfig.OldestReadableVersion} to {AppsConfig.CurrentVersion}.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < config.Apps.Count; i++)
        {
            var app = config.Apps[i];
            var at = $"apps[{i}]";

            if (!AppId.IsValid(app.Id))
            {
                return $"{at}.id: \"{app.Id}\" is invalid. Allowed are a-z, 0-9 and -, 1 to {AppId.MaxLength} characters.";
            }

            if (!seen.Add(app.Id))
            {
                return $"{at}.id: \"{app.Id}\" occurs more than once.";
            }

            if (string.IsNullOrWhiteSpace(app.Name))
            {
                return $"{at}.name: is missing.";
            }

            if (!IsAbsoluteWindowsPath(app.Exe) || !app.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return $"{at}.exe: \"{app.Exe}\" is not an absolute path to an EXE.";
            }

            if (!IsAbsoluteWindowsPath(app.WorkingDir))
            {
                return $"{at}.workingDir: \"{app.WorkingDir}\" is not an absolute path.";
            }

            if (app.DataDir is not null && !IsAbsoluteWindowsPath(app.DataDir))
            {
                return $"{at}.dataDir: \"{app.DataDir}\" is not an absolute path.";
            }

            if (app.RegistryKey is not null && !app.RegistryKey.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            {
                return $"{at}.registryKey: \"{app.RegistryKey}\" must lie under HKLM\\.";
            }

            switch (app.Check)
            {
                case IntegrityCheck.Signature when !IsHex(app.SignerThumbprint, 40):
                    return $"{at}.signerThumbprint: check \"signature\" needs 40 hex characters.";
                case IntegrityCheck.Sha256 when !IsHex(app.Sha256, 64):
                    return $"{at}.sha256: check \"sha256\" needs 64 hex characters.";
            }

            var childError = ValidateChildProcesses(app, at, config.Version);
            if (childError is not null)
            {
                return childError;
            }
        }

        return null;
    }

    // add-allowed-child-processes, design D1.
    private static string? ValidateChildProcesses(AppEntry app, string at, int version)
    {
        if (version == 1)
        {
            if (app.ChildProcesses is not null)
            {
                return $"{at}.childProcesses: exists only from version 2.";
            }

            return app.AllowedChildren is not null ? $"{at}.allowedChildren: exists only from version 2." : null;
        }

        if (app.AllowChildProcesses is not null)
        {
            return $"{at}.allowChildProcesses: no longer exists from version 2, use \"childProcesses\".";
        }

        if (app.ChildProcessMode != ChildProcessMode.Allowlist)
        {
            return app.AllowedChildren is not null
                ? $"{at}.allowedChildren: only allowed with childProcesses \"allowlist\"."
                : null;
        }

        if (app.AllowedChildren is not { Count: > 0 })
        {
            return $"{at}.allowedChildren: childProcesses \"allowlist\" needs at least one entry.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < app.AllowedChildren.Count; j++)
        {
            var child = app.AllowedChildren[j];
            var cat = $"{at}.allowedChildren[{j}]";

            if (!IsAbsoluteWindowsPath(child.Exe) || child.Exe.Contains('%', StringComparison.Ordinal)
                || !child.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return $"{cat}.exe: \"{child.Exe}\" is not an absolute path to an EXE (without placeholders).";
            }

            if (!seen.Add(child.Exe))
            {
                return $"{cat}.exe: \"{child.Exe}\" occurs more than once.";
            }

            switch (child.Check)
            {
                case IntegrityCheck.Signature when !IsHex(child.SignerThumbprint, 40):
                    return $"{cat}.signerThumbprint: check \"signature\" needs 40 hex characters.";
                case IntegrityCheck.Sha256 when !IsHex(child.Sha256, 64):
                    return $"{cat}.sha256: check \"sha256\" needs 64 hex characters.";
            }

            if (ChildProcessRules.IsShellOrScriptHost(child.Exe) && !child.AllowShell)
            {
                return $"{cat}.allowShell: \"{Path.GetFileName(child.Exe)}\" is a shell or a script host and only allowed with \"allowShell\": true.";
            }
        }

        return null;
    }

    // Drive-letter paths only (C:\...). UNC paths are rejected: the service must not start code from network shares.
    internal static bool IsAbsoluteWindowsPath(string? path) =>
        path is { Length: >= 3 }
        && char.IsAsciiLetter(path[0])
        && path[1] == ':'
        && path[2] == '\\'
        && !path.Contains("\\..", StringComparison.Ordinal)
        && path.IndexOfAny(['/', '"', '<', '>', '|', '?', '*', '\0']) < 0
        && path.IndexOf(':', 2) < 0;

    internal static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
}
