using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Text in a popup window sits on the opaque background of the control it belongs to, exactly as it
/// does in the main window, so on the screen the two have to be the same glyph pixels: the same
/// antialiasing, the same weight, the same snapping. This reads both from the screen.
/// </summary>
[TestClass]
public sealed class PopupTextRenderingTests
{
    private const string SAMPLE = "Popup list item Rendering 0123";

    [TestMethod]
    public Task TextInAPopup_IsTheSamePixelsAsInTheMainWindow() => CaptureScene.RunAsync(async scene =>
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Reads the screen back through the Windows capture helper.");
        }

        var owner = new Button { Content = new TextBlock { Text = "Owner" }, Width = 120, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 0, 0) };
        var mainList = NewList();
        mainList.HorizontalAlignment = HorizontalAlignment.Left;
        mainList.VerticalAlignment = VerticalAlignment.Top;
        mainList.Margin = new Thickness(8, 120, 0, 0);
        var root = new Grid();
        root.Children(owner, mainList);
        var window = await scene.ShowAsync(root);

        var popupList = NewList();
        var popup = new Popup { Content = popupList, StaysOpen = true };
        popup.ShowAt(owner, owner.Bounds);
        await Task.Delay(800);

        var surface = popupList.ResolveInputHostWindow();
        if (surface == null || ReferenceEquals(surface, window))
        {
            Assert.Inconclusive("The popup opened inside its owner's surface, so there is no popup window to compare.");
        }

        await scene.Input.MoveAsync(window, new Point(window.ClientSize.Width - 4, window.ClientSize.Height - 4));
        await Task.Delay(500);

        var mainShot = ScreenCapture.OfClientArea(window.Handle);
        var popupShot = ScreenCapture.OfClientArea(surface!.Handle);
        double scale = window.GetDpi() / 96.0;

        // In the main window the list is searched inside its own box, so the owner button above it is left out.
        var mainText = FindText(mainShot, (int)(mainList.Bounds.X * scale) + 3, (int)(mainList.Bounds.Y * scale) + 3, (int)(mainList.Bounds.Right * scale) - 3, (int)(mainList.Bounds.Bottom * scale) - 3);
        // The popup window is wider than the list by its shadow, which is dark too: the list is where
        // its own background colour is, so the search runs inside the box that colour covers.
        var listInPopup = BoxOfColor(popupShot, MostCommonColor(popupShot, 0, 0, popupShot.Width, popupShot.Height));
        var popupText = FindText(popupShot, listInPopup.X + 3, listInPopup.Y + 3, listInPopup.X + listInPopup.Width - 3, listInPopup.Y + listInPopup.Height - 3);

        string backend = window.GraphicsFactory.Backend;
        Assert.IsTrue(mainText.Width > 20 && popupText.Width > 20, $"{backend}: the text was not found (main {mainText}, popup {popupText})");
        Assert.AreEqual((mainText.Width, mainText.Height), (popupText.Width, popupText.Height), $"{backend}: the text covers a different box in the popup (main {mainText}, popup {popupText})");

        int differing = 0;
        int largest = 0;
        int mainColored = 0;
        int popupColored = 0;
        for (int y = 0; y < mainText.Height; y++)
        {
            for (int x = 0; x < mainText.Width; x++)
            {
                var main = mainShot.At(mainText.X + x, mainText.Y + y);
                var inPopup = popupShot.At(popupText.X + x, popupText.Y + y);
                int delta = Math.Max(Math.Abs(main.B - inPopup.B), Math.Max(Math.Abs(main.G - inPopup.G), Math.Abs(main.R - inPopup.R)));
                if (delta > 0)
                {
                    differing++;
                    largest = Math.Max(largest, delta);
                }

                if (Math.Abs(main.R - main.B) > 12)
                {
                    mainColored++;
                }

                if (Math.Abs(inPopup.R - inPopup.B) > 12)
                {
                    popupColored++;
                }
            }
        }

        popup.Close();
        Assert.AreEqual(
            0,
            differing,
            $"{backend}: {differing} of {mainText.Width * mainText.Height} text pixels differ between the popup and the main window by up to {largest}; pixels with subpixel colour: main {mainColored}, popup {popupColored}");
    });

    private static ListBox NewList()
    {
        var list = new ListBox { Width = 240, Height = 72 };
        list.Items(SAMPLE, "second row", "third row");
        return list;
    }

    private static (int X, int Y, int Width, int Height) BoxOfColor(ScreenCapture shot, (byte B, byte G, byte R, byte A) color)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < shot.Height; y++)
        {
            for (int x = 0; x < shot.Width; x++)
            {
                var pixel = shot.At(x, y);
                if (pixel.B == color.B && pixel.G == color.G && pixel.R == color.R)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? default : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>The background of the list, which covers more of the area than anything else.</summary>
    private static (byte B, byte G, byte R, byte A) MostCommonColor(ScreenCapture shot, int left, int top, int right, int bottom)
    {
        var counts = new Dictionary<int, int>();
        int best = 0;
        int bestCount = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                var pixel = shot.At(x, y);
                int packed = pixel.B | (pixel.G << 8) | (pixel.R << 16);
                counts.TryGetValue(packed, out int count);
                counts[packed] = ++count;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = packed;
                }
            }
        }

        return ((byte)(best & 255), (byte)((best >> 8) & 255), (byte)((best >> 16) & 255), 255);
    }

    /// <summary>The box around the first row of pixels that stand out from the background of the area.</summary>
    private static (int X, int Y, int Width, int Height) FindText(ScreenCapture shot, int left, int top, int right, int bottom)
    {
        right = Math.Min(right, shot.Width);
        bottom = Math.Min(bottom, shot.Height);
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        int emptyRows = 0;
        var paper = MostCommonColor(shot, Math.Max(0, left), Math.Max(0, top), right, bottom);
        for (int y = Math.Max(0, top); y < bottom; y++)
        {
            bool rowHasInk = false;
            for (int x = Math.Max(0, left); x < right; x++)
            {
                var pixel = shot.At(x, y);
                if (Math.Abs(pixel.R - paper.R) + Math.Abs(pixel.G - paper.G) + Math.Abs(pixel.B - paper.B) > 240)
                {
                    rowHasInk = true;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxY >= 0 && !rowHasInk)
            {
                // The first row of text has ended.
                emptyRows++;
                if (emptyRows >= 3)
                {
                    break;
                }
            }
        }

        if (maxX < 0)
        {
            return default;
        }

        // A margin around the ink keeps the antialiased fringe of the glyphs in the comparison.
        return (minX - 2, minY - 2, maxX - minX + 5, maxY - minY + 5);
    }
}
