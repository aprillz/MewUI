using Aprillz.MewUI.MarkupAudit;

namespace Aprillz.MewUI.MarkupAudit.Cli;

/// <summary>Options of the documentation commands that replace the fluent audit when any of them is given.</summary>
internal sealed class DocCommandOptions
{
    public const string HELP =
        """

        Documentation commands (each replaces the audit; assembly paths are positional as above):
          --doc-coverage              List public types and members without an XML <summary>, grouped by assembly and namespace.
          --doc-coverage-scope <ns>   Limit --doc-coverage to a namespace and those below it.
                                      With --fail-on-missing, exit code 1 when the scoped count is not zero.
          --api-crosslink <api-dir>   Insert "Fluent:" lines into property/event pages and "Sets:" lines into extension
                                      method pages of xmldocmd output under <api-dir>; re-running replaces the lines.
          --api-index <api-dir>       Write <api-dir>/index.md (package table) and <api-dir>/<Assembly>/index.md (namespaces);
                                      <api-dir>/version.txt (MewUIVersion=, Commit=) is shown when present.
          --doc-review <ns>           Write a Markdown review table of every public member of the namespace, one type per
                                      section, with the summaries of the fluent counterparts side by side.
          --changed-since <git-ref>   Limit --doc-review to types declared in .cs files that 'git diff --name-only <git-ref>'
                                      or the untracked list names. Best effort: the files are scanned for class/struct/
                                      interface/enum/record names, so partial types and same-named types widen the set.
        """;

    public bool DocCoverage { get; private set; }

    public string? DocCoverageScope { get; private set; }

    public string? ApiCrosslinkDirectory { get; private set; }

    public string? ApiIndexDirectory { get; private set; }

    public string? DocReviewNamespace { get; private set; }

    public string? ChangedSince { get; private set; }

    /// <summary>True when any documentation command was given.</summary>
    public bool IsSet => DocCoverage || ApiCrosslinkDirectory != null || ApiIndexDirectory != null || DocReviewNamespace != null;

    /// <summary>Consumes one option (and its value when it takes one); false with a message when the option is unknown or lacks its value.</summary>
    public bool TryParse(string option, string[] arguments, ref int index, out string? error)
    {
        error = null;
        switch (option.ToLowerInvariant())
        {
            case "--doc-coverage":
                DocCoverage = true;
                return true;
            case "--doc-coverage-scope":
                return TakeValue(arguments, ref index, option, value => DocCoverageScope = value, out error);
            case "--api-crosslink":
                return TakeValue(arguments, ref index, option, value => ApiCrosslinkDirectory = value, out error);
            case "--api-index":
                return TakeValue(arguments, ref index, option, value => ApiIndexDirectory = value, out error);
            case "--doc-review":
                return TakeValue(arguments, ref index, option, value => DocReviewNamespace = value, out error);
            case "--changed-since":
                return TakeValue(arguments, ref index, option, value => ChangedSince = value, out error);
            default:
                error = $"Unknown option: {option}";
                return false;
        }
    }

    private static bool TakeValue(string[] arguments, ref int index, string option, Action<string> assign, out string? error)
    {
        if (index + 1 >= arguments.Length)
        {
            error = $"{option} requires a value.";
            return false;
        }

        index++;
        assign(arguments[index]);
        error = null;
        return true;
    }
}

/// <summary>Runs the documentation commands and returns the process exit code.</summary>
internal static class DocCommands
{
    private const string SOLUTION_FILE = "MewUI.Dev.slnx";

