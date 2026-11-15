using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace AppStart.Documentation.Tests;

/// <summary>
/// The manual (docs/manual/*.md) as the reader sees it: Markdown rendered with the same pipeline as
/// build/ManualBuilder, tags removed, entities decoded, whitespace collapsed. Comparing against the rendered
/// text (not the source) means Markdown escapes such as <c>\|</c> or <c>\,</c> cannot hide or fake a text.
/// </summary>
public sealed partial class Manual
{
    private static readonly Lazy<Manual> Instance = new(Load);

    private Manual(string directory, IReadOnlyDictionary<string, string> sources)
    {
        Directory = directory;
        Sources = sources;
        Plain = sources.ToDictionary(kv => kv.Key, kv => ToPlainText(kv.Value), StringComparer.OrdinalIgnoreCase);
        AllPlain = string.Join(" ", Plain.Values);
    }

    public static Manual Current => Instance.Value;

    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>Folder that holds index.md and the chapters.</summary>
    public string Directory { get; }

    /// <summary>File name (e.g. "07-meldungen.md") → Markdown source.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>File name → rendered, normalised text.</summary>
    public IReadOnlyDictionary<string, string> Plain { get; }

    public string AllPlain { get; }

    /// <summary>Normalised text of one chapter; the file is found by its prefix, e.g. "07-".</summary>
    public string Chapter(string prefix) => Plain[ChapterFile(prefix)];

    public string ChapterSource(string prefix) => Sources[ChapterFile(prefix)];

    public string ChapterFile(string prefix) =>
        Sources.Keys.SingleOrDefault(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Kapitel «{prefix}*.md» fehlt in {Directory}.");

    /// <summary>Collapses all whitespace (incl. line breaks) to single spaces, so wrapping never matters.</summary>
    public static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    public static string ToPlainText(string markdown)
    {
        var html = Markdown.ToHtml(markdown, Pipeline);
        html = HtmlComment().Replace(html, " ");
        html = InlineTag().Replace(html, string.Empty);
        html = AnyTag().Replace(html, " ");
        return Normalize(WebUtility.HtmlDecode(html));
    }

    /// <summary>
    /// Source lines of the section that starts with the first heading matching <paramref name="isHeading"/>,
    /// up to the next heading of the same or a higher level. Null when no heading matches.
    /// </summary>
    public static string? Section(string markdown, Func<string, bool> isHeading)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (inFence || HeadingLevel(lines[i]) is not { } level || !isHeading(lines[i]))
            {
                continue;
            }

            var end = i + 1;
            var fence = false;
            for (; end < lines.Length; end++)
            {
                if (lines[end].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    fence = !fence;
                }

                if (!fence && HeadingLevel(lines[end]) is { } next && next <= level)
                {
                    break;
                }
            }

            return string.Join('\n', lines[i..end]);
        }

        return null;
    }

    public static int? HeadingLevel(string line)
    {
        var m = HeadingLine().Match(line);
        return m.Success ? m.Groups[1].Length : null;
    }

    private static Manual Load()
    {
        var directory = FindManualDirectory();
        var sources = System.IO.Directory.EnumerateFiles(directory, "*.md")
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        return new Manual(directory, sources);
    }

    // Published test EXE: manual\ next to it (csproj copies docs/manual). dotnet test in the repo: walk up to docs/manual.
    private static string FindManualDirectory()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "manual");
        if (File.Exists(Path.Combine(local, "index.md")))
        {
            return local;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "manual");
            if (File.Exists(Path.Combine(candidate, "index.md")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"docs/manual/index.md nicht gefunden (gesucht ab {AppContext.BaseDirectory}).");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    // Inline markup disappears without a gap ("<code>verify</code>." stays "verify."), block tags become a space.
    [GeneratedRegex(@"</?(code|em|strong|a|span|kbd|del|sup|sub|mark)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"^(#{1,6})\s")]
    private static partial Regex HeadingLine();
}
