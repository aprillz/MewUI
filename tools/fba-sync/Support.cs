namespace Aprillz.MewUI.FbaSync;

/// <summary>A generation step that cannot produce a correct file; the run stops without writing.</summary>
internal sealed class FbaSyncException(string message) : Exception(message);

internal static class Locate
{
    // The shared props rather than a solution file: the development and release branches carry
    // different solutions, and a release is generated from the branch that has neither of the other's.
    private static readonly string _rootMarker = Path.Combine("build", "MewUI.Common.props");

    public static string RepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, _rootMarker)))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new FbaSyncException($"{_rootMarker} not found above {start}");
    }

    /// <summary>Prefers the copy beside the built tool, then the one in the repo.</summary>
    public static string TemplateDir(string baseDirectory, string repoRoot)
    {
        string local = Path.Combine(baseDirectory, "template");
        return Directory.Exists(local) ? local : Path.Combine(repoRoot, "tools", "fba-sync", "template");
    }
}
