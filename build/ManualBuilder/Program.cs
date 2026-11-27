using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

// ManualBuilder --input <docs/manual> --output <file.html> [--version <x.y.z>] [--date <dd.MM.yyyy>]
//
// Joins index.md (title page, everything before "## Contents") and the chapters NN-*.md into one standalone HTML
// for A4 printing: generated table of contents, page break per chapter, images embedded as data URIs, missing
// screenshots as visible placeholders, links between chapters rewritten to anchors inside the document.
// Exit code 0 = written, 1 = error, 2 = wrong usage.
namespace AppStart.ManualBuilder;

public static partial class Program
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static int Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: ManualBuilder --input <folder> --output <file.html> [--version <x.y.z>] [--date <dd.MM.yyyy>]");
            return 2;
        }

        try
        {
            var result = Build(options.Value.Input, options.Value.Version, options.Value.Date);
            var output = Path.GetFullPath(options.Value.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, result.Html, new UTF8Encoding(false));
            Console.WriteLine($"Manual: {output} ({result.Chapters} chapters, {result.Images} images, {result.Placeholders} placeholders)");
            foreach (var missing in result.MissingImages)
            {
                Console.WriteLine($"  Placeholder: {missing}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public sealed record BuildResult(string Html, int Chapters, int Images, int Placeholders, IReadOnlyList<string> MissingImages);

    public static BuildResult Build(string inputDir, string version, string date)
    {
        var index = Path.Combine(inputDir, "index.md");
        if (!File.Exists(index))
        {
            throw new FileNotFoundException($"{index} is missing.");
        }

        var chapters = Directory.EnumerateFiles(inputDir, "*.md")
            .Where(f => ChapterFile().IsMatch(Path.GetFileName(f)))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();
        if (chapters.Count == 0)
        {
            throw new InvalidDataException($"No chapters (NN-name.md) in {inputDir}.");
        }

        var images = new ImageEmbedder(inputDir);

        // Title page: index.md up to "## Contents" (German manual: "## Inhalt") (the Markdown table of contents is for reading on GitHub).
        var indexText = File.ReadAllText(index);
        var cut = Regex.Match(indexText, @"^##\s+(?:Contents|Inhalt)\s*$", RegexOptions.Multiline);
        var titleMarkdown = cut.Success ? indexText[..cut.Index] : indexText;
        var titleHtml = images.Embed(Markdown.ToHtml(titleMarkdown, Pipeline));
        var title = Regex.Match(titleMarkdown, @"^#\s+(.+)$", RegexOptions.Multiline) is { Success: true } t ? t.Groups[1].Value.Trim() : "Manual";

        var toc = new StringBuilder();
        var body = new StringBuilder();
        foreach (var file in chapters)
        {
            var slug = Path.GetFileNameWithoutExtension(file);
            var document = Markdown.Parse(File.ReadAllText(file), Pipeline);

            foreach (var heading in document.Descendants<HeadingBlock>().Where(h => h.Level <= 2))
            {
                var id = heading.GetAttributes().Id;
                var target = heading.Level == 1 || id is null ? slug : $"{slug}--{id}";
                toc.Append(CultureInfo.InvariantCulture, $"<li class=\"toc-{heading.Level}\"><a href=\"#{target}\">{WebUtility.HtmlEncode(InlineText(heading.Inline))}</a></li>\n");
            }

            var html = Markdown.ToHtml(document, Pipeline);
            html = RewriteAnchors(html, slug);
            html = images.Embed(html);
            body.Append(CultureInfo.InvariantCulture, $"<section class=\"chapter\" id=\"{slug}\">\n{html}</section>\n");
        }

        var page = Template
            .Replace("{{title}}", WebUtility.HtmlEncode(title), StringComparison.Ordinal)
            .Replace("{{css}}", Css, StringComparison.Ordinal)
            .Replace("{{titlepage}}", titleHtml, StringComparison.Ordinal)
            .Replace("{{version}}", WebUtility.HtmlEncode(version), StringComparison.Ordinal)
            .Replace("{{date}}", WebUtility.HtmlEncode(date), StringComparison.Ordinal)
            .Replace("{{toc}}", toc.ToString(), StringComparison.Ordinal)
            .Replace("{{body}}", body.ToString(), StringComparison.Ordinal);
        return new BuildResult(page, chapters.Count, images.Embedded, images.Missing.Count, images.Missing);
    }

    /// <summary>
    /// Makes ids unique across chapters ("x" → "slug--x") and rewrites links: "#x" → "#slug--x",
    /// "07-meldungen.md#x" → "#07-meldungen--x", "07-meldungen.md" → "#07-meldungen".
    /// </summary>
    public static string RewriteAnchors(string html, string slug)
    {
        html = IdAttribute().Replace(html, m => $"{m.Groups[1].Value}id=\"{slug}--{m.Groups[2].Value}\"");
        return HrefAttribute().Replace(html, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value);
            string? rewritten = null;
            if (href.StartsWith('#'))
            {
                rewritten = $"#{slug}--{href[1..]}";
            }
            else if (LocalChapterLink().Match(href) is { Success: true } link)
            {
                rewritten = link.Groups[2].Success ? $"#{link.Groups[1].Value}--{link.Groups[2].Value}" : $"#{link.Groups[1].Value}";
            }

            return rewritten is null ? m.Value : $"href=\"{WebUtility.HtmlEncode(rewritten)}\"";
        });
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in inline.Descendants<Inline>())
        {
            switch (item)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
            }
        }

        return sb.ToString();
    }

    private static (string Input, string Output, string Version, string Date)? ParseArgs(string[] args)
    {
        string? input = null, output = null, version = null, date = null;
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--input": input = args[i + 1]; break;
                case "--output": output = args[i + 1]; break;
                case "--version": version = args[i + 1]; break;
                case "--date": date = args[i + 1]; break;
                default: return null;
            }
        }

        if (args.Length % 2 != 0 || input is null || output is null)
        {
            return null;
        }

        return (input, output, version ?? "–", date ?? DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"^\d{2}-[a-z0-9-]+\.md$")]
    private static partial Regex ChapterFile();

    [GeneratedRegex(@"(\s)id=""([^""]+)""")]
    private static partial Regex IdAttribute();

    [GeneratedRegex(@"href=""([^""]*)""")]
    private static partial Regex HrefAttribute();

    [GeneratedRegex(@"^(\d{2}-[a-z0-9-]+|index)\.md(?:#(.+))?$")]
    private static partial Regex LocalChapterLink();

    private const string Template = """
        <!doctype html>
        <html lang="en-US">
        <head>
        <meta charset="utf-8">
        <title>{{title}}</title>
        <style>
        {{css}}
        </style>
        </head>
        <body>
        <section class="titlepage">
        {{titlepage}}
        <p class="edition">Version {{version}} · As of {{date}}</p>
        </section>
        <nav class="toc">
        <h1>Contents</h1>
        <ol>
        {{toc}}</ol>
        </nav>
        {{body}}
        </body>
        </html>
        """;

    private const string Css = """
        @page {
          size: A4;
          margin: 20mm 18mm 22mm 20mm;
          @bottom-left { content: "AppStart – User manual"; font: 8pt "Segoe UI", Arial, sans-serif; color: #666; }
          @bottom-right { content: "Page " counter(page) " of " counter(pages); font: 8pt "Segoe UI", Arial, sans-serif; color: #666; }
        }
        @page :first { @bottom-left { content: none; } @bottom-right { content: none; } }
        html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
        body { font: 10pt/1.45 "Segoe UI", Arial, sans-serif; color: #1b1b1b; margin: 0; }
        h1, h2, h3, h4 { color: #0f3d6e; line-height: 1.25; break-after: avoid; page-break-after: avoid; }
        h1 { font-size: 20pt; margin: 0 0 6mm; padding-bottom: 2mm; border-bottom: 2px solid #0f5299; }
        h2 { font-size: 14pt; margin: 8mm 0 3mm; }
        h3 { font-size: 11.5pt; margin: 6mm 0 2mm; }
        h4 { font-size: 10pt; margin: 4mm 0 1.5mm; }
        p, ul, ol { margin: 0 0 2.5mm; }
        a { color: #0f5299; text-decoration: none; }
        code { font: 8.8pt Consolas, "Cascadia Mono", monospace; background: #f1f3f5; padding: 0 2px; border-radius: 2px; }
        pre { background: #f1f3f5; border-left: 3px solid #0f5299; padding: 2.5mm 3mm; white-space: pre-wrap; word-break: break-word; break-inside: avoid; }
        pre code { background: none; padding: 0; }
        table { border-collapse: collapse; width: 100%; margin: 2mm 0 4mm; font-size: 9pt; }
        thead { display: table-header-group; }
        tr { break-inside: avoid; page-break-inside: avoid; }
        th, td { border: 1px solid #c9d1d9; padding: 1.2mm 2mm; text-align: left; vertical-align: top; }
        th { background: #e8eef5; }
        td code { overflow-wrap: break-word; }
        blockquote { margin: 3mm 0; padding: 2mm 4mm; border-left: 4px solid #d4a017; background: #fff8e1; break-inside: avoid; }
        blockquote p:last-child { margin-bottom: 0; }
        img { max-width: 100%; }
        p > img:only-child { display: block; margin: 3mm auto; max-height: 120mm; }
        .placeholder { display: block; margin: 3mm 0; padding: 8mm 4mm; border: 1.5px dashed #9aa5b1; background: #f7f9fb; color: #52606d; text-align: center; font-size: 9pt; break-inside: avoid; }
        .placeholder strong { display: block; color: #0f3d6e; }
        .titlepage { height: 250mm; display: flex; flex-direction: column; justify-content: center; }
        .titlepage h1 { font-size: 30pt; border: none; margin-bottom: 4mm; }
        .titlepage .edition { margin-top: 12mm; color: #52606d; }
        .toc { break-before: page; }
        .toc ol { list-style: none; padding: 0; }
        .toc li.toc-1 { font-weight: 600; margin-top: 3mm; }
        .toc li.toc-2 { margin-left: 6mm; }
        .chapter { break-before: page; }
        """;
}

