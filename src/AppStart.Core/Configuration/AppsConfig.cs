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

/// <summary>Content of C:\ProgramData\AppStart\apps.json.</summary>
public sealed record AppsConfig
{
    public const int CurrentVersion = 1;

    [JsonRequired]
    public int Version { get; init; }

    [JsonRequired]
    public IReadOnlyList<AppEntry> Apps { get; init; } = [];

    public AppEntry? Find(string appId) =>
        Apps.FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.Ordinal));
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

    public bool AllowChildProcesses { get; init; }

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
