using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// The measured fragments have to describe the same text the per-grapheme cluster list did: the same
/// pieces in the same order, and the same width for every grapheme in them.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FragmentMeasurementTests
{
    private const string ASCII = "The quick brown fox jumps over the lazy dog";
    private const string MIXED = "A\U0001F600é한 tab\there\r\n한글 mixed \U0001F600\U0001F600 end";
    private const string TABS = "\tone\ttwo\tthree";

    [TestMethod]
    [DataRow(ASCII, 0.0)]
    [DataRow(MIXED, 0.0)]
    [DataRow(TABS, 0.0)]
    [DataRow(ASCII, 1.5)]
    [DataRow(MIXED, 2.0)]
    public void EveryGraphemeMeasuresAsItsClusterDid(string text, double letterSpacing)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var engine = (ManagedTextEngine)factory.TextEngine;
        var snapshot = Snapshot(text, letterSpacing);
        using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(snapshot.Dpi);

        var clusters = engine.MeasureClusters(snapshot, 0, text.Length);
        var fragments = engine.MeasureFragments(context, snapshot);

        int fragmentIndex = 0;
        int clusterIndex = 0;
        while (clusterIndex < clusters.Count)
        {
            Assert.IsLessThan(fragments.Count, fragmentIndex, "The fragments ran out before the clusters did.");
            var fragment = fragments.Items[fragmentIndex];
            var cluster = clusters[clusterIndex];

            Assert.AreEqual(cluster.Start, fragment.TextStart,
                $"Fragment {fragmentIndex} started somewhere the clusters did not.");
            Assert.AreEqual(Kind(cluster.Kind), fragment.Kind,
                $"Fragment {fragmentIndex} at {fragment.TextStart} is a different kind of piece.");

            if (fragment.Kind != ManagedTextRunKind.Text)
            {
                Assert.AreEqual(cluster.Width, fragment.Width, 1e-6,
                    $"Fragment {fragmentIndex} at {fragment.TextStart} measured differently.");
                clusterIndex++;
                fragmentIndex++;
                continue;
            }

            // Every cluster the fragment covers has to measure the same as the difference of the
            // advances at its two ends.
            double total = 0;
            while (clusterIndex < clusters.Count && clusters[clusterIndex].End <= fragment.TextEnd)
            {
                var covered = clusters[clusterIndex];
                double width = fragments.AdvanceBetween(in fragment, covered.Start, covered.End);
                Assert.AreEqual(covered.Width, width, 1e-4,
                    $"Grapheme at {covered.Start} measured {width} against the cluster's {covered.Width}.");
                total += covered.Width;
                clusterIndex++;
            }

            Assert.AreEqual(total, fragment.Width, 1e-4,
                $"Fragment {fragmentIndex} at {fragment.TextStart} does not add up to its graphemes.");
            fragmentIndex++;
        }

        Assert.AreEqual(fragments.Count, fragmentIndex, "The clusters ran out before the fragments did.");
    }

    [TestMethod]
    public void FragmentsCoverTheTextInOrder()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var engine = (ManagedTextEngine)factory.TextEngine;
        var snapshot = Snapshot(MIXED, 0);
        using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(snapshot.Dpi);

        var fragments = engine.MeasureFragments(context, snapshot);

        Assert.IsGreaterThan(0, fragments.Count);
        Assert.AreEqual(0, fragments.Items[0].TextStart);
        Assert.AreEqual(MIXED.Length, fragments.Items[fragments.Count - 1].TextEnd);
        for (int index = 1; index < fragments.Count; index++)
        {
            Assert.AreEqual(fragments.Items[index - 1].TextEnd, fragments.Items[index].TextStart,
                $"Fragments {index - 1} and {index} left a gap.");
        }
    }

    private static ManagedTextRunKind Kind(ManagedTextClusterKind kind) => kind switch
    {
        ManagedTextClusterKind.Tab => ManagedTextRunKind.Tab,
        ManagedTextClusterKind.NewLine => ManagedTextRunKind.NewLine,
        ManagedTextClusterKind.Inline => ManagedTextRunKind.Inline,
        _ => ManagedTextRunKind.Text
    };

    private static TextLayoutRequestSnapshot Snapshot(string text, double letterSpacing)
        => TextLayoutRequestSnapshot.Create(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 12),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap,
                LetterSpacing = letterSpacing
            }
        });
}
