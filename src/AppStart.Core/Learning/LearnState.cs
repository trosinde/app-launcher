using System.Text.Json;
using System.Text.Json.Serialization;
using AppStart.Core.Configuration;
using AppStart.Core.Security;

namespace AppStart.Core.Learning;

/// <summary>
/// Content of C:\ProgramData\AppStart\learn.json: which applications are in learn mode until when
/// (add-allowed-child-processes, design D3). Written by the setup only, read by the service on every
/// start request with the same protection check as apps.json.
/// </summary>
public sealed record LearnState
{
    public const int CurrentVersion = 1;
    public const int DefaultMinutes = 30;
    public const int MinMinutes = 5;
    public const int MaxMinutes = 120;

    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;

    [JsonRequired]
    public IReadOnlyList<LearnSession> Apps { get; init; } = [];

    /// <summary>The learn session of <paramref name="appId"/> that is still running at <paramref name="nowUtc"/>, else null.</summary>
    public LearnSession? ActiveFor(string appId, DateTimeOffset nowUtc) =>
        Apps.FirstOrDefault(s => string.Equals(s.AppId, appId, StringComparison.Ordinal) && s.IsActive(nowUtc));

    /// <summary>Starts (or restarts) learn mode for one application; other sessions stay.</summary>
    public LearnState Start(string appId, int minutes, DateTimeOffset nowUtc)
    {
        if (minutes is < MinMinutes or > MaxMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, $"Allowed are {MinMinutes} to {MaxMinutes} minutes.");
        }

        var session = new LearnSession { AppId = appId, StartedUtc = nowUtc, ExpiresUtc = nowUtc.AddMinutes(minutes) };
        return this with { Apps = [.. Apps.Where(s => s.AppId != appId), session] };
    }

    /// <summary>Ends learn mode for one application at once.</summary>
    public LearnState Stop(string appId) => this with { Apps = [.. Apps.Where(s => s.AppId != appId)] };
}

/// <summary>Learn mode of one application.</summary>
public sealed record LearnSession
{
    [JsonRequired]
    public string AppId { get; init; } = string.Empty;

    [JsonRequired]
    public DateTimeOffset StartedUtc { get; init; }

    [JsonRequired]
    public DateTimeOffset ExpiresUtc { get; init; }

    public bool IsActive(DateTimeOffset nowUtc) => nowUtc >= StartedUtc && nowUtc < ExpiresUtc;
}

/// <summary>Result of reading learn.json for the service.</summary>
/// <param name="State">The valid state; an empty state when the file does not exist.</param>
/// <param name="Error">Why the file is ignored (unprotected or invalid); null when fine or missing.</param>
/// <param name="Unprotected">True when operators could change the file or its folder (spec: error event).</param>
public sealed record LearnStateLoadResult(LearnState State, string? Error, bool Unprotected)
{
    public bool IsValid => Error is null;
}

public static class LearnStateSerializer
{
    public static LearnState Empty { get; } = new();

    /// <summary>Parses learn.json. Errors name the field, like apps.json.</summary>
    public static (LearnState? State, string? Error) Parse(string json)
    {
        LearnState? state;
        try
        {
            state = JsonSerializer.Deserialize(json, LearningJsonContext.Default.LearnState);
        }
        catch (JsonException ex)
        {
            return (null, $"learn.json is not valid JSON ({ex.Path}): {ex.Message}");
        }

        if (state is null)
        {
            return (null, "learn.json is empty.");
        }

        var error = Validate(state);
        return error is null ? (state, null) : (null, error);
    }

    public static string Serialize(LearnState state)
    {
        var error = Validate(state);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(state));
        }

        return JsonSerializer.Serialize(state, LearningJsonContext.Default.LearnState);
    }

    public static string? Validate(LearnState state)
    {
        if (state.Version != LearnState.CurrentVersion)
        {
            return $"version: {state.Version} is not supported, expected {LearnState.CurrentVersion}.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < state.Apps.Count; i++)
        {
            var s = state.Apps[i];
            var at = $"apps[{i}]";
            if (!AppId.IsValid(s.AppId))
            {
                return $"{at}.appId: \"{s.AppId}\" is invalid.";
            }

            if (!seen.Add(s.AppId))
            {
                return $"{at}.appId: \"{s.AppId}\" occurs more than once.";
            }

            var minutes = (s.ExpiresUtc - s.StartedUtc).TotalMinutes;
            if (minutes is < LearnState.MinMinutes or > LearnState.MaxMinutes)
            {
                return $"{at}.expiresUtc: duration {minutes:F0} minutes, allowed are {LearnState.MinMinutes} to {LearnState.MaxMinutes}.";
            }
        }

        return null;
    }
}

/// <summary>Reads learn.json for the service (spec app-launch, requirement "Learning mode").</summary>
public static class LearnStateStore
{
    /// <summary>
    /// Missing file: no learn mode, no error. Unprotected file or folder, or invalid content: the
    /// file is treated as not present and <see cref="LearnStateLoadResult.Error"/> says why (fail-closed:
    /// the allowlist applies).
    /// </summary>
    public static LearnStateLoadResult Load(string? path = null)
    {
        path ??= AppStartConstants.LearnStatePath;
        if (!File.Exists(path))
        {
            return new(LearnStateSerializer.Empty, null, false);
        }

        foreach (var target in new[] { Path.GetDirectoryName(path)!, path })
        {
            var violations = WriteAccessPolicy.FindViolations(target);
            if (violations.Count > 0)
            {
                return new(LearnStateSerializer.Empty, $"{target} is not protected: {string.Join(" ", violations)}", true);
            }
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return new(LearnStateSerializer.Empty, $"{path} cannot be read: {ex.Message}", false);
        }

        var (state, error) = LearnStateSerializer.Parse(json);
        return state is null ? new(LearnStateSerializer.Empty, error, false) : new(state, null, false);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    WriteIndented = true)]
[JsonSerializable(typeof(LearnState))]
[JsonSerializable(typeof(LearnRecording))]
internal sealed partial class LearningJsonContext : JsonSerializerContext;
