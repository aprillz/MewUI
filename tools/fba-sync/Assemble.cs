using System.Text;

namespace Aprillz.MewUI.FbaSync;

/// <summary>
/// Joins the host template and the gallery types in the one order a file-based app allows: directives
/// and usings, then the top-level statements, then the type declarations.
/// </summary>
internal static class Assemble
{
    public static string File(string templateDir, GalleryReader gallery)
    {
        string host = ReadTemplate(templateDir, "host.txt");

        var builder = new StringBuilder();
        builder.Append(MergeUsings(host, gallery.Usings).TrimEnd()).Append('\n');

        builder.Append('\n');
        builder.Append("// ").Append(new string('=', 69)).Append('\n');
        builder.Append("// Gallery, generated from samples/MewUI.Gallery by tools/fba-sync\n");
        builder.Append("// ").Append(new string('=', 69)).Append('\n');

        foreach (var type in gallery.Types)
        {
            builder.Append('\n').Append(type).Append('\n');
        }

        return builder.ToString();
    }

    private static string ReadTemplate(string templateDir, string name)
    {
        string path = Path.Combine(templateDir, name);
        if (!System.IO.File.Exists(path))
        {
            throw new FbaSyncException($"template not found: {path}");
        }

        return System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
    }

    /// <summary>
    /// Inserts the gallery's usings into the host template at its {USINGS} marker, dropping the ones
    /// the template already has and the gallery's own namespace, whose types are now global.
    /// </summary>
    private static string MergeUsings(string host, IReadOnlyList<string> galleryUsings)
    {
        const string marker = "{USINGS}";
        if (!host.Contains(marker, StringComparison.Ordinal))
        {
            throw new FbaSyncException($"host template has no {marker} marker for the gallery's usings.");
        }

        var existing = host
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("using ", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var extra = galleryUsings
            .Where(u => !existing.Contains(u))
            .Where(u => !u.Contains("Aprillz.MewUI.Gallery", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        return host.Replace(marker, string.Join('\n', extra));
    }
}
