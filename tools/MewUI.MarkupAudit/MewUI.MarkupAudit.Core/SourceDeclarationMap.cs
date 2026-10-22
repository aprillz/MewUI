using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Best-effort map from changed source files to the type names they declare, for limiting a review to recent edits.</summary>
public static class SourceDeclarationMap
{
    private const string SOURCE_EXTENSION = ".cs";

    private static readonly Regex _declaration = new(@"\b(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>Simple names of the types declared in .cs files that differ from the git reference, plus untracked .cs files; deleted files contribute nothing.</summary>
    public static IReadOnlySet<string> ChangedTypeNames(string repositoryRoot, string gitReference, IList<string> warnings)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        files.UnionWith(RunGit(repositoryRoot, $"diff --name-only {gitReference}", warnings));
        files.UnionWith(RunGit(repositoryRoot, "ls-files --others --exclude-standard", warnings));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in files)
        {
            if (!relative.EndsWith(SOURCE_EXTENSION, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (Match match in _declaration.Matches(File.ReadAllText(path)))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> RunGit(string repositoryRoot, string arguments, IList<string> warnings)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                warnings.Add("git could not be started.");
                return Array.Empty<string>();
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                warnings.Add($"git {arguments} failed: {error.Trim()}");
                return Array.Empty<string>();
            }

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            warnings.Add($"git could not be started: {exception.Message}");
            return Array.Empty<string>();
        }
    }
}
