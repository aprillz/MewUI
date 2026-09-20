using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Pins the pixels an <see cref="ItemContainer"/> paints for selection, hover and alternating rows,
/// so moving those fills out of the items control and into the container cannot change what a list
/// looks like. Each test renders a laid-out <see cref="ListBox"/> into an offscreen GDI surface and
/// reads the row's own pixels.
/// Not parallelizable: renders through the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemContainerBackgroundTests
{
    private const int SURFACE_WIDTH = 240;
    private const int SURFACE_HEIGHT = 200;
    private const double ITEM_HEIGHT = 24;
    private const int ITEM_COUNT = 6;

    // Sampled from the row's trailing edge inward: the item's text sits against the leading edge,
    // and the row's vertical middle keeps the sample clear of any antialiased rounded corner.
    private const int SAMPLE_INSET = 4;

    [TestMethod]
    public void SelectedRow_PaintsTheThemeSelectionBackground()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(zebraStriping: false);
        list.SelectedIndex = 2;
        window.PerformLayout();

        var expected = list.ThemeInternal.Palette.SelectionBackground;
        byte[] pixels = RenderList(list);

        AssertRowColor(pixels, list, 2, expected, "the selected row");
        AssertRowColorDiffers(pixels, list, 1, expected, "an unselected row");
        AssertRowColorDiffers(pixels, list, 3, expected, "an unselected row");
        window.Close();
    }

    [TestMethod]
    public void TheContainer_HasNoBackgroundPropertiesOfItsOwn()
    {
        // A row's look in each state belongs to its style, as it does for every other control: the state
        // goes into the visual state, and the triggers of the style set Background. Properties that carry
        // the colours on every container would be a second way to do the same, set row by row.
        var declared = typeof(ItemContainer)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Where(name => name.Contains("Background") || name.Contains("Hovered") || name.Contains("Alternate"))
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

        Assert.AreEqual(0, declared.Length, "public members of ItemContainer about its backgrounds: " + string.Join(", ", declared));
    }

    [TestMethod]
    public void HoveredAndSelectedRows_TakeTheirBackgroundFromTheStyle()
    {
        var (window, list) = CreateList(zebraStriping: false);
        var palette = list.ThemeInternal.Palette;

        var hovered = ContainerBounds(list, 2);
        window.SendMouseMove(new Point(hovered.X + hovered.Width / 2, hovered.Y + hovered.Height / 2));
        list.SelectedIndex = 4;
        window.PerformLayout();
        window.PerformLayout();

        Assert.AreEqual(palette.ControlBackground.Lerp(palette.Accent, 0.15), Container(list, 2).Background, "the hovered row");
        Assert.AreEqual(palette.SelectionBackground, Container(list, 4).Background, "the selected row");
        Assert.AreEqual(0, Container(list, 0).Background.A, "a row that is neither");
    }

    [TestMethod]
    public void HoveredRow_PaintsTheHoverBackground()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(zebraStriping: false);
        var palette = list.ThemeInternal.Palette;
        var expected = palette.ControlBackground.Lerp(palette.Accent, 0.15);

        var hovered = ContainerBounds(list, 2);
        window.SendMouseMove(new Point(hovered.X + hovered.Width / 2, hovered.Y + hovered.Height / 2));

        Assert.IsTrue(Container(list, 2).IsHovered, "the mouse move did not put the row into the hovered state");

        // A frame brings the visual states up to date before it draws, which is where the style puts the hover in.
        window.PerformLayout();

        byte[] pixels = RenderList(list);

        AssertRowColor(pixels, list, 2, expected, "the hovered row");
        AssertRowColorDiffers(pixels, list, 1, expected, "a row the pointer is not over");
        window.Close();
    }

    [TestMethod]
    public void AlternatingRows_PaintTheZebraBackground()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(zebraStriping: true);
        var theme = list.ThemeInternal;
        var expected = theme.Palette.ControlBackground.Lerp(theme.Palette.ButtonFace, theme.IsDark ? 0.45 : 0.33);

        byte[] pixels = RenderList(list);

        AssertRowColor(pixels, list, 1, expected, "an odd row");
        AssertRowColor(pixels, list, 3, expected, "an odd row");
        AssertRowColorDiffers(pixels, list, 0, expected, "an even row");
        AssertRowColorDiffers(pixels, list, 2, expected, "an even row");
        window.Close();
    }

    [TestMethod]
    public void SelectionWins_OverTheZebraAndHoverBackgrounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(zebraStriping: true);
        var theme = list.ThemeInternal;
        var palette = theme.Palette;
        var selection = palette.SelectionBackground;
        var zebra = palette.ControlBackground.Lerp(palette.ButtonFace, theme.IsDark ? 0.45 : 0.33);
        var hover = palette.ControlBackground.Lerp(palette.Accent, 0.15);

        // Row 1 is an alternating row, so it can carry all three states at once.
        list.SelectedIndex = 1;
        window.PerformLayout();
        var selected = ContainerBounds(list, 1);
        window.SendMouseMove(new Point(selected.X + selected.Width / 2, selected.Y + selected.Height / 2));

        var container = Container(list, 1);
        Assert.IsTrue(container.IsSelected, "the row under test is not selected");
        Assert.IsTrue(container.IsAlternate, "the row under test is not an alternating row");
        Assert.IsTrue(container.IsHovered, "the row under test is not hovered");

        byte[] pixels = RenderList(list);

        AssertRowColor(pixels, list, 1, selection, "the selected alternating row under the pointer");
        AssertRowColorDiffers(pixels, list, 1, zebra, "the selected row");
        AssertRowColorDiffers(pixels, list, 1, hover, "the selected row");
        window.Close();
    }

    private static (Window Window, ListBox List) CreateList(bool zebraStriping)
    {
        var window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        var list = new ListBox
        {
            ItemHeight = ITEM_HEIGHT,
            ZebraStriping = zebraStriping,
        };
        list.ItemsSource = ItemsView.Create(
            Enumerable.Range(0, ITEM_COUNT).Select(index => "Item " + index).ToList());
        window.Content = list;
        window.PerformLayout();
        return (window, list);
    }

    private static ItemContainer Container(ListBox list, int index)
    {
        ItemContainer? found = null;
        list.VisitRealizedContainers((realizedIndex, element) =>
        {
            if (realizedIndex == index && element is ItemContainer container)
            {
                found = container;
            }
        });

        Assert.IsNotNull(found, "item " + index + " has no realized ItemContainer");
        return found;
    }

    private static Rect ContainerBounds(ListBox list, int index) => Container(list, index).Bounds;

    private static byte[] RenderList(ListBox list)
    {
        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            list.Render(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var source = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[SURFACE_WIDTH * SURFACE_HEIGHT * 4];
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            source.Slice(row * stride, SURFACE_WIDTH * 4).CopyTo(copy.AsSpan(row * SURFACE_WIDTH * 4));
        }

        return copy;
    }

    private static (int X, int Y) SamplePoint(ListBox list, int index)
    {
        var bounds = ContainerBounds(list, index);
        int x = (int)Math.Round(bounds.Right) - SAMPLE_INSET;
        int y = (int)Math.Round(bounds.Y + bounds.Height / 2);
        Assert.IsTrue(x >= 0 && x < SURFACE_WIDTH && y >= 0 && y < SURFACE_HEIGHT,
            $"row {index} sampled at ({x},{y}), outside the {SURFACE_WIDTH}x{SURFACE_HEIGHT} surface");
        return (x, y);
    }

    private static Color ReadPixel(byte[] pixels, int x, int y)
    {
        int offset = (y * SURFACE_WIDTH + x) * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static void AssertRowColor(byte[] pixels, ListBox list, int index, Color expected, string what)
    {
        var (x, y) = SamplePoint(list, index);
        var actual = ReadPixel(pixels, x, y);
        if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
        {
            Assert.Fail(
                $"{what} (index {index}) sampled at ({x},{y}): expected RGB({expected.R},{expected.G},{expected.B}) " +
                $"but read RGB({actual.R},{actual.G},{actual.B}).");
        }
    }

    private static void AssertRowColorDiffers(byte[] pixels, ListBox list, int index, Color unexpected, string what)
    {
        var (x, y) = SamplePoint(list, index);
        var actual = ReadPixel(pixels, x, y);
        if (actual.R == unexpected.R && actual.G == unexpected.G && actual.B == unexpected.B)
        {
            Assert.Fail(
                $"{what} (index {index}) sampled at ({x},{y}): read RGB({actual.R},{actual.G},{actual.B}), " +
                "which is the color that row must not carry.");
        }
    }
}
