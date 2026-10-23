using Aprillz.MewUI.MarkupAudit;
using Aprillz.MewUI.MarkupAudit.Cli;

const string REPOSITORY_MARKER = "build/MewUI.Common.props";
const string DEFAULT_ASSEMBLY = "src/MewUI/bin/Debug/net10.0/Aprillz.MewUI.dll";
const string DEFAULT_EXCLUDED_TYPE = "Aprillz.MewUI.Controls.ControlTemplate";

var assemblyPaths = new List<string>();
var excludedTypes = new List<string>();
string? jsonPath = null;
string? sourceDirectory = null;
string? docsDirectory = null;
string? snippetsPath = null;
var guidePaths = new List<string>();
var showAll = false;
var failOnMissing = false;
var propertiesOnly = false;
var eventsOnly = false;
var docCommand = new DocCommandOptions();

for (var index = 0; index < args.Length; index++)
{
    var argument = args[index];
    switch (argument.ToLowerInvariant())
    {
        case "--help":
        case "-h":
            PrintHelp();
            return;
        case "--all":
            showAll = true;
            break;
        case "--properties":
            propertiesOnly = true;
            break;
        case "--events":
            eventsOnly = true;
            break;
        case "--fail-on-missing":
            failOnMissing = true;
            break;
        case "--json":
            jsonPath = TakeValue(args, ref index, argument);
            break;
        case "--exclude-type":
            excludedTypes.Add(TakeValue(args, ref index, argument));
            break;
        case "--source":
            sourceDirectory = TakeValue(args, ref index, argument);
            break;
        case "--docs":
            docsDirectory = TakeValue(args, ref index, argument);
            break;
        case "--snippets":
            snippetsPath = TakeValue(args, ref index, argument);
            break;
        case "--guide":
            guidePaths.Add(TakeValue(args, ref index, argument));
            break;
        case "--doc-coverage":
        case "--doc-coverage-scope":
        case "--api-crosslink":
        case "--api-index":
        case "--api-starlight":
        case "--site-base":
        case "--guide-links":
        case "--doc-review":
        case "--changed-since":
            if (!docCommand.TryParse(argument, args, ref index, out var docError))
            {
                Console.Error.WriteLine(docError);
                Environment.ExitCode = 2;
                return;
            }

            break;
        default:
            if (argument.StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option: {argument}");
                Environment.ExitCode = 2;
                return;
            }

            assemblyPaths.Add(argument);
            break;
    }
}

if (Environment.ExitCode != 0)
{
    return;
}

if (propertiesOnly && eventsOnly)
{
    Console.Error.WriteLine("--properties and --events cannot be used together.");
    Environment.ExitCode = 2;
    return;
}

// A page-only command (--api-starlight) has nothing to load, so the default assembly is not required.
if (assemblyPaths.Count == 0 && (!docCommand.IsSet || docCommand.NeedsAssemblies))
{
    var repositoryRoot = FindRepositoryRoot();
    if (repositoryRoot == null)
    {
        Console.Error.WriteLine($"No assembly given and {REPOSITORY_MARKER} was not found above the current directory.");
        Environment.ExitCode = 2;
        return;
    }

    assemblyPaths.Add(Path.Combine(repositoryRoot, DEFAULT_ASSEMBLY));
}

foreach (var path in assemblyPaths)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Assembly not found: {path}");
        Environment.ExitCode = 2;
        return;
    }
}

if (sourceDirectory != null)
{
    Console.Error.WriteLine($"--source {sourceDirectory}: body verification is not implemented; the option is accepted and ignored.");
}

if (excludedTypes.Count == 0)
{
    excludedTypes.Add(DEFAULT_EXCLUDED_TYPE);
}

if (docCommand.IsSet)
{
    Environment.ExitCode = DocCommands.Run(docCommand, assemblyPaths, failOnMissing);
    return;
}

var result = MarkupAuditor.Run(new AuditOptions
{
    AssemblyPaths = assemblyPaths,
    ExcludedTypes = excludedTypes,
});

if (docsDirectory != null || snippetsPath != null)
{
    Environment.ExitCode = Aprillz.MewUI.MarkupAudit.Cli.MarkupReferenceCommand.Run(result, docsDirectory, snippetsPath, guidePaths);
    return;
}

MarkdownAuditWriter.Write(result, Console.Out, new MarkdownWriteOptions
{
    ShowAll = showAll,
    IncludeProperties = !eventsOnly,
    IncludeEvents = !propertiesOnly,
});

if (jsonPath != null)
{
    JsonAuditWriter.Write(result, jsonPath);
}

foreach (var warning in result.Warnings)
{
    Console.Error.WriteLine($"warning: {warning}");
}

var counts = result.Counts;
var missingPropertyCount = eventsOnly ? 0 : counts.MissingProperties;
var missingEventCount = propertiesOnly ? 0 : counts.MissingEvents;
var ambiguousEventExtensionCount = propertiesOnly ? 0 : counts.AmbiguousEventExtensions;

Console.Error.WriteLine(
    $"Audited {counts.Properties} properties and {counts.Events} events on {counts.Receivers} public UI types; " +
    $"{missingPropertyCount} missing property extensions, {missingEventCount} missing event extensions, " +
    $"{ambiguousEventExtensionCount} ambiguous event extension overloads.");

var findingSummary = string.Join(", ", counts.FindingsByCode.Select(static pair => $"{pair.Key} {pair.Value}"));
Console.Error.WriteLine(
    $"Inventoried {counts.Extensions} extension methods ({counts.ExtensionsWithoutSummary} without XML summary); " +
    $"{result.Findings.Count} findings{(findingSummary.Length > 0 ? $": {findingSummary}" : string.Empty)}.");

if (failOnMissing && missingPropertyCount + missingEventCount + ambiguousEventExtensionCount > 0)
{
    Environment.ExitCode = 1;
}

static string TakeValue(string[] arguments, ref int index, string option)
{
    if (index + 1 >= arguments.Length)
    {
        Console.Error.WriteLine($"{option} requires a value.");
        Environment.ExitCode = 2;
        return string.Empty;
    }

    index++;
    return arguments[index];
}

static string? FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, REPOSITORY_MARKER)))
            {
                return directory.FullName;
            }
        }
    }

    return null;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        MewUI.MarkupAudit [assembly.dll ...] [options]

        Inventories public fluent extension methods and audits every public settable property and event
        of each concrete UI receiver against them. By default, only missing members are listed.
        Without assembly paths, src/MewUI/bin/Debug/net10.0/Aprillz.MewUI.dll under the repository root is audited.

        Options:
          --all                     Show covered and missing members.
          --properties              Audit properties only.
          --events                  Audit events only.
          --fail-on-missing         Exit with code 1 when coverage is missing or an event extension is ambiguous.
          --json <path>             Write the full inventory, ledger, findings, and counts as JSON.
          --exclude-type <FullName> Skip a receiver type (repeatable; default: Aprillz.MewUI.Controls.ControlTemplate).
          --source <dir>            Reserved for body verification; accepted and ignored.
          --docs <dir>              Write the C# Markup reference (one Markdown file per receiver group) instead of the audit report.
          --snippets <file.cs>      Copy the #region bodies of the file into the snippet blocks of each --guide file.
          --guide <file.md>         Guide to update with --snippets (repeatable).
          --help, -h                Show this help.
        """);
    Console.WriteLine(DocCommandOptions.HELP);
}
