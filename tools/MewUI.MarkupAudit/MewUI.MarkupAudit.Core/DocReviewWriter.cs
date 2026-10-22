namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Input of one documentation review extract.</summary>
public sealed class DocReviewOptions
{
    /// <summary>Paths of the assemblies to read; the fluent inventory is taken from the same set.</summary>
    public required IReadOnlyList<string> AssemblyPaths { get; init; }

    /// <summary>Namespace whose public types are listed (the namespace itself and those below it).</summary>
    public required string NamespacePrefix { get; init; }

    /// <summary>Simple type names (without generic arity) to keep; null keeps every type of the namespace.</summary>
    public IReadOnlySet<string>? TypeNames { get; init; }
}

/// <summary>Counts of one review extract.</summary>
public sealed record DocReviewSummary(int Types, int Members, IReadOnlyList<string> Warnings);

/// <summary>Writes every public member of a namespace with its documentation sections and the summaries of its fluent counterparts, one type per section.</summary>
public static class DocReviewWriter
{
    private const string MISSING_CELL = "**missing**";
    private const string INHERITED_CELL = "(inheritdoc)";
    private const string CELL_SEPARATOR = "<br>";

    /// <summary>Writes the review tables as Markdown and returns the counts; the extract judges nothing.</summary>
    public static DocReviewSummary Write(DocReviewOptions options, TextWriter writer)
    {
        var warnings = new List<string>();
        var audit = MarkupAuditor.Run(new AuditOptions { AssemblyPaths = options.AssemblyPaths });
        warnings.AddRange(audit.Warnings);

        var recordsByDisplayName = audit.Inventory.ToLookup(static record => $"{ShortTypeName(record.DeclaringType)}.{record.Name}", StringComparer.Ordinal);
        var ledger = new Dictionary<string, MemberLedgerEntry>(StringComparer.Ordinal);
        foreach (var entry in audit.Properties.Concat(audit.Events))
        {
            ledger.TryAdd(LedgerKey(entry.Receiver, entry.Member), entry);
        }

        using var assemblies = AuditAssemblySet.Load(options.AssemblyPaths);
        writer.WriteLine($"# Documentation review: {options.NamespacePrefix}");

        var typeCount = 0;
        var memberCount = 0;
        for (var index = 0; index < assemblies.Assemblies.Count; index++)
        {
            var documentation = XmlDocumentationIndex.TryLoad(assemblies.Paths[index]);
            if (documentation == null)
            {
                warnings.Add($"No XML documentation file next to {assemblies.Paths[index]}; its members are listed without text.");
            }

            foreach (var type in PublicSurface.EnumerateTypes(assemblies.Assemblies[index], options.NamespacePrefix, warnings))
            {
                if (options.TypeNames != null && !options.TypeNames.Contains(SimpleName(type)))
                {
                    continue;
                }

                PublicMember[] members;
                try
                {
                    members = PublicSurface.EnumerateMembers(type).ToArray();
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
                {
                    warnings.Add($"Skipped {type.FullName}: {exception.Message}");
                    continue;
                }

                typeCount++;
                var typeDisplay = TypeFormatter.FormatType(type);
                var typeFullName = TypeFormatter.FormatFullType(type);
                writer.WriteLine();
                writer.WriteLine($"### {typeFullName}");
                writer.WriteLine();
                writer.WriteLine("|Type|Member|Kind|Summary|Params/Returns|Fluent counterpart summary|");
                writer.WriteLine("|-|-|-|-|-|-|");

                foreach (var member in members)
                {
                    memberCount++;
                    var text = documentation?.FindDocumentation(member.DocumentationId);
                    var fluent = member.Kind is SurfaceKinds.PROPERTY or SurfaceKinds.EVENT
                        ? FluentCounterpart(typeFullName, member.Member.Name, ledger, recordsByDisplayName, audit.Inventory)
                        : string.Empty;

                    writer.WriteLine($"|`{Escape(typeDisplay)}`|`{Escape(member.DisplayName)}`|{member.Kind}|{SummaryCell(text)}|{DetailsCell(text)}|{fluent}|");
                }
            }
        }

        return new DocReviewSummary(typeCount, memberCount, warnings);
    }

    private static string SummaryCell(MemberDocumentation? text)
    {
        if (text == null)
        {
            return MISSING_CELL;
        }

        if (!string.IsNullOrEmpty(text.Summary))
        {
            return Escape(text.Summary);
        }

        return text.HasInheritDoc ? INHERITED_CELL : MISSING_CELL;
    }

    private static string DetailsCell(MemberDocumentation? text)
    {
        if (text == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var (name, description) in text.TypeParameters)
        {
            parts.Add($"`{name}`: {Escape(description)}");
        }

        foreach (var (name, description) in text.Parameters)
        {
            parts.Add($"`{name}`: {Escape(description)}");
        }

        if (text.Returns != null)
        {
            parts.Add($"returns: {Escape(text.Returns)}");
        }

        if (text.Value != null)
        {
            parts.Add($"value: {Escape(text.Value)}");
        }

        foreach (var (name, description) in text.Exceptions)
        {
            parts.Add($"throws `{name}`: {Escape(description)}");
        }

        if (text.Remarks != null)
        {
            parts.Add($"remarks: {Escape(text.Remarks)}");
        }

        return string.Join(CELL_SEPARATOR, parts);
    }

    private static string FluentCounterpart(
        string typeFullName,
        string memberName,
        Dictionary<string, MemberLedgerEntry> ledger,
        ILookup<string, ExtensionMethodRecord> recordsByDisplayName,
        IReadOnlyList<ExtensionMethodRecord> inventory)
    {
        IEnumerable<ExtensionMethodRecord> records;
        if (ledger.TryGetValue(LedgerKey(typeFullName, memberName), out var entry))
        {
            records = entry.Extensions.Concat(entry.MultiArgumentExtensions).SelectMany(name => recordsByDisplayName[name]);
        }
        else
        {
            // Abstract declaring types have no ledger row; the inventory names them as receiver or constraint.
            records = inventory.Where(record =>
                record.TargetMembers.Contains(memberName) &&
                (record.Receiver.Type == typeFullName || record.Receiver.Constraints.Contains(typeFullName)));
        }

        var cells = records
            .Distinct()
            .OrderBy(static record => record.DeclaringType, StringComparer.Ordinal)
            .ThenBy(static record => record.Signature, StringComparer.Ordinal)
            .Select(record =>
            {
                var parameters = string.Join(", ", record.Parameters.Select(static parameter => parameter.Type));
                var summary = record.Summary == null ? MISSING_CELL : Escape(record.Summary);
                return $"`{Escape(record.Name)}({Escape(parameters)})`: {summary}";
            });

        return string.Join(CELL_SEPARATOR, cells);
    }

    private static string LedgerKey(string receiver, string member) => $"{receiver}|{member}";

    private static string SimpleName(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return tick >= 0 ? type.Name[..tick] : type.Name;
    }

    private static string ShortTypeName(string fullName)
    {
        var generic = fullName.IndexOf('<');
        var withoutArguments = generic >= 0 ? fullName[..generic] : fullName;
        var dot = withoutArguments.LastIndexOf('.');
        return dot >= 0 ? withoutArguments[(dot + 1)..] : withoutArguments;
    }

    private static string Escape(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
