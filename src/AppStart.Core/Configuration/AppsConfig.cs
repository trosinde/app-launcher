using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppStart.Core.Configuration;

/// <summary>How the integrity of an application EXE is checked.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntegrityCheck>))]
public enum IntegrityCheck
{
    /// <summary>Valid Authenticode chain and pinned signer thumbprint (survives application updates).</summary>
    [JsonStringEnumMemberName("signature")]
    Signature,

    /// <summary>SHA-256 of the file (for unsigned EXEs; needs rehash after each update).</summary>
    [JsonStringEnumMemberName("sha256")]
    Sha256,
}

/// <summary>Which further processes an application may start (add-allowed-child-processes, design D1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChildProcessMode>))]
public enum ChildProcessMode
{
    /// <summary>Job object with ActiveProcessLimit 1: no further process (default).</summary>
    [JsonStringEnumMemberName("blocked")]
    Blocked,

    /// <summary>Monitored job object: only entries of allowedChildren, every other process is terminated.</summary>
    [JsonStringEnumMemberName("allowlist")]
    Allowlist,

    /// <summary>No job object: every process allowed (unsafe, deliberate choice).</summary>
    [JsonStringEnumMemberName("all")]
    All,
}

/// <summary>Content of C:\ProgramData\AppStart\apps.json.</summary>
public sealed record AppsConfig
{
    /// <summary>Version written by the setup. Version 1 is still read (design D1, migration plan).</summary>
    public const int CurrentVersion = 2;

    public const int OldestReadableVersion = 1;

    [JsonRequired]
    public int Version { get; init; }

    [JsonRequired]
    public IReadOnlyList<AppEntry> Apps { get; init; } = [];

    public AppEntry? Find(string appId) =>
        Apps.FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.Ordinal));

    /// <summary>
    /// The same configuration in the current version: allowChildProcesses false becomes childProcesses
    /// blocked, true becomes all (spec installation, requirement "Migration der Konfiguration").
    /// </summary>
    public AppsConfig ToCurrentVersion() => Version == CurrentVersion
        ? this
        : this with
        {
            Version = CurrentVersion,
            Apps = [.. Apps.Select(a => a with { AllowChildProcesses = null, ChildProcesses = a.ChildProcessMode })],
        };
}

/// <summary>A helper program an application may start in mode allowlist (design D1).</summary>
public sealed record AllowedChild
{
    [JsonRequired]
    public string Exe { get; init; } = string.Empty;

    [JsonRequired]
    public IntegrityCheck Check { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignerThumbprint { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sha256 { get; init; }

    /// <summary>Confirmation that a shell or script host is allowed on purpose (design D3).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowShell { get; init; }
}

/// <summary>One released application.</summary>
public sealed record AppEntry
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public string Exe { get; init; } = string.Empty;

    [JsonRequired]
    public string WorkingDir { get; init; } = string.Empty;

    [JsonRequired]
    public IntegrityCheck Check { get; init; }

    public string? SignerThumbprint { get; init; }

    public string? Sha256 { get; init; }

    /// <summary>Version 1 only: false = blocked, true = all. Use <see cref="ChildProcessMode"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowChildProcesses { get; init; }

    /// <summary>Version 2: blocked (also when missing), allowlist or all.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChildProcessMode? ChildProcesses { get; init; }

    /// <summary>Version 2, mode allowlist only: at least one entry.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AllowedChild>? AllowedChildren { get; init; }

    /// <summary>The effective mode for both versions (version 1: allowChildProcesses false = blocked, true = all).</summary>
    [JsonIgnore]
    public ChildProcessMode ChildProcessMode =>
        ChildProcesses ?? (AllowChildProcesses == true ? ChildProcessMode.All : ChildProcessMode.Blocked);

    public string? DataDir { get; init; }

    public string? RegistryKey { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppsConfig))]
internal sealed partial class AppsConfigJsonContext : JsonSerializerContext;
