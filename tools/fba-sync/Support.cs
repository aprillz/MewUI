namespace Aprillz.MewUI.FbaSync;

/// <summary>A generation step that cannot produce a correct file; the run stops without writing.</summary>
internal sealed class FbaSyncException(string message) : Exception(message);

internal static class Locate
{
    // Nothing tracked marks the root: the development and release branches carry different solutions,
    // and a file that is committed can move. A linked worktree holds .git as a file rather than a
    // directory, so both are accepted.
    private const string ROOT_MARKER = ".git";

    public static string RepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null && !IsRepoRoot(dir))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new FbaSyncException($"No {ROOT_MARKER} found above {start}");
    }

    private static bool IsRepoRoot(DirectoryInfo dir)
    {
        string marker = Path.Combine(dir.FullName, ROOT_MARKER);
        return Directory.Exists(marker) || File.Exists(marker);
    }

    /// <summary>Prefers the copy beside the built tool, then the one in the repo.</summary>
    public static string TemplateDir(string baseDirectory, string repoRoot)
    {
        string local = Path.Combine(baseDirectory, "template");
        return Directory.Exists(local) ? local : Path.Combine(repoRoot, "tools", "fba-sync", "template");
    }
}

internal static class PackageVersion
{
    /// <summary>Reads MewUIVersion, the version the release scripts declare and publish under.</summary>
    public static string Declared(string repoRoot)
    {
        string path = Path.Combine(repoRoot, "build", "MewUI.Common.props");
        if (!File.Exists(path))
        {
            throw new FbaSyncException($"props not found: {path}");
        }

        var document = System.Xml.Linq.XDocument.Load(path);
        var node = document.Descendants("MewUIVersion").FirstOrDefault();
        if (node == null)
        {
            throw new FbaSyncException($"MewUIVersion is missing from {path}.");
        }

        string version = node.Value.Trim();
        if (version.Length == 0)
        {
            throw new FbaSyncException($"MewUIVersion is empty in {path}.");
        }

        return version;
    }
}
