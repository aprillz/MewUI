namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Input of one XML documentation coverage run.</summary>
public sealed class DocCoverageOptions
{
    /// <summary>Paths of the assemblies to scan; each is read with the .xml file next to it.</summary>
    public required IReadOnlyList<string> AssemblyPaths { get; init; }

    /// <summary>Namespace that limits the scan (the namespace itself and those below it); null scans every namespace.</summary>
    public string? NamespacePrefix { get; init; }
}

/// <summary>One public type or member whose XML documentation has neither a summary nor an inheritdoc.</summary>
public sealed record UndocumentedMember(
    string Assembly,
    string Namespace,
    string Type,
    string Member,
    string Kind,
    string DocumentationId);

/// <summary>Per-assembly totals of one coverage run.</summary>
public sealed record DocCoverageAssemblyCounts(string Assembly, int Scanned, int Missing);

/// <summary>Output of one coverage run; rows are ordered by assembly, namespace, type, and member.</summary>
public sealed record DocCoverageResult(
    IReadOnlyList<UndocumentedMember> Missing,
    IReadOnlyList<DocCoverageAssemblyCounts> Assemblies,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Number of public types and members scanned over all assemblies.</summary>
    public int Scanned => Assemblies.Sum(static counts => counts.Scanned);
}

/// <summary>Lists the public surface members that the compiler-generated XML documentation leaves without a summary.</summary>
public static class DocCoverageAnalyzer
{
    /// <summary>Runs one coverage scan over the assemblies named in the options.</summary>
    public static DocCoverageResult Run(DocCoverageOptions options)
    {
        var warnings = new List<string>();
        var missing = new List<UndocumentedMember>();
        var assemblyCounts = new List<DocCoverageAssemblyCounts>();

        using var assemblies = AuditAssemblySet.Load(options.AssemblyPaths);
        for (var index = 0; index < assemblies.Assemblies.Count; index++)
        {
            var assembly = assemblies.Assemblies[index];
            var assemblyName = assembly.GetName().Name ?? string.Empty;
            var documentation = XmlDocumentationIndex.TryLoad(assemblies.Paths[index]);
            if (documentation == null)
            {
                warnings.Add($"No XML documentation file next to {assemblies.Paths[index]}; every member of {assemblyName} is reported as missing.");
            }

            var scanned = 0;
            var missingBefore = missing.Count;
            foreach (var type in PublicSurface.EnumerateTypes(assembly, options.NamespacePrefix, warnings))
            {
                IEnumerable<PublicMember> members;
                try
                {
                    members = PublicSurface.EnumerateMembers(type).ToArray();
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
                {
                    warnings.Add($"Skipped {type.FullName}: {exception.Message}");
                    continue;
                }

                foreach (var member in members)
                {
                    scanned++;
                    if (documentation?.IsDocumented(member.DocumentationId) == true)
                    {
                        continue;
                    }

                    missing.Add(new UndocumentedMember(
                        assemblyName,
                        type.Namespace ?? string.Empty,
                        TypeFormatter.FormatType(type),
                        member.DisplayName,
                        member.Kind,
                        member.DocumentationId));
                }
            }

            assemblyCounts.Add(new DocCoverageAssemblyCounts(assemblyName, scanned, missing.Count - missingBefore));
        }

        return new DocCoverageResult(missing, assemblyCounts, warnings);
    }

    /// <summary>Writes the missing members as Markdown grouped by assembly and namespace, with counts in the headings.</summary>
    public static void Write(DocCoverageResult result, string? namespacePrefix, TextWriter writer)
    {
        writer.WriteLine("# XML documentation coverage");
        writer.WriteLine();
        writer.WriteLine($"Scope: {(string.IsNullOrEmpty(namespacePrefix) ? "all namespaces" : $"`{namespacePrefix}`")}. " +
                         $"Scanned {result.Scanned} public types and members; {result.Missing.Count} without `<summary>`.");

        foreach (var assembly in result.Assemblies)
        {
            writer.WriteLine();
            writer.WriteLine($"## {assembly.Assembly} ({assembly.Missing} missing of {assembly.Scanned})");

            var byNamespace = result.Missing
                .Where(member => member.Assembly == assembly.Assembly)
                .GroupBy(static member => member.Namespace, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal);

            foreach (var group in byNamespace)
            {
                writer.WriteLine();
                writer.WriteLine($"### {group.Key} ({group.Count()} missing)");
                writer.WriteLine();
                writer.WriteLine("|Type|Member|Kind|");
                writer.WriteLine("|-|-|-|");
                foreach (var member in group)
                {
                    writer.WriteLine($"|`{member.Type}`|`{member.Member}`|{member.Kind}|");
                }
            }
        }
    }
}
