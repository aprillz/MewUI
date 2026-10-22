namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Selects which Markdown sections and rows are written.</summary>
public sealed class MarkdownWriteOptions
{
    /// <summary>Writes covered rows as well as missing ones.</summary>
    public bool ShowAll { get; init; }

    public bool IncludeProperties { get; init; } = true;

    public bool IncludeEvents { get; init; } = true;
}

/// <summary>Writes the audit as the Markdown tables the previous CLI produced, plus a findings table.</summary>
public static class MarkdownAuditWriter
{
    private const string MISSING_CELL = "**missing**";

    public static void Write(AuditResult result, TextWriter writer, MarkdownWriteOptions options)
    {
        if (options.IncludeProperties)
        {
            writer.WriteLine("## Properties");
            writer.WriteLine();
            writer.WriteLine("|Type|Property|Property Type|Same-name Extension|");
            writer.WriteLine("|-|-|-|-|");

            foreach (var entry in SelectRows(result.Properties, options.ShowAll))
            {
                writer.WriteLine($"|`{entry.ReceiverDisplay}`|`{entry.Member}`|`{entry.MemberType}`|{FormatCoverage(entry)}|");
            }
        }

        if (options.IncludeEvents)
        {
            writer.WriteLine();
            writer.WriteLine("## Events");
            writer.WriteLine();
            writer.WriteLine("|Type|Event|Handler Type|Extension|");
            writer.WriteLine("|-|-|-|-|");

            foreach (var entry in SelectRows(result.Events, options.ShowAll))
            {
                writer.WriteLine($"|`{entry.ReceiverDisplay}`|`{entry.Member}`|`{entry.MemberType}`|{FormatCoverage(entry)}|");
            }

            writer.WriteLine();
            writer.WriteLine("## Ambiguous Event Extensions");
            writer.WriteLine();
            writer.WriteLine("|Receiver Type|Extension|First Handler|Second Handler|");
            writer.WriteLine("|-|-|-|-|");

            foreach (var row in result.AmbiguousEventExtensions)
            {
                writer.WriteLine($"|`{row.ReceiverDisplay}`|`{row.Extension}`|`{row.FirstHandler}`|`{row.SecondHandler}`|");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Findings");
        writer.WriteLine();
        writer.WriteLine("|Code|Assembly|Subject|Message|");
        writer.WriteLine("|-|-|-|-|");

        foreach (var finding in result.Findings)
        {
            writer.WriteLine($"|`{finding.Code}`|`{finding.Assembly}`|`{finding.Subject}`|{Escape(finding.Message)}|");
        }
    }

    private static IEnumerable<MemberLedgerEntry> SelectRows(IReadOnlyList<MemberLedgerEntry> entries, bool showAll)
    {
        return showAll ? entries : entries.Where(static entry => entry.IsMissing);
    }

    private static string FormatCoverage(MemberLedgerEntry entry)
    {
        var names = string.Join("<br>", entry.Extensions.Select(static name => $"`{name}`"));
        switch (entry.Disposition)
        {
            case Dispositions.CONVENTIONAL:
            case Dispositions.ON_PREFIX:
                return names;
            case Dispositions.ANNOTATED:
                return $"annotated: {names}";
            case Dispositions.EXCLUDED:
                return $"excluded: {Escape(entry.ExclusionReason ?? string.Empty)}";
            default:
                if (entry.MultiArgumentExtensions.Count == 0)
                {
                    return MISSING_CELL;
                }

                var multi = string.Join(", ", entry.MultiArgumentExtensions.Select(static name => $"`{name}`"));
                return $"{MISSING_CELL} (multi-argument: {multi})";
        }
    }

    private static string Escape(string text) => text.Replace("|", "\\|");
}
