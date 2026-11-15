using System.Reflection;
using System.Text.RegularExpressions;

namespace AppStart.Documentation.Tests;

/// <summary>One visible text of the software: a <c>const string</c> of a text class.</summary>
public sealed record TextConstant(string Type, string Member, string Value)
{
    public string Key => $"{Type}.{Member}";
}

/// <summary>
/// Finds the text classes of the software by reflection: every static class named <c>*Texts</c> in the
/// AppStart assemblies next to the test (so a new class such as <c>BootstrapperTexts</c> is picked up as soon as
/// the project is referenced) plus the classes listed in <see cref="ExtraTextTypes"/>.
/// </summary>
public static partial class TextCatalog
{
    /// <summary>Text classes the test must find; guards against a reference getting lost.</summary>
    public static readonly string[] RequiredTextTypes =
    [
        "AppStart.Launcher.Texts",
        "AppStart.TestApp.Texts",
        "AppStart.Setup.ViewModels.SetupTexts",
        "AppStart.Setup.Cli.CliTexts",
        "AppStart.Setup.Engine.SetupEngineTexts",
        "AppStart.Setup.Engine.Verification.VerificationTexts",
        "AppStart.Bootstrapper.BootstrapperTexts",
    ];

    /// <summary>Classes with visible texts that are not named *Texts.</summary>
    public static readonly string[] ExtraTextTypes =
    [
        "AppStart.Setup.Cli.StartMenu", // Start menu folder and entries («AppStart – Einrichtung», ...)
    ];

    /// <summary>
    /// Texts that need not appear in the manual, each with its reason. Only purely technical values belong here;
    /// a real message never does.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Exceptions = new Dictionary<string, string>
    {
        ["AppStart.Setup.ViewModels.SetupTexts.NavDoneMark"] = "Häkchen « ✓» hinter erledigten Schritten der Navigation, kein Text.",
        ["AppStart.Setup.ViewModels.SetupTexts.DateFormat"] = "Formatmuster dd.MM.yyyy HH:mm; das Handbuch zeigt das Ergebnis (z.B. 15.11.2026 10:42).",
    };

    public static IReadOnlyList<Type> TextTypes()
    {
        var types = new List<Type>();
        foreach (var assembly in AppStartAssemblies())
        {
            Type[] all;
            try
            {
                all = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                all = ex.Types.Where(t => t is not null).ToArray()!;
            }

            types.AddRange(all.Where(t =>
                (t.IsAbstract && t.IsSealed && t.Name.EndsWith("Texts", StringComparison.Ordinal))
                || ExtraTextTypes.Contains(t.FullName)));
        }

        return types.DistinctBy(t => t.FullName).OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<TextConstant> Constants() =>
        TextTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
                .Select(f => new TextConstant(t.FullName!, f.Name, (string)f.GetRawConstantValue()!)))
            .ToList();

    /// <summary>
    /// What must appear in the manual for one text: the whole text, or for format strings («{0} von {1}») every
    /// literal fragment of at least <paramref name="minFragment"/> characters.
    /// </summary>
    public static IReadOnlyList<string> RequiredFragments(string value, int minFragment = 6)
    {
        if (!Placeholder().IsMatch(value))
        {
            return [Manual.Normalize(value)];
        }

        return Placeholder().Split(value)
            .Select(Manual.Normalize)
            .Where(f => f.Length >= minFragment)
            .ToList();
    }

    public static IEnumerable<Assembly> AppStartAssemblies()
    {
        var self = typeof(TextCatalog).Assembly.GetName().Name;
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "AppStart*.dll"))
        {
            var name = AssemblyName.GetAssemblyName(file);
            if (string.Equals(name.Name, self, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.Load(name);
            }
            catch (BadImageFormatException)
            {
                continue; // native AOT leftovers
            }

            yield return assembly;
        }
    }

    // {0}, {1:dd.MM.yyyy HH:mm}, {2,-20}, {2:X8}
    [GeneratedRegex(@"\{\d+(?:,-?\d+)?(?::[^}]*)?\}")]
    private static partial Regex Placeholder();
}