    public static int Run(DocCommandOptions options, IReadOnlyList<string> assemblyPaths, bool failOnMissing)
    {
        try
        {
            var exitCode = 0;
            if (options.DocCoverage)
            {
                exitCode = Math.Max(exitCode, RunCoverage(options, assemblyPaths, failOnMissing));
            }

            if (options.DocReviewNamespace != null)
            {
                exitCode = Math.Max(exitCode, RunReview(options, assemblyPaths));
            }

            if (options.ApiCrosslinkDirectory != null)
            {
                exitCode = Math.Max(exitCode, RunCrosslink(options.ApiCrosslinkDirectory, assemblyPaths));
            }

            if (options.ApiIndexDirectory != null)
            {
                exitCode = Math.Max(exitCode, RunIndex(options.ApiIndexDirectory, assemblyPaths));
            }

            return exitCode;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or BadImageFormatException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int RunCoverage(DocCommandOptions options, IReadOnlyList<string> assemblyPaths, bool failOnMissing)
    {
        var result = DocCoverageAnalyzer.Run(new DocCoverageOptions
        {
            AssemblyPaths = assemblyPaths,
            NamespacePrefix = options.DocCoverageScope,
        });

        DocCoverageAnalyzer.Write(result, options.DocCoverageScope, Console.Out);
        WriteWarnings(result.Warnings);

        var scope = string.IsNullOrEmpty(options.DocCoverageScope) ? "all namespaces" : options.DocCoverageScope;
        Console.Error.WriteLine(
            $"Scanned {result.Scanned} public types and members in {result.Assemblies.Count} assemblies ({scope}); " +
            $"{result.Missing.Count} without <summary>.");

        if (failOnMissing && result.Missing.Count > 0)
        {
            return 1;
        }

        return 0;
    }

    private static int RunReview(DocCommandOptions options, IReadOnlyList<string> assemblyPaths)
    {
        var warnings = new List<string>();
        IReadOnlySet<string>? typeNames = null;
        if (options.ChangedSince != null)
        {
            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                Console.Error.WriteLine($"--changed-since needs {SOLUTION_FILE} above the current directory to locate the repository.");
                return 2;
            }

            typeNames = SourceDeclarationMap.ChangedTypeNames(repositoryRoot, options.ChangedSince, warnings);
        }

        var summary = DocReviewWriter.Write(new DocReviewOptions
        {
            AssemblyPaths = assemblyPaths,
            NamespacePrefix = options.DocReviewNamespace!,
            TypeNames = typeNames,
        }, Console.Out);

        WriteWarnings(warnings.Concat(summary.Warnings));
        var changed = typeNames == null ? string.Empty : $" (limited to {typeNames.Count} type names changed since {options.ChangedSince})";
        Console.Error.WriteLine($"Listed {summary.Members} public members of {summary.Types} types in {options.DocReviewNamespace}{changed}.");
        return 0;
    }

    private static int RunCrosslink(string apiDirectory, IReadOnlyList<string> assemblyPaths)
    {
        if (!Directory.Exists(apiDirectory))
        {
            Console.Error.WriteLine($"API directory not found: {apiDirectory}");
            return 2;
        }

        var warnings = new List<string>();
        var summary = ApiCrosslinker.Run(apiDirectory, assemblyPaths, warnings);
        WriteWarnings(warnings);
        Console.Error.WriteLine(
            $"Cross-linked {summary.MemberPagesLinked} member pages and {summary.ExtensionPagesLinked} extension pages; " +
            $"{summary.MemberPagesMissing} member pages and {summary.ExtensionPagesMissing} extension pages were not generated; " +
            $"{summary.DroppedTargets} links dropped for absent target pages.");
        return 0;
    }

    private static int RunIndex(string apiDirectory, IReadOnlyList<string> assemblyPaths)
    {
        if (!Directory.Exists(apiDirectory))
        {
            Console.Error.WriteLine($"API directory not found: {apiDirectory}");
            return 2;
        }

        var packages = ApiIndexWriter.Write(apiDirectory, assemblyPaths, Console.Error);
        Console.Error.WriteLine($"Wrote index pages for {packages.Count} assemblies under {apiDirectory}.");
        return 0;
    }

    private static void WriteWarnings(IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }
}
