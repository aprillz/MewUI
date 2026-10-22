using Aprillz.MewUI.MarkupAudit;

namespace Aprillz.MewUI.MarkupAudit.Cli;

/// <summary>Runs the <c>--docs</c> and <c>--snippets</c>/<c>--guide</c> modes; returns the process exit code.</summary>
internal static class MarkupReferenceCommand
{
    public static int Run(AuditResult result, string? docsDirectory, string? snippetsPath, IReadOnlyList<string> guidePaths)
    {
        foreach (var warning in result.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        try
        {
            if (docsDirectory != null)
            {
                var output = MarkupReferenceWriter.Write(result, docsDirectory);
                var lineCount = output.Files.Values.Sum(static content => content.Count(static character => character == '\n'));
                Console.Error.WriteLine(
                    $"Wrote {output.Files.Count} reference files ({output.MethodCount} extension methods, {lineCount} lines) to {docsDirectory}; " +
                    $"{output.Skipped.Count} non-fluent extension methods skipped.");
            }

            if (snippetsPath != null)
            {
                if (guidePaths.Count == 0)
                {
                    Console.Error.WriteLine("--snippets requires at least one --guide <file>.");
                    return 2;
                }

                var regions = SnippetUpdater.ReadRegions(snippetsPath);
                foreach (var guidePath in guidePaths)
                {
                    var changed = SnippetUpdater.UpdateFile(guidePath, regions);
                    Console.Error.WriteLine($"{guidePath}: snippets {(changed ? "updated" : "unchanged")}.");
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        return 0;
    }
}
