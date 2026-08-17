namespace Aprillz.MewUI.FbaSync;

/// <summary>A generation step that cannot produce a correct file; the run stops without writing.</summary>
internal sealed class FbaSyncException(string message) : Exception(message);

internal static class Locate
{
    public static string RepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MewUI.Dev.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new FbaSyncException($"MewUI.Dev.slnx not found above {start}");
    }

    /// <summary>Prefers the copy beside the built tool, then the one in the repo.</summary>
    public static string TemplateDir(string baseDirectory, string repoRoot)
    {
        string local = Path.Combine(baseDirectory, "template");
        return Directory.Exists(local) ? local : Path.Combine(repoRoot, "tools", "fba-sync", "template");
    }
}
