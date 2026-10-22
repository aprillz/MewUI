using System.Reflection;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Counts of one cross-link pass; "missing" pages were named by the inventory but not generated, dropped targets had no page to link to.</summary>
public sealed record CrosslinkSummary(
    int MemberPagesLinked,
    int MemberPagesMissing,
    int ExtensionPagesLinked,
    int ExtensionPagesMissing,
    int DroppedTargets);

/// <summary>Appends "Fluent:" lines to property and event pages and "Sets:" lines to extension method pages of xmldocmd output; re-running replaces the lines.</summary>
public static class ApiCrosslinker
{
    private const string FLUENT_PREFIX = "Fluent: ";
    private const string SETS_PREFIX = "Sets: ";
    private const string SEE_ALSO_HEADING = "## See Also";
    private const string GENERATED_MARKER = "<!-- DO NOT EDIT";

    /// <summary>Runs the audit over the assemblies and rewrites the pages under the api directory in place.</summary>
    public static CrosslinkSummary Run(string apiDirectory, IReadOnlyList<string> assemblyPaths, IList<string> warnings)
    {
        var audit = MarkupAuditor.Run(new AuditOptions { AssemblyPaths = assemblyPaths });
        foreach (var warning in audit.Warnings)
        {
            warnings.Add(warning);
        }

        using var assemblies = AuditAssemblySet.Load(assemblyPaths);
        var typesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var assembly in assemblies.Assemblies)
        {
            foreach (var type in PublicSurface.EnumerateTypes(assembly, null, warnings))
            {
                typesByName.TryAdd(TypeFormatter.FormatFullType(type), type);
            }
        }

        var recordsByDisplayName = audit.Inventory.ToLookup(static record => $"{ShortTypeName(record.DeclaringType)}.{record.Name}", StringComparer.Ordinal);
        var memberLinks = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var extensionLinks = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var entry in audit.Properties.Concat(audit.Events))
        {
            var names = entry.Extensions.Concat(entry.MultiArgumentExtensions).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length == 0 || !typesByName.TryGetValue(entry.DeclaringType, out var declaringType))
            {
                continue;
            }

            var memberPage = XmlDocMarkdownLayout.MemberPagePath(apiDirectory, declaringType, entry.Member);
            foreach (var name in names)
            {
                foreach (var record in recordsByDisplayName[name])
                {
                    if (!typesByName.TryGetValue(record.DeclaringType, out var extensionType))
                    {
                        continue;
                    }

                    var label = $".{record.Name}({string.Join(", ", record.Parameters.Select(static parameter => parameter.Type))})";
                    Add(memberLinks, memberPage, label, XmlDocMarkdownLayout.MemberPagePath(apiDirectory, extensionType, record.Name));
                }
            }
        }

        foreach (var record in audit.Inventory)
        {
            if (record.TargetMembers.Count == 0 || !typesByName.TryGetValue(record.DeclaringType, out var extensionType))
            {
                continue;
            }

            var extensionPage = XmlDocMarkdownLayout.MemberPagePath(apiDirectory, extensionType, record.Name);
            var lookupTypes = ResolveLookupTypes(record, typesByName);
            foreach (var memberName in record.TargetMembers)
            {
                var declaringType = FindDeclaringType(lookupTypes, memberName);
                if (declaringType == null)
                {
                    continue;
                }

                var label = $"{TypeFormatter.FormatType(declaringType)}.{memberName}";
                Add(extensionLinks, extensionPage, label, XmlDocMarkdownLayout.MemberPagePath(apiDirectory, declaringType, memberName));
            }
        }

        var dropped = 0;
        var (membersLinked, membersMissing) = Apply(memberLinks, FLUENT_PREFIX, ref dropped);
        var (extensionsLinked, extensionsMissing) = Apply(extensionLinks, SETS_PREFIX, ref dropped);
        return new CrosslinkSummary(membersLinked, membersMissing, extensionsLinked, extensionsMissing, dropped);
    }

    private static (int Linked, int Missing) Apply(SortedDictionary<string, SortedDictionary<string, string>> links, string prefix, ref int dropped)
    {
        var linked = 0;
        var missing = 0;
        foreach (var (pagePath, targets) in links)
        {
            if (!File.Exists(pagePath))
            {
                missing++;
                continue;
            }

            var items = new List<string>();
            foreach (var (label, targetPath) in targets)
            {
                if (File.Exists(targetPath))
                {
                    items.Add($"[`{label}`]({XmlDocMarkdownLayout.RelativeLink(pagePath, targetPath)})");
                }
                else
                {
                    dropped++;
                }
            }

            if (items.Count == 0)
            {
                continue;
            }

            WriteLine(pagePath, prefix + string.Join(", ", items));
            linked++;
        }

        return (linked, missing);
    }

    private static void WriteLine(string pagePath, string line)
    {
        var original = File.ReadAllText(pagePath);
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Split('\n').Select(static text => text.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        // A previous run inserted the line plus one blank separator; both go so re-runs leave the file unchanged.
        var prefix = line[..line.IndexOf(' ')];
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith(prefix + " ", StringComparison.Ordinal))
            {
                continue;
            }

            lines.RemoveAt(index);
            if (index < lines.Count && lines[index].Length == 0)
            {
                lines.RemoveAt(index);
            }

            break;
        }

        var insertAt = lines.FindIndex(static text => text == SEE_ALSO_HEADING);
        if (insertAt < 0)
        {
            insertAt = lines.FindIndex(static text => text.StartsWith(GENERATED_MARKER, StringComparison.Ordinal));
        }

        if (insertAt < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(line);
        }
        else
        {
            lines.Insert(insertAt, line);
            lines.Insert(insertAt + 1, string.Empty);
        }

        File.WriteAllText(pagePath, string.Join(newline, lines) + newline);
    }

    private static void Add(SortedDictionary<string, SortedDictionary<string, string>> links, string pagePath, string label, string targetPath)
    {
        if (!links.TryGetValue(pagePath, out var targets))
        {
            targets = new SortedDictionary<string, string>(StringComparer.Ordinal);
            links[pagePath] = targets;
        }

        targets.TryAdd(label, targetPath);
    }

    private static IReadOnlyList<Type> ResolveLookupTypes(ExtensionMethodRecord record, Dictionary<string, Type> typesByName)
    {
        var names = record.Receiver.IsGenericParameter ? record.Receiver.Constraints : new[] { record.Receiver.Type };
        var types = names.Select(name => typesByName.GetValueOrDefault(name)).OfType<Type>().ToList();
        if (types.Count == 0)
        {
            types.AddRange(record.ApplicableReceivers.Select(name => typesByName.GetValueOrDefault(name)).OfType<Type>());
        }

        return types;
    }

    private static Type? FindDeclaringType(IReadOnlyList<Type> lookupTypes, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var type in lookupTypes)
        {
            MemberInfo? member;
            try
            {
                member = type.GetProperties(flags).FirstOrDefault(property => property.Name == memberName)
                         ?? (MemberInfo?)type.GetEvents(flags).FirstOrDefault(eventInfo => eventInfo.Name == memberName);
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or TypeLoadException)
            {
                continue;
            }

            if (member?.DeclaringType != null)
            {
                return member.DeclaringType;
            }
        }

        return null;
    }

    private static string ShortTypeName(string fullName)
    {
        var generic = fullName.IndexOf('<');
        var withoutArguments = generic >= 0 ? fullName[..generic] : fullName;
        var dot = withoutArguments.LastIndexOf('.');
        return dot >= 0 ? withoutArguments[(dot + 1)..] : withoutArguments;
    }
}
