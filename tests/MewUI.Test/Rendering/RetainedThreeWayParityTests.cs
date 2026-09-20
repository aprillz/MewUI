extern alias MewVGWin32;

using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A frame can be drawn straight from the visuals, replayed whole from the scene, or replayed only where
/// something changed. On every backend the three have to end at the same pixels, antialiased edges,
/// overlapping siblings and text included.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedThreeWayParityTests
{
    private const int WIDTH = 260;
    private const int HEIGHT = 200;
    private const int CHANGE_ROUNDS = 6;

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("Direct2D")]
    [DataRow("MewVG")]
    public void Immediate_WholeReplay_AndPartialReplay_EndAtTheSamePixels(string backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend switch
        {
            "Gdi" => new GdiGraphicsFactory(),
            "Direct2D" => new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(),
            _ => new MewVGWin32GraphicsFactory(),
        };
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;

        var lower = new Border
        {
            Width = 120,
            Height = 80,
            CornerRadius = 12,
            BorderThickness = 1.5,
            BorderBrush = Color.FromArgb(255, 20, 60, 140),
            Background = Color.FromArgb(255, 200, 220, 250),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16.5, 14.5, 0, 0),
            Child = new TextBlock { Text = "lower sibling", Margin = new Thickness(8) },
        };
        var upper = new Border
        {
            Width = 120,
            Height = 80,
            CornerRadius = 20,
            BorderThickness = 2,
            BorderBrush = Color.FromArgb(255, 150, 60, 20),
            Background = Color.FromArgb(230, 250, 225, 190),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(90, 60, 0, 0),
            Child = new TextBlock { Text = "upper sibling", Margin = new Thickness(8) },
        };
        var check = new CheckBox
        {
            Content = new TextBlock { Text = "checked" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 0, 12),
        };
        var layers = new Grid();
        layers.Children(lower, upper, check);
        var backdrop = new Border { Background = Color.FromArgb(255, 248, 248, 248), Child = layers };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = backdrop;
        window.PerformLayout();
        using var partial = CreateSurface(factory);
        Frames(window, partial, 3);

        int partialFrames = 0;
        for (int round = 0; round < CHANGE_ROUNDS; round++)
        {
            // Each round changes one sibling that the other overlaps, so the repaint crosses antialiased edges.
            if ((round & 1) == 0)
            {
                lower.Background = Color.FromArgb(255, (byte)(180 + (round * 10)), 220, 250);
            }
            else
            {
                upper.BorderBrush = Color.FromArgb(255, 150, (byte)(60 + (round * 20)), 20);
            }

            check.IsChecked = (round & 1) == 0;
            Frames(window, partial, 1);
            if (window.LastRetainedDamage is Rect area && area.Width > 0)
            {
                partialFrames++;
            }
        }

        Assert.IsTrue(partialFrames > 0, $"{backend}: no frame took the partial path ({window.LastWholeFrameReason})");

        using var immediate = CreateSurface(factory);
        window.RenderReferenceFrameToSurface(immediate);

        using var whole = CreateSurface(factory);
        var scene = (RenderScene)typeof(Window).GetField("_renderScene", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        scene.RequestFullDamage();
        window.RenderFrameToSurface(whole);
        Assert.IsNull(window.LastRetainedDamage, $"{backend}: the frame asked to be whole repainted only {window.LastRetainedDamage}");

        var immediatePixels = Read(factory, immediate);
        AssertSame(backend, "a whole replay", immediatePixels, Read(factory, whole), allowedDelta: 0);

        // The vector backend rasterizes a partial frame's clip edge one step off; the rounds above show it does not add up.
        int allowedDelta = factory is MewVGWin32GraphicsFactory ? 1 : 0;
        AssertSame(backend, "repeated partial replays", immediatePixels, Read(factory, partial), allowedDelta);
    }

    private static IRenderSurface CreateSurface(IGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static byte[] Read(IGraphicsFactory factory, IRenderSurface surface)
    {
        var pixels = new byte[WIDTH * HEIGHT * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, WIDTH * 4), "the surface could not be read back");
        return pixels;
    }

    private static void AssertSame(string backend, string what, byte[] expected, byte[] actual, int allowedDelta)
    {
        int differing = 0;
        int largest = 0;
        string first = "";
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            int delta = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                delta = Math.Max(delta, Math.Abs(expected[offset + channel] - actual[offset + channel]));
            }

            if (delta > allowedDelta)
            {
                if (differing == 0)
                {
                    first = $"({offset / 4 % WIDTH},{offset / 4 / WIDTH})";
                }

                differing++;
                largest = Math.Max(largest, delta);
            }
        }

        Assert.AreEqual(0, differing, $"{backend}: {what} differs from a frame drawn straight from the visuals in {differing} pixels, by up to {largest}, first at {first}");
    }
}
