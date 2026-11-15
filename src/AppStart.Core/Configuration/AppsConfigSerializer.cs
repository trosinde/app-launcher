using System.Text.Json;

namespace AppStart.Core.Configuration;

/// <summary>Result of reading apps.json: either a valid configuration or the first error found.</summary>
public sealed record ConfigLoadResult(AppsConfig? Config, string? Error)
{
    public bool IsValid => Config is not null;

    public static ConfigLoadResult Ok(AppsConfig config) => new(config, null);

    public static ConfigLoadResult Fail(string error) => new(null, error);
}

/// <summary>Parses and validates apps.json (spec app-launch, requirement "Geschützte und gültige Konfiguration").</summary>
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
            var where = ex.LineNumber is { } line ? $"Zeile {line + 1}, {ex.Path}" : ex.Path;
            return ConfigLoadResult.Fail($"apps.json ist kein gültiges JSON ({where}): {ex.Message}");
        }

        if (config is null)
        {
            return ConfigLoadResult.Fail("apps.json ist leer.");
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
            return $"version: {config.Version} wird nicht unterstützt, erwartet {AppsConfig.OldestReadableVersion} bis {AppsConfig.CurrentVersion}.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < config.Apps.Count; i++)
        {
            var app = config.Apps[i];
            var at = $"apps[{i}]";

            if (!AppId.IsValid(app.Id))
            {
                return $"{at}.id: «{app.Id}» ist ungültig. Erlaubt sind a-z, 0-9 und -, 1 bis {AppId.MaxLength} Zeichen.";
            }

            if (!seen.Add(app.Id))
            {
                return $"{at}.id: «{app.Id}» kommt mehrfach vor.";
            }

            if (string.IsNullOrWhiteSpace(app.Name))
            {
                return $"{at}.name: fehlt.";
            }

            if (!IsAbsoluteWindowsPath(app.Exe) || !app.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return $"{at}.exe: «{app.Exe}» ist kein absoluter Pfad auf eine EXE.";
            }

            if (!IsAbsoluteWindowsPath(app.WorkingDir))
            {
                return $"{at}.workingDir: «{app.WorkingDir}» ist kein absoluter Pfad.";
            }

            if (app.DataDir is not null && !IsAbsoluteWindowsPath(app.DataDir))
            {
                return $"{at}.dataDir: «{app.DataDir}» ist kein absoluter Pfad.";
            }

            if (app.RegistryKey is not null && !app.RegistryKey.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            {
                return $"{at}.registryKey: «{app.RegistryKey}» muss unter HKLM\\ liegen.";
            }

            switch (app.Check)
            {
                case IntegrityCheck.Signature when !IsHex(app.SignerThumbprint, 40):
                    return $"{at}.signerThumbprint: bei check «signature» sind 40 Hex-Zeichen nötig.";
                case IntegrityCheck.Sha256 when !IsHex(app.Sha256, 64):
                    return $"{at}.sha256: bei check «sha256» sind 64 Hex-Zeichen nötig.";
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
                return $"{at}.childProcesses: gibt es erst ab Version 2.";
            }

            return app.AllowedChildren is not null ? $"{at}.allowedChildren: gibt es erst ab Version 2." : null;
        }

        if (app.AllowChildProcesses is not null)
        {
            return $"{at}.allowChildProcesses: gibt es ab Version 2 nicht mehr, «childProcesses» verwenden.";
        }

        if (app.ChildProcessMode != ChildProcessMode.Allowlist)
        {
            return app.AllowedChildren is not null
                ? $"{at}.allowedChildren: nur bei childProcesses «allowlist» erlaubt."
                : null;
        }

        if (app.AllowedChildren is not { Count: > 0 })
        {
            return $"{at}.allowedChildren: bei childProcesses «allowlist» ist mindestens ein Eintrag nötig.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < app.AllowedChildren.Count; j++)
        {
            var child = app.AllowedChildren[j];
            var cat = $"{at}.allowedChildren[{j}]";

            if (!IsAbsoluteWindowsPath(child.Exe) || child.Exe.Contains('%', StringComparison.Ordinal)
                || !child.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return $"{cat}.exe: «{child.Exe}» ist kein absoluter Pfad auf eine EXE (ohne Platzhalter).";
            }

            if (!seen.Add(child.Exe))
            {
                return $"{cat}.exe: «{child.Exe}» kommt mehrfach vor.";
            }

            switch (child.Check)
            {
                case IntegrityCheck.Signature when !IsHex(child.SignerThumbprint, 40):
                    return $"{cat}.signerThumbprint: bei check «signature» sind 40 Hex-Zeichen nötig.";
                case IntegrityCheck.Sha256 when !IsHex(child.Sha256, 64):
                    return $"{cat}.sha256: bei check «sha256» sind 64 Hex-Zeichen nötig.";
            }

            if (ChildProcessRules.IsShellOrScriptHost(child.Exe) && !child.AllowShell)
            {
                return $"{cat}.allowShell: «{Path.GetFileName(child.Exe)}» ist eine Shell oder ein Skript-Host und nur mit «allowShell»: true erlaubt.";
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
