// Ported from Windows Terminal src/types/ut_types/CodepointWidthDetectorTests.cpp (MIT).

using Aprillz.MewUI.Terminal.Unicode;

namespace MewUI.Terminal.Test.Unicode;

[TestClass]
public sealed class CodepointWidthDetectorTests
{
    [TestMethod]
    public void GraphemeBreakTest()
    {
        var detector = new CodepointWidthDetector();
        var failures = new List<string>();

        foreach (var test in GraphemeBreakTestData.Official.Concat(GraphemeBreakTestData.Extra))
        {
            string text = string.Concat(test.Graphemes);

            var actual = new List<string>();
            var state = new GraphemeState();
            while (true)
            {
                bool ok = detector.GraphemeNext(ref state, text);
                actual.Add(text.Substring(state.Beg, state.Len));
                if (!ok)
                {
                    break;
                }
            }

            if (!actual.SequenceEqual(test.Graphemes))
            {
                failures.Add($"next: {test.Comment}");
            }

            actual.Clear();
            state = new GraphemeState();
            while (true)
            {
                bool ok = detector.GraphemePrev(ref state, text);
                actual.Add(text.Substring(state.Beg, state.Len));
                if (!ok)
                {
                    break;
                }
            }

            actual.Reverse();
            if (!actual.SequenceEqual(test.Graphemes))
            {
                failures.Add($"prev: {test.Comment}");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures.Take(20)));
    }

    [TestMethod]
    public void BasicGraphemes()
    {
        const string TEXT = "aͣeͤͤiͥ";
        var detector = new CodepointWidthDetector();

        int[] expectedAdvances = [2, 3, 2];
        int[] expectedWidths = [1, 1, 1];
        var actualAdvances = new List<int>();
        var actualWidths = new List<int>();

        var state = new GraphemeState();
        while (true)
        {
            bool ok = detector.GraphemeNext(ref state, TEXT);
            actualAdvances.Add(state.Len);
            actualWidths.Add(state.Width);
            if (!ok)
            {
                break;
            }
        }

        CollectionAssert.AreEqual(expectedAdvances, actualAdvances);
        CollectionAssert.AreEqual(expectedWidths, actualWidths);

        actualAdvances.Clear();
        actualWidths.Clear();
        state = new GraphemeState();
        while (true)
        {
            bool ok = detector.GraphemePrev(ref state, TEXT);
            actualAdvances.Add(state.Len);
            actualWidths.Add(state.Width);
            if (!ok)
            {
                break;
            }
        }

        actualAdvances.Reverse();
        actualWidths.Reverse();
        CollectionAssert.AreEqual(expectedAdvances, actualAdvances);
        CollectionAssert.AreEqual(expectedWidths, actualWidths);
    }

    [TestMethod]
    [DataRow((int)TextMeasurementMode.Graphemes, new[] { 3, 3, 0, 1 }, new[] { 2, 2, 2, 1 }, new[] { 1, 0, 3, 3 }, new[] { 1, 1, 2, 2 })]
    [DataRow((int)TextMeasurementMode.Wcswidth, new[] { 3, 1, 2, 0, 1 }, new[] { 1, 1, 2, 2, 1 }, new[] { 1, 0, 2, 1, 3 }, new[] { 1, 1, 2, 0, 1 })]
    [DataRow((int)TextMeasurementMode.Console, new[] { 2, 1, 0, 1, 2, 0, 1 }, new[] { 1, 0, 0, 0, 2, 2, 1 }, new[] { 1, 0, 2, 1, 0, 1, 2 }, new[] { 1, 1, 2, 0, 0, 0, 1 })]
    public void ChunkedText(int mode, int[] advancesNext, int[] widthsNext, int[] advancesPrev, int[] widthsPrev)
    {
        // A fully qualified rainbow flag split over two strings, then "a". Moving to the next string detaches the
        // state, which the original does by comparing the cluster pointer against the new string.
        string[] chunks = ["\U0001F3F3️", "‍\U0001F308", "a"];

        var detector = new CodepointWidthDetector();
        detector.Reset((TextMeasurementMode)mode);

        var actualAdvances = new List<int>();
        var actualWidths = new List<int>();
        var state = new GraphemeState();
        for (int chunk = 0; chunk < 3; chunk++)
        {
            state.Detach();
            bool ok;
            do
            {
                ok = detector.GraphemeNext(ref state, chunks[chunk]);
                actualAdvances.Add(state.Len);
                actualWidths.Add(state.Width);
            }
            while (ok);
        }

        CollectionAssert.AreEqual(advancesNext, actualAdvances, "next advances");
        CollectionAssert.AreEqual(widthsNext, actualWidths, "next widths");

        actualAdvances.Clear();
        actualWidths.Clear();
        state = new GraphemeState();
        for (int chunk = 2; chunk >= 0; chunk--)
        {
            state.Detach();
            bool ok;
            do
            {
                ok = detector.GraphemePrev(ref state, chunks[chunk]);
                actualAdvances.Add(state.Len);
                actualWidths.Add(state.Width);
            }
            while (ok);
        }

        CollectionAssert.AreEqual(advancesPrev, actualAdvances, "prev advances");
        CollectionAssert.AreEqual(widthsPrev, actualWidths, "prev widths");
    }

    [TestMethod]
    [DataRow((int)TextMeasurementMode.Graphemes)]
    [DataRow((int)TextMeasurementMode.Wcswidth)]
    public void AmbiguousWidthPolicy(int mode)
    {
        var detector = new CodepointWidthDetector();
        detector.Reset((TextMeasurementMode)mode);

        detector.AmbiguousWidth = 1;
        Assert.AreEqual(1, MeasureWidth(detector, "→"));
        detector.AmbiguousWidth = 2;
        Assert.AreEqual(2, MeasureWidth(detector, "→"));
    }

    private static int MeasureWidth(CodepointWidthDetector detector, string text)
    {
        var state = new GraphemeState();
        detector.GraphemeNext(ref state, text);
        return state.Width;
    }
}