/// <summary>Replaces local &lt;img&gt; sources with data URIs; a missing file becomes a placeholder box (screenshots of task 10.2).</summary>
public sealed partial class ImageEmbedder(string baseDir)
{
    private readonly List<string> _missing = [];

    public int Embedded { get; private set; }

    public IReadOnlyList<string> Missing => _missing;

    public string Embed(string html) => ImgTag().Replace(html, m =>
    {
        var src = WebUtility.HtmlDecode(m.Groups["src"].Value);
        if (src.Contains("://", StringComparison.Ordinal) || src.StartsWith("data:", StringComparison.Ordinal))
        {
            return m.Value;
        }

        var alt = AltAttribute().Match(m.Value) is { Success: true } a ? WebUtility.HtmlDecode(a.Groups[1].Value) : string.Empty;
        var path = Path.GetFullPath(Path.Combine(baseDir, src));
        if (!File.Exists(path))
        {
            if (!_missing.Contains(src))
            {
                _missing.Add(src);
            }

            return $"<span class=\"placeholder\"><strong>{WebUtility.HtmlEncode(alt)}</strong>Screenshot to follow: {WebUtility.HtmlEncode(Path.GetFileName(src))}</span>";
        }

        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            _ => throw new InvalidDataException($"Image format not supported: {src}"),
        };
        Embedded++;
        var data = Convert.ToBase64String(File.ReadAllBytes(path));
        return m.Value.Replace(m.Groups["src"].Value, $"data:{mime};base64,{data}", StringComparison.Ordinal);
    });

    [GeneratedRegex(@"<img\b[^>]*\bsrc=""(?<src>[^""]+)""[^>]*>")]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"\balt=""([^""]*)""")]
    private static partial Regex AltAttribute();
}
