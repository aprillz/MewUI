using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Package facts shown in the API reference home, read from the csproj next to the built assembly.</summary>
public sealed record ApiPackageInfo(string Assembly, string PackageId, string Description);

/// <summary>Writes the index pages above xmldocmd output: the package table and one namespace listing per assembly, with Starlight frontmatter.</summary>
public static class ApiIndexWriter
{
    private const string INDEX_FILE = "index.md";
    private const string VERSION_FILE = "version.txt";
    private const string SOLUTION_FILE = "MewUI.Dev.slnx";
    private const string COMMON_PROPS = "build/MewUI.Common.props";
    private const string PROJECT_PATTERN = "*.csproj";
    private const string EXTENSIONS_SUFFIX = "Extensions";
    private const int MAX_PROJECT_SEARCH_DEPTH = 6;
    private const int MAX_SUBSTITUTION_DEPTH = 5;

    private static readonly Regex _property = new(@"\$\(([A-Za-z0-9_]+)\)", RegexOptions.Compiled);
    private static readonly Regex _whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex _heading = new(@"^# (?<name>.+?) (?<kind>[a-z ]+)$", RegexOptions.Compiled);

    /// <summary>Writes the root and per-assembly index pages for the assemblies whose xmldocmd folders exist; absent folders are logged and skipped.</summary>
    public static IReadOnlyList<ApiPackageInfo> Write(string apiDirectory, IReadOnlyList<string> assemblyPaths, TextWriter log)
    {
        var version = ReadVersionStamp(apiDirectory);
        var packages = new List<ApiPackageInfo>();
        foreach (var assemblyPath in assemblyPaths)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (!Directory.Exists(XmlDocMarkdownLayout.AssemblyDirectory(apiDirectory, assemblyName)))
            {
                log.WriteLine($"No generated pages for {assemblyName} under {apiDirectory}; skipped in the index.");
                continue;
            }

            packages.Add(ReadPackageInfo(assemblyPath, assemblyName));
        }

        WriteRootIndex(apiDirectory, packages, version);
        for (var index = 0; index < packages.Count; index++)
        {
            WriteAssemblyIndex(apiDirectory, packages[index], index + 1, version);
        }

