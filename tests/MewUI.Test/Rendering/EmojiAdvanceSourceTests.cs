using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Emoji sequences reach the shaper as clusters that map to fewer glyphs than characters, and some
/// map to none at all. The advance source must still return one cumulative width per UTF-16 unit.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmojiAdvanceSourceTests
{
    private static readonly string[] _sequences =
    [
        "\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F", // England flag
        "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466",             // family, ZWJ
        "\U0001F44D\U0001F3FD",                                                    // skin tone
        "1️⃣",                                                           // keycap
        "‍‍",                                                            // bare joiners
        "\U000E0067\U000E007F",                                                    // bare tag characters
        "A\U0001F3F4\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007FB"
    ];

    [TestMethod]
    public void PrefixAdvancesCoverEveryCodeUnitOfAnEmojiSequence()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows backends only.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        using var d2d = new Direct2DGraphicsFactory();
        foreach ((string name, IGraphicsFactory factory) in new (string, IGraphicsFactory)[] { ("GDI", gdi), ("D2D", d2d) })
        {
            using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(96);
            using var font = factory.CreateFont("Segoe UI Emoji", 14, 96);
            foreach (string sequence in _sequences)
            {
                double[] advances = context.GetUtf16PrefixAdvances(sequence, font)!;

                Assert.HasCount(sequence.Length, advances, $"{name}: one entry per code unit.");
                for (int index = 1; index < advances.Length; index++)
                {
                    Assert.IsGreaterThanOrEqualTo(
                        advances[index - 1],
                        advances[index],
                        $"{name}: prefix advances must not go backwards at {index}.");
                }
            }
        }
    }

    [TestMethod]
    public void LayoutOfAnEmojiSequenceDoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows backends only.");
            return;
        }

        using var d2d = new Direct2DGraphicsFactory();
        string text = string.Join('\n', _sequences);
        var layout = d2d.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI Emoji", 14),
            Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap }
        });

        Assert.IsGreaterThan(0.0, layout.MeasuredSize.Width);
    }
}
