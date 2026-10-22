#if NET10_0_OR_GREATER
using System.Text;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MarkupAudit;

namespace MewUI.Test.Markup;

/// <summary>Fails when docs/markup or the guide snippet blocks drift from the compiled extension methods and DocSnippets.cs.</summary>
[TestClass]
public sealed class MarkupReferenceDocsTests
{
    private const string SOLUTION_FILE = "MewUI.Dev.slnx";
    private const string DOCS_DIRECTORY = "docs/markup";
    private const string SNIPPETS_FILE = "tests/MewUI.Test/Markup/DocSnippets.cs";
    private const string DEFAULT_EXCLUDED_TYPE = "Aprillz.MewUI.Controls.ControlTemplate";
    private const string SNIPPET_MARKER = "<!-- snippet:";
    private const string REGENERATE_HINT =
        "Regenerate with: dotnet run --project tools/MewUI.MarkupAudit/MewUI.MarkupAudit -- " +
        "src/MewUI/bin/Debug/net10.0/Aprillz.MewUI.dll <every extensions/*/bin/Debug/<tfm>/Aprillz.MewUI.*.dll listed in " +
        "MarkupReferenceDocsTests._extensionAssemblies> --docs docs/markup " +
        "--snippets tests/MewUI.Test/Markup/DocSnippets.cs --guide docs/CSharpMarkup.md --guide docs/CSharpMarkup.ko.md";

    // Extension packages with their own reference file; an absent build output skips that file instead of failing.
    private static readonly string[] _extensionAssemblies =
    [
        "extensions/MewUI.MewDock/bin/Debug/net10.0/Aprillz.MewUI.MewDock.dll",
        "extensions/MewUI.Markdown/bin/Debug/net10.0/Aprillz.MewUI.Markdown.dll",
        "extensions/MewUI.Terminal/bin/Debug/net10.0/Aprillz.MewUI.Terminal.dll",
        "extensions/MewUI.MewvalonEdit/bin/Debug/net10.0/Aprillz.MewUI.MewvalonEdit.dll",
        "extensions/MewUI.Skia/bin/Debug/net10.0/Aprillz.MewUI.Skia.dll",
        "extensions/MewUI.WebView2.Win32/bin/Debug/net10.0-windows/Aprillz.MewUI.WebView2.Win32.dll",
        "extensions/MewUI.MewCharts/bin/Debug/net10.0/Aprillz.MewUI.MewCharts.dll",
    ];

    private static readonly string[] _guides = ["docs/CSharpMarkup.md", "docs/CSharpMarkup.ko.md"];

    [TestMethod]
    public void GeneratedReference_MatchesCommittedFiles()
    {
        var root = RepositoryRoot();
        var inputs = new List<string> { typeof(Element).Assembly.Location };
        var skippedFiles = new List<string>();
        foreach (var relative in _extensionAssemblies)
        {
            var path = Resolve(root, relative);
            if (File.Exists(path))
            {
                inputs.Add(path);
            }
            else
            {
                skippedFiles.Add(MarkupReferenceWriter.ExtensionFileName(Path.GetFileNameWithoutExtension(path)));
            }
        }

        var result = MarkupAuditor.Run(new AuditOptions
        {
            AssemblyPaths = inputs,
            ExcludedTypes = [DEFAULT_EXCLUDED_TYPE],
        });
        var generated = MarkupReferenceWriter.Render(result);
        var docsDirectory = Resolve(root, DOCS_DIRECTORY);
        var failures = new List<string>();

        foreach (var (file, content) in generated.Files)
        {
            var path = Path.Combine(docsDirectory, file);
            if (!File.Exists(path))
            {
                failures.Add($"{file}: not present in {DOCS_DIRECTORY}");
                continue;
            }

            var difference = FirstDifference(Normalize(File.ReadAllText(path)), content);
            if (difference != null)
            {
                failures.Add($"{file}: {difference}");
            }
        }

        foreach (var path in Directory.EnumerateFiles(docsDirectory, "*.md"))
        {
            var file = Path.GetFileName(path);
            if (!generated.Files.ContainsKey(file) && !skippedFiles.Contains(file) && MarkupReferenceWriter.IsGeneratedFile(path))
            {
                failures.Add($"{file}: generated file with no remaining group");
            }
        }

        AssertNone(failures, $"{DOCS_DIRECTORY} differs from the generated reference");
    }

    [TestMethod]
    public void GuideSnippets_MatchDocSnippetsRegions()
    {
        var root = RepositoryRoot();
        var regions = SnippetUpdater.ReadRegions(Resolve(root, SNIPPETS_FILE));
        var failures = new List<string>();

        foreach (var guide in _guides)
        {
            var text = Normalize(File.ReadAllText(Resolve(root, guide)));
            if (!text.Contains(SNIPPET_MARKER, StringComparison.Ordinal))
            {
                failures.Add($"{guide}: no snippet markers");
                continue;
            }

            var difference = FirstDifference(text, SnippetUpdater.Apply(text, regions, guide));
            if (difference != null)
            {
                failures.Add($"{guide}: {difference}");
            }
        }

        AssertNone(failures, $"guide snippet blocks differ from the #region bodies of {SNIPPETS_FILE}");
    }

    private static void AssertNone(List<string> failures, string header)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(header).Append(". ").Append(REGENERATE_HINT).Append('\n');
        foreach (var failure in failures)
        {
            builder.Append("  ").Append(failure).Append('\n');
        }

        Assert.Fail(builder.ToString());
    }

    private static string? FirstDifference(string committed, string generated)
    {
        var committedLines = committed.Split('\n');
        var generatedLines = generated.Split('\n');
        var shared = Math.Min(committedLines.Length, generatedLines.Length);

        for (var index = 0; index < shared; index++)
        {
            if (committedLines[index] != generatedLines[index])
            {
                return $"line {index + 1}: committed '{committedLines[index]}' but generated '{generatedLines[index]}'";
            }
        }

        if (committedLines.Length != generatedLines.Length)
        {
            return $"line {shared + 1}: committed has {committedLines.Length} lines but generated has {generatedLines.Length}";
        }

        return null;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string Resolve(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            {
                return directory.FullName;
            }
        }

        Assert.Inconclusive($"{SOLUTION_FILE} was not found above {AppContext.BaseDirectory}.");
        return string.Empty;
    }
}
#endif
