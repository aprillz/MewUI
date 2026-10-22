#if NET10_0_OR_GREATER
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MarkupAudit;

namespace MewUI.Test.Markup;

/// <summary>Enforces the first XML documentation coverage gate: every public member of the markup extension classes has a summary.</summary>
[TestClass]
public sealed class MarkupDocCoverageTests
{
    private const string EXTENSIONS_SUFFIX = "Extensions";
    private const string CORE_NAMESPACE_PREFIX = "Aprillz.MewUI";
    private const int MAX_LISTED_ROWS = 50;

    // The markup extension classes live in these two namespaces of the core assembly.
    private static readonly string[] _markupNamespaces = ["Aprillz.MewUI", "Aprillz.MewUI.Controls"];

    [TestMethod]
    public void MarkupExtensionClasses_HaveNoMissingSummaries()
    {
        var corePath = typeof(Element).Assembly.Location;
        var markupClasses = MarkupExtensionClasses(corePath);
        Assert.IsTrue(markupClasses.Count > 0, "The fluent inventory found no markup extension class in the core assembly.");

        var result = DocCoverageAnalyzer.Run(new DocCoverageOptions
        {
            AssemblyPaths = [corePath],
        });

        Assert.IsTrue(result.Scanned > 0, "The coverage scan saw no public members; the core assembly or its XML file was not found.");

        var rows = result.Missing
            .Where(member => markupClasses.Contains($"{member.Namespace}.{member.Type}"))
            .Select(static member => $"{member.Namespace}.{member.Type}.{member.Member} ({member.Kind})")
            .ToArray();

        if (rows.Length == 0)
        {
            return;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append("missing <summary>: ").Append(rows.Length).Append(" offending row(s) in ").Append(markupClasses.Count).Append(" markup extension classes\n");
        foreach (var row in rows.Take(MAX_LISTED_ROWS))
        {
            builder.Append("  ").Append(row).Append('\n');
        }

        if (rows.Length > MAX_LISTED_ROWS)
        {
            builder.Append("  ... and ").Append(rows.Length - MAX_LISTED_ROWS).Append(" more\n");
        }

        Assert.Fail(builder.ToString());
    }

    /// <summary>Full names of the *Extensions classes in the markup namespaces whose extension methods take a MewUI receiver (helpers over BCL types are not markup).</summary>
    private static HashSet<string> MarkupExtensionClasses(string corePath)
    {
        var audit = MarkupAuditor.Run(new AuditOptions { AssemblyPaths = [corePath] });
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in audit.Inventory)
        {
            var receivers = record.Receiver.IsGenericParameter ? record.Receiver.Constraints : [record.Receiver.Type];
            var dot = record.DeclaringType.LastIndexOf('.');
            var declaringNamespace = dot >= 0 ? record.DeclaringType[..dot] : string.Empty;
            if (_markupNamespaces.Contains(declaringNamespace) &&
                record.DeclaringType.EndsWith(EXTENSIONS_SUFFIX, StringComparison.Ordinal) &&
                receivers.Any(static receiver => receiver.StartsWith(CORE_NAMESPACE_PREFIX, StringComparison.Ordinal)))
            {
                classes.Add(record.DeclaringType);
            }
        }

        return classes;
    }
}
#endif