        return packages;
    }

    private static void WriteRootIndex(string apiDirectory, IReadOnlyList<ApiPackageInfo> packages, string? version)
    {
        using var writer = CreateWriter(Path.Combine(apiDirectory, INDEX_FILE));
        WriteFrontMatter(writer, "API Reference", 0);
        writer.WriteLine("# MewUI API Reference");
        writer.WriteLine();
        if (version != null)
        {
            writer.WriteLine(version);
            writer.WriteLine();
        }

        writer.WriteLine("| Package | Assembly | Description |");
        writer.WriteLine("| --- | --- | --- |");
        foreach (var package in packages)
        {
            writer.WriteLine($"| `{package.PackageId}` | [{package.Assembly}]({package.Assembly}/{INDEX_FILE}) | {Escape(package.Description)} |");
        }
    }

    private static void WriteAssemblyIndex(string apiDirectory, ApiPackageInfo package, int order, string? version)
    {
        var assemblyDirectory = XmlDocMarkdownLayout.AssemblyDirectory(apiDirectory, package.Assembly);
        using var writer = CreateWriter(Path.Combine(assemblyDirectory, INDEX_FILE));
        WriteFrontMatter(writer, package.Assembly, order);
        writer.WriteLine($"# {package.Assembly}");
        writer.WriteLine();
        writer.WriteLine($"Package `{package.PackageId}`. {package.Description}");
        writer.WriteLine();
        if (version != null)
        {
            writer.WriteLine(version);
            writer.WriteLine();
        }

        if (File.Exists(Path.Combine(assemblyDirectory, package.Assembly + ".md")))
        {
            writer.WriteLine($"Full member listing: [{package.Assembly} assembly]({package.Assembly}.md)");
            writer.WriteLine();
        }

        writer.WriteLine("## Namespaces");

        foreach (var namespaceDirectory in Directory.GetDirectories(assemblyDirectory).OrderBy(static path => path, StringComparer.Ordinal))
        {
            var namespaceName = Path.GetFileName(namespaceDirectory);
            var pages = Directory.GetFiles(namespaceDirectory, "*.md")
                .Select(path => ReadTypeHeading(namespaceName, path))
                .OfType<TypeHeading>()
                .OrderBy(static heading => heading.Name, StringComparer.Ordinal)
                .ToArray();

            writer.WriteLine();
            writer.WriteLine($"### {namespaceName}");
            WriteTypeTable(writer, pages.Where(static heading => !heading.IsFluentExtensions), "Type");
            var fluent = pages.Where(static heading => heading.IsFluentExtensions).ToArray();
            if (fluent.Length > 0)
            {
                writer.WriteLine();
                writer.WriteLine("Fluent extensions:");
                WriteTypeTable(writer, fluent, "Extension class");
            }
        }
    }

    private static void WriteTypeTable(TextWriter writer, IEnumerable<TypeHeading> headings, string firstColumn)
    {
        writer.WriteLine();
        writer.WriteLine($"| {firstColumn} | Kind | Description |");
        writer.WriteLine("| --- | --- | --- |");
        foreach (var heading in headings)
        {
            writer.WriteLine($"| [{Escape(heading.Name)}]({heading.Link}) | {heading.Kind} | {Escape(heading.Description)} |");
        }
    }

    private static void WriteFrontMatter(TextWriter writer, string title, int order)
    {
        writer.WriteLine("---");
        writer.WriteLine($"title: {title}");
        writer.WriteLine("sidebar:");
        writer.WriteLine($"  order: {order}");
        writer.WriteLine("---");
        writer.WriteLine();
    }

    private static StreamWriter CreateWriter(string path)
    {
        return new StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
    }

    private static TypeHeading? ReadTypeHeading(string namespaceName, string pagePath)
    {
        string[] lines;
        try
        {
            lines = File.ReadLines(pagePath).Take(3).ToArray();
        }
        catch (IOException)
        {
            return null;
        }

        var match = lines.Length > 0 ? _heading.Match(lines[0]) : Match.Empty;
        if (!match.Success)
        {
            return null;
        }

        var name = System.Net.WebUtility.HtmlDecode(match.Groups["name"].Value);
        var kind = match.Groups["kind"].Value;
        var description = lines.Length > 2 && !lines[2].StartsWith("```", StringComparison.Ordinal) ? lines[2] : string.Empty;
        var link = $"{namespaceName}/{Path.GetFileName(pagePath)}";
        var isFluent = kind.EndsWith("class", StringComparison.Ordinal) && name.EndsWith(EXTENSIONS_SUFFIX, StringComparison.Ordinal);
        return new TypeHeading(name, kind, description, link, isFluent);
    }

    private static string? ReadVersionStamp(string apiDirectory)
    {
        var path = Path.Combine(apiDirectory, VERSION_FILE);
        if (!File.Exists(path))
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        var parts = new List<string>();
        if (values.TryGetValue("MewUIVersion", out var version) && version.Length > 0)
        {
            parts.Add($"MewUI {version}");
        }

        if (values.TryGetValue("Commit", out var commit) && commit.Length > 0)
        {
            parts.Add($"commit `{(commit.Length > 8 ? commit[..8] : commit)}`");
        }

        return parts.Count == 0 ? null : $"Generated from {string.Join(", ", parts)}.";
    }

    private static ApiPackageInfo ReadPackageInfo(string assemblyPath, string assemblyName)
    {
        var projectPath = FindProjectFile(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!);
        if (projectPath == null)
        {
            return new ApiPackageInfo(assemblyName, assemblyName, string.Empty);
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repositoryRoot = FindRepositoryRoot(Path.GetDirectoryName(projectPath)!);
        if (repositoryRoot != null)
        {
            ReadProperties(Path.Combine(repositoryRoot, COMMON_PROPS), properties);
        }

        ReadProperties(projectPath, properties);

        var packageId = Resolve(properties, "PackageId") ?? Resolve(properties, "AssemblyName") ?? assemblyName;
        var description = Resolve(properties, "PackageDescription") ?? Resolve(properties, "Description") ?? string.Empty;
        return new ApiPackageInfo(assemblyName, packageId, _whitespace.Replace(description, " ").Trim());
    }

    private static void ReadProperties(string path, Dictionary<string, string> properties)
    {
        if (!File.Exists(path))
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        // Conditions are not evaluated: the last unconditional-looking value wins, which is what the package metadata uses.
        foreach (var element in document.Descendants().Where(static element => element.Parent?.Name.LocalName == "PropertyGroup"))
        {
            properties[element.Name.LocalName] = element.Value;
        }
    }

    private static string? Resolve(Dictionary<string, string> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            return null;
        }

        for (var depth = 0; depth < MAX_SUBSTITUTION_DEPTH && value.Contains("$(", StringComparison.Ordinal); depth++)
        {
            value = _property.Replace(value, match => properties.GetValueOrDefault(match.Groups[1].Value) ?? string.Empty);
        }

        return value.Length > 0 ? value : null;
    }

    private static string? FindProjectFile(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory != null && depth < MAX_PROJECT_SEARCH_DEPTH; depth++, directory = directory.Parent)
        {
            var project = directory.GetFiles(PROJECT_PATTERN).FirstOrDefault();
            if (project != null)
            {
                return project.FullName;
            }
        }

        return null;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string Escape(string text) => text.Replace("|", "\\|");

    private sealed record TypeHeading(string Name, string Kind, string Description, string Link, bool IsFluentExtensions);
}
