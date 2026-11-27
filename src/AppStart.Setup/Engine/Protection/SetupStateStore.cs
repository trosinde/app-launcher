using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AppStart.Core;

namespace AppStart.Setup.Engine.Protection;

/// <summary>
/// Loads and saves &lt;base&gt;\setup-state.json (what setup changed and uninstall must undo). Written through
/// <see cref="IProtectionService.WriteProtectedFile"/>, so it is protected like apps.json.
/// </summary>
public sealed class SetupStateStore : ISetupStateStore
{
    public const string FileName = "setup-state.json";

    // Strict like apps.json: every property must be present (setup always writes all of them), unknown
    // properties and nulls are errors. SetupState lives in Contracts.cs, so "required" is added here.
    private static readonly JsonSerializerOptions Options = new(SetupStateJsonContext.Default.Options)
    {
        TypeInfoResolver = SetupStateJsonContext.Default.WithAddedModifier(RequireAllProperties),
    };

    private readonly IProtectionService protection;

    public SetupStateStore(IProtectionService protection, string? baseDirectory = null)
    {
        this.protection = protection ?? throw new ArgumentNullException(nameof(protection));
        Path = System.IO.Path.Combine(baseDirectory ?? AppStartConstants.DataDirectory, FileName);
    }

    public string Path { get; }

    /// <summary>Missing file = nothing set up yet (empty state).</summary>
    public SetupState Load()
    {
        if (!File.Exists(Path))
        {
            return new SetupState();
        }

        return Parse(File.ReadAllText(Path), Path);
    }

    public void Save(SetupState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        protection.WriteProtectedFile(Path, Serialize(state));
    }

    internal static string Serialize(SetupState state) =>
        JsonSerializer.Serialize(state, (JsonTypeInfo<SetupState>)Options.GetTypeInfo(typeof(SetupState)));

    internal static SetupState Parse(string json, string source = FileName)
    {
        SetupState? state;
        try
        {
            state = JsonSerializer.Deserialize(json, (JsonTypeInfo<SetupState>)Options.GetTypeInfo(typeof(SetupState)));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is invalid: {ex.Message}", ex);
        }

        if (state is null)
        {
            throw new InvalidDataException($"{source} is empty.");
        }

        if (state.Version != 1)
        {
            throw new InvalidDataException($"{source} has the unknown version {state.Version}.");
        }

        if (state.AclBackups.Any(b => !Enum.IsDefined(b.Target) || b.Path.Length == 0 || b.OriginalSddl.Length == 0))
        {
            throw new InvalidDataException($"{source} contains an invalid permissions backup.");
        }

        return state;
    }

    private static void RequireAllProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(SetupState))
        {
            foreach (var property in typeInfo.Properties)
            {
                property.IsRequired = true;
            }
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SetupState))]
internal sealed partial class SetupStateJsonContext : JsonSerializerContext;
