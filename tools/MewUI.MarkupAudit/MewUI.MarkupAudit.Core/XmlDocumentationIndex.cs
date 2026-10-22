using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Documentation of one member as flattened one-line texts; absent sections are null or empty.</summary>
public sealed record MemberDocumentation(
    string? Summary,
    string? Remarks,
    string? Returns,
    string? Value,
    IReadOnlyList<KeyValuePair<string, string>> Parameters,
    IReadOnlyList<KeyValuePair<string, string>> TypeParameters,
    IReadOnlyList<KeyValuePair<string, string>> Exceptions,
    bool HasInheritDoc)
{
    /// <summary>True when the member carries a non-empty summary or defers to an inherited one.</summary>
    public bool IsDocumented => !string.IsNullOrEmpty(Summary) || HasInheritDoc;
}

/// <summary>Members of one compiler-generated XML documentation file, keyed by member id.</summary>
public sealed class XmlDocumentationIndex
{
    private static readonly Regex _whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly Dictionary<string, XElement> _members;
    private readonly Dictionary<string, string> _summaries;

    private XmlDocumentationIndex(Dictionary<string, XElement> members, Dictionary<string, string> summaries, string filePath)
    {
        _members = members;
        _summaries = summaries;
        FilePath = filePath;
    }

    /// <summary>Path of the XML file the index was read from.</summary>
    public string FilePath { get; }

    /// <summary>Number of members that carry a summary.</summary>
    public int Count => _summaries.Count;

    /// <summary>Loads the documentation file next to an assembly, or returns null when the file does not exist.</summary>
    public static XmlDocumentationIndex? TryLoad(string assemblyPath)
    {
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        if (!File.Exists(xmlPath))
        {
            return null;
        }

        var document = XDocument.Load(xmlPath);
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var summaries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var member in document.Descendants("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (name == null)
            {
                continue;
            }

            members[name] = member;
            var summary = member.Element("summary");
            if (summary == null)
            {
                continue;
            }

            var text = Normalize(summary);
            if (text.Length > 0)
            {
                summaries[name] = text;
            }
        }

        return new XmlDocumentationIndex(members, summaries, xmlPath);
    }

    /// <summary>Returns the one-line summary of a member id, or null when the file has none.</summary>
    public string? FindSummary(string memberId)
    {
        return _summaries.TryGetValue(memberId, out var summary) ? summary : null;
    }

    /// <summary>Returns every documented section of a member id, or null when the file has no entry for it.</summary>
    public MemberDocumentation? FindDocumentation(string memberId)
    {
        if (!_members.TryGetValue(memberId, out var member))
        {
            return null;
        }

        return new MemberDocumentation(
            FindSummary(memberId),
            NormalizeOrNull(member.Element("remarks")),
            NormalizeOrNull(member.Element("returns")),
            NormalizeOrNull(member.Element("value")),
            NamedSections(member, "param", "name"),
            NamedSections(member, "typeparam", "name"),
            NamedSections(member, "exception", "cref"),
            member.Element("inheritdoc") != null);
    }

    /// <summary>True when the member has a non-empty summary or an inheritdoc element.</summary>
    public bool IsDocumented(string memberId)
    {
        return _summaries.ContainsKey(memberId) ||
               (_members.TryGetValue(memberId, out var member) && member.Element("inheritdoc") != null);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> NamedSections(XElement member, string elementName, string attributeName)
    {
        var sections = new List<KeyValuePair<string, string>>();
        foreach (var element in member.Elements(elementName))
        {
            var name = element.Attribute(attributeName)?.Value ?? string.Empty;
            sections.Add(new KeyValuePair<string, string>(ShortReference(name), Normalize(element)));
        }

        return sections;
    }

    private static string? NormalizeOrNull(XElement? element)
    {
        if (element == null)
        {
            return null;
        }

        var text = Normalize(element);
        return text.Length > 0 ? text : null;
    }

    private static string Normalize(XElement summary)
    {
        var builder = new StringBuilder();
        AppendText(builder, summary);
        return _whitespace.Replace(builder.ToString(), " ").Trim();
    }

    private static void AppendText(StringBuilder builder, XElement element)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
            else if (node is XElement child)
            {
                var reference = child.Attribute("cref")?.Value ?? child.Attribute("name")?.Value;
                if (child.Name.LocalName is "see" or "seealso" or "paramref" or "typeparamref" && reference != null)
                {
                    builder.Append(ShortReference(reference));
                }
                else
                {
                    AppendText(builder, child);
                }
            }
        }
    }

    private static string ShortReference(string reference)
    {
        var withoutPrefix = reference.Length > 2 && reference[1] == ':' ? reference[2..] : reference;
        var parenthesis = withoutPrefix.IndexOf('(');
        var path = parenthesis >= 0 ? withoutPrefix[..parenthesis] : withoutPrefix;
        var dot = path.LastIndexOf('.');
        var name = dot >= 0 ? path[(dot + 1)..] : path;
        var arity = name.IndexOf('`');
        return arity >= 0 ? name[..arity] : name;
    }
}
