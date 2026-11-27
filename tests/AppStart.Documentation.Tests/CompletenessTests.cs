using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AppStart.Service;
using AppStart.Setup.Engine.Verification;

namespace AppStart.Documentation.Tests;

/// <summary>
/// Task 10.4 / spec documentation "Completeness": every message, check point and command of the software is
/// described in the manual. Failures name the missing text and where it comes from.
/// </summary>
public sealed partial class CompletenessTests
{
    private static Manual M => Manual.Current;

    [Fact]
    public void TextClassesAreFound()
    {
        var found = TextCatalog.TextTypes().Select(t => t.FullName).ToHashSet();
        var missing = TextCatalog.RequiredTextTypes.Concat(TextCatalog.ExtraTextTypes).Where(t => !found.Contains(t)).ToList();
        Assert.True(missing.Count == 0, "Text classes not found (project reference missing?): " + string.Join(", ", missing));
    }

    [Fact]
    public void ExceptionsReferToExistingTexts()
    {
        var keys = TextCatalog.Constants().Select(c => c.Key).ToHashSet();
        var stale = TextCatalog.Exceptions.Keys.Where(k => !keys.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Exception list contains texts that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryTextAppearsInTheManual()
    {
        var report = new StringBuilder();
        foreach (var text in TextCatalog.Constants().Where(c => !TextCatalog.Exceptions.ContainsKey(c.Key)))
        {
            foreach (var fragment in TextCatalog.RequiredFragments(text.Value).Where(f => !M.AllPlain.Contains(f, StringComparison.Ordinal)))
            {
                report.AppendLine($"  {text.Key}: \"{fragment}\"");
            }
        }

        Assert.True(report.Length == 0, "These texts of the software are missing in the manual (docs/manual):\n" + report);
    }

    [Fact]
    public void LauncherMessagesAreInChapterMessages()
    {
        var chapter = M.Chapter("07-");
        var missing = TextCatalog.Constants()
            .Where(c => c.Type == "AppStart.Launcher.Texts")
            .SelectMany(c => TextCatalog.RequiredFragments(c.Value).Select(f => (c.Key, Fragment: f)))
            .Where(x => !chapter.Contains(x.Fragment, StringComparison.Ordinal))
            .Select(x => $"  {x.Key}: \"{x.Fragment}\"")
            .ToList();
        Assert.True(missing.Count == 0, "Launcher messages missing in the chapter \"Messages and troubleshooting\":\n" + string.Join('\n', missing));
    }

    [Fact]
    public void EveryEventIdHasARowInChapterMessages()
    {
        var source = M.ChapterSource("07-");
        var missing = EventIds.All
            .Where(e => !Regex.IsMatch(source, $@"^\|\s*{e.Id}\s*\|", RegexOptions.Multiline))
            .Select(e => $"  {e.Id} ({e.Name})")
            .ToList();
        Assert.True(missing.Count == 0, "Event IDs without table row \"| <ID> |\" in the chapter \"Messages and troubleshooting\":\n" + string.Join('\n', missing));
    }

    [Fact]
    public void EveryCommandAndOptionIsInChapterReference()
    {
        var root = BuildCommandLine();
        var source = M.ChapterSource("09-");
        var chapter = M.Chapter("09-");
        var problems = new List<string>();

        // Options of the root command (--help, --version ...), with all aliases.
        foreach (var option in root.Options)
        {
            foreach (var name in option.Aliases.Prepend(option.Name).Distinct())
            {
                if (!chapter.Contains(name, StringComparison.Ordinal))
                {
                    problems.Add($"  Root option \"{name}\" missing in the chapter Reference");
                }
            }
        }

        foreach (var (command, path) in AllCommands(root, string.Empty))
        {
            // Subcommands by their full path (`learn apply`), so equal names under different parents stay apart.
            var heading = $"`{path}`";
            var section = Manual.Section(source, line => line.Contains(heading, StringComparison.Ordinal));
            if (section is null)
            {
                problems.Add($"  Command \"{command.Name}\": no heading with {heading} in the chapter Reference");
                continue;
            }

            var text = Manual.ToPlainText(section);
            if (command.Description is { Length: > 0 } description && !text.Contains(Manual.Normalize(description), StringComparison.Ordinal))
            {
                problems.Add($"  Command \"{command.Name}\": description \"{description}\" missing in its section");
            }

            foreach (var option in command.Options)
            {
                foreach (var name in option.Aliases.Prepend(option.Name).Distinct().Where(n => !text.Contains(n, StringComparison.Ordinal)))
                {
                    problems.Add($"  Command \"{command.Name}\": option \"{name}\" missing in its section");
                }

                if (option.Description is { Length: > 0 } od && !text.Contains(Manual.Normalize(od), StringComparison.Ordinal))
                {
                    problems.Add($"  Command \"{command.Name}\", option \"{option.Name}\": description \"{od}\" missing in its section");
                }
            }
        }

        Assert.True(problems.Count == 0, "Command line incomplete in the manual:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void EveryCheckHasASectionInChapterVerification()
    {
        var source = M.ChapterSource("06-");
        var problems = new List<string>();
        foreach (var (id, title, expected) in CheckPoints())
        {
            var anchor = $"<a id=\"{id}\"></a>";
            var at = source.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0)
            {
                problems.Add($"  Check \"{title}\": anchor {anchor} missing (line directly before the heading)");
                continue;
            }

            var section = Manual.Section(source[at..], line => true);
            var headingLine = section?.Split('\n')[0] ?? string.Empty;
            if (section is null || !headingLine.Contains(title, StringComparison.Ordinal))
            {
                problems.Add($"  Check \"{title}\": the anchor {anchor} is not followed by a heading with \"{title}\"");
                continue;
            }

            if (!Manual.ToPlainText(section).Contains(Manual.Normalize(expected), StringComparison.Ordinal))
            {
                problems.Add($"  Check \"{title}\": expected \"{expected}\" missing in its section");
            }
        }

        Assert.True(problems.Count == 0, "Checks incomplete in the chapter \"Verification after a service visit\":\n" + string.Join('\n', problems));
    }

    [Fact]
    public void EveryImageExistsOrIsListedAsMissingScreenshot()
    {
        var listing = File.ReadAllText(Path.Combine(M.Directory, "images", "README.md"));
        var problems = new List<string>();
        foreach (var (file, markdown) in M.Sources)
        {
            foreach (Match m in ImageReference().Matches(markdown))
            {
                var path = m.Groups[1].Value;
                var name = Path.GetFileName(path);
                if (!File.Exists(Path.Combine(M.Directory, path)) && !listing.Contains($"`{name}`", StringComparison.Ordinal))
                {
                    problems.Add($"  {file}: image {path} is missing and not listed in images/README.md");
                }
            }
        }

        Assert.True(problems.Count == 0, "Images:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void LinksBetweenChaptersResolve()
    {
        var problems = new List<string>();
        foreach (var (file, markdown) in M.Sources)
        {
            foreach (Match m in ChapterLink().Matches(markdown))
            {
                if (!M.Sources.ContainsKey(m.Groups[1].Value))
                {
                    problems.Add($"  {file}: link to {m.Groups[1].Value} without file");
                }
            }
        }

        Assert.True(problems.Count == 0, "Links:\n" + string.Join('\n', problems));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Command tree of AppStart.Setup (internal CommandLine.Build, via reflection; the engine factory is never called).</summary>
    private static RootCommand BuildCommandLine()
    {
        var type = typeof(VerificationTexts).Assembly.GetType("AppStart.Setup.Cli.CommandLine", throwOnError: true)!;
        var build = type.GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("AppStart.Setup.Cli.CommandLine.Build");
        return (RootCommand)build.Invoke(null, [null])!;
    }

    private static IEnumerable<(Command Command, string Path)> AllCommands(Command command, string prefix)
    {
        foreach (var sub in command.Subcommands)
        {
            var path = prefix.Length == 0 ? sub.Name : $"{prefix} {sub.Name}";
            yield return (sub, path);
            foreach (var deeper in AllCommands(sub, path))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Pairs Verifier.XCheckId with VerificationTexts.XTitle and XExpected (naming convention of the code).</summary>
    private static IEnumerable<(string Id, string Title, string Expected)> CheckPoints()
    {
        var ids = typeof(Verifier).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.EndsWith("CheckId", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(10, ids.Count);
        foreach (var field in ids)
        {
            var stem = field.Name[..^"CheckId".Length];
            string Text(string suffix) => (string)(typeof(VerificationTexts).GetField(stem + suffix)?.GetRawConstantValue()
                ?? throw new InvalidOperationException($"VerificationTexts.{stem}{suffix} is missing for Verifier.{field.Name}."));
            yield return ((string)field.GetRawConstantValue()!, Text("Title"), Text("Expected"));
        }
    }

    [GeneratedRegex(@"!\[[^\]]*\]\(([^)\s]+)")]
    private static partial Regex ImageReference();

    [GeneratedRegex(@"\]\(([0-9a-z][\w-]*\.md)(?:#[^)]*)?\)")]
    private static partial Regex ChapterLink();
}
