using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;

using SvgImageSource = Aprillz.MewUI.Svg.SvgImageSource;

namespace MewUI.Svg.Test.Rendering;

/// <summary>
/// Holds <see cref="SimpleSvgSource"/> to the extension's rendering for the markup both are meant to
/// agree on. The extension is the reference: it reads the full format, so any difference outside the
/// documented out-of-range set is a defect in the core reader.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SimpleSvgSourceParityTests
{
    private const int RENDER_SIZE = 64;

    // Per-channel tolerance. Both readers drive the same backend through the same brush and geometry
    // types, so the only expected spread is antialiasing at curve edges.
    private const int CHANNEL_TOLERANCE = 2;

    // Share of pixels allowed to exceed the per-channel tolerance.
    private const double PIXEL_TOLERANCE = 0.01;

    /// <summary>Root fill inheritance, negative viewBox origin, px lengths, T command, empty subpath.</summary>
    private const string MATERIAL_SHAPED = """
        <svg xmlns="http://www.w3.org/2000/svg" height="24px" viewBox="0 -960 960 960" width="24px" fill="#1f1f1f"><path d="M240-200h120v-200q0-17 11.5-28.5T400-440h160q17 0 28.5 11.5T600-400v200h120v-360L480-740 240-560v360Zm-80 0v-360q0-19 8.5-36t23.5-28l240-180q21-16 48-16t48 16l240 180q15 11 23.5 28t8.5 36v360q0 33-23.5 56.5T720-120H560q-17 0-28.5-11.5T520-160v-200h-80v200q0 17-11.5 28.5T400-120H240q-33 0-56.5-23.5T160-200Zm320-270Z"/></svg>
        """;

    /// <summary>User-space gradients, gradientTransform, stop-opacity, root fill="none" inheritance.</summary>
    private const string FLUENT_SHAPED = """
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M14 11.5V5.5L8 4.5L2 5.5V11.5C2 12.8807 3.11929 14 4.5 14H11.5C12.8807 14 14 12.8807 14 11.5Z" fill="url(#p0)"/>
        <path d="M14 4.5C14 3.11929 12.8807 2 11.5 2H4.5C3.11929 2 2 3.11929 2 4.5V6H14V4.5Z" fill="url(#p1)"/>
        <defs>
        <linearGradient id="p0" x1="2" y1="9.25" x2="14" y2="9.25" gradientUnits="userSpaceOnUse">
        <stop stop-color="#7C6CF5"/>
        <stop offset="1" stop-color="#4A3EC0" stop-opacity="0.85"/>
        </linearGradient>
        <radialGradient id="p1" cx="0" cy="0" r="1" gradientUnits="userSpaceOnUse" gradientTransform="translate(8 4) rotate(90) scale(4 8)">
        <stop stop-color="#F0EEFF"/>
        <stop offset="1" stop-color="#9B8FED"/>
        </radialGradient>
        </defs>
        </svg>
        """;

    /// <summary>No viewBox, shapes other than path, strokes with joins and a rounded rect.</summary>
    private const string GODOT_SHAPED = """
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"><rect x="1" y="1" width="14" height="10" rx="2" fill="#5fb2ff"/><circle cx="8" cy="6" r="3" fill="#fc9c9c" stroke="#242424" stroke-width="2"/><path fill="none" stroke="#fc9c9c" stroke-width="2" stroke-linejoin="round" d="m8 12 4 2-8 0z"/></svg>
        """;

    /// <summary>
    /// The same drawing with and without <c>paint-order</c>. Kept out of the parity set on purpose:
    /// the extension has no notion of paint order and always fills before stroking, so agreeing with
    /// it here would mean the core reader ignores the attribute too.
    /// </summary>
    private const string PAINT_ORDER_DEFAULT = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><circle cx="8" cy="8" r="5" fill="#ffffff" stroke="#000000" stroke-width="4"/></svg>
        """;

    private const string PAINT_ORDER_STROKE_FIRST = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><circle cx="8" cy="8" r="5" fill="#ffffff" stroke="#000000" stroke-width="4" paint-order="stroke markers fill"/></svg>
        """;

    /// <summary>Ellipse, polygon, polyline, line, group transform, group opacity, evenodd fill.</summary>
    private const string SHAPES_AND_GROUPS = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
          <g transform="translate(2 2) scale(0.9)" opacity="0.6">
            <ellipse cx="10" cy="10" rx="8" ry="5" fill="#3366cc"/>
            <polygon points="18,2 30,2 24,14" fill="#cc6633"/>
            <polyline points="2,26 10,20 18,26 26,20" fill="none" stroke="#224466" stroke-width="2"/>
            <line x1="2" y1="30" x2="30" y2="30" stroke="#888" stroke-width="1.5"/>
          </g>
          <path fill-rule="evenodd" fill="#2a2a2a" d="M4 4h10v10H4zM6 6h6v6H6z"/>
        </svg>
        """;

    /// <summary>clipPath, inline style declarations, rgb-less hex shorthand, named color.</summary>
    private const string CLIP_AND_INLINE_STYLE = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <defs><clipPath id="c"><circle cx="12" cy="12" r="9"/></clipPath></defs>
          <g clip-path="url(#c)">
            <rect x="0" y="0" width="24" height="12" style="fill:#f60;fill-opacity:0.8"/>
            <rect x="0" y="12" width="24" height="12" fill="navy"/>
          </g>
        </svg>
        """;

    public static IEnumerable<object[]> Fixtures =>
    [
        ["material", MATERIAL_SHAPED],
        ["fluent", FLUENT_SHAPED],
        ["godot", GODOT_SHAPED],
        ["shapes-and-groups", SHAPES_AND_GROUPS],
        ["clip-and-inline-style", CLIP_AND_INLINE_STYLE],
    ];

    [TestMethod]
    [DynamicData(nameof(Fixtures))]
    public void Render_MatchesExtension(string name, string markup)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var core = RenderToPixels(SimpleSvgSource.FromString(markup));
        var reference = RenderToPixels(SvgImageSource.FromString(markup));

        double mismatch = MismatchRatio(core, reference);
        Assert.IsLessThanOrEqualTo(
            PIXEL_TOLERANCE,
            mismatch,
            $"{name}: {mismatch:P2} of pixels differ from the extension (allowed {PIXEL_TOLERANCE:P0}).");
    }

    [TestMethod]
    public void Render_MatchesExtensionForRepositoryLogo()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        string path = Path.Combine(RepositoryRoot(), "assets", "logo", "logo_h.svg");
        Assert.IsTrue(File.Exists(path), $"Missing {path}");

        string markup = File.ReadAllText(path);
        var core = RenderToPixels(SimpleSvgSource.FromString(markup));
        var reference = RenderToPixels(SvgImageSource.FromString(markup));

        double mismatch = MismatchRatio(core, reference);
        Assert.IsLessThanOrEqualTo(
            PIXEL_TOLERANCE,
            mismatch,
            $"logo_h: {mismatch:P2} of pixels differ from the extension (allowed {PIXEL_TOLERANCE:P0}).");
    }

    [TestMethod]
    public void IntrinsicSize_ComesFromTheViewBoxWhenBothAreDeclared()
    {
        var source = SimpleSvgSource.FromString(MATERIAL_SHAPED);
        Assert.AreEqual(24.0, source.IntrinsicSize.Width, 0.001);
        Assert.AreEqual(24.0, source.IntrinsicSize.Height, 0.001);
    }

    [TestMethod]
    public void IntrinsicSize_FallsBackToWidthAndHeightWithoutAViewBox()
    {
        var source = SimpleSvgSource.FromString(GODOT_SHAPED);
        Assert.AreEqual(16.0, source.IntrinsicSize.Width, 0.001);
        Assert.AreEqual(16.0, source.IntrinsicSize.Height, 0.001);
    }

    [TestMethod]
    public void Tint_ReplacesTheColorInheritedFromTheRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // The Material icon declares its color on the root only, so the whole drawing follows the tint.
        var source = SimpleSvgSource.FromString(MATERIAL_SHAPED);
        var untinted = RenderToPixels(source);

        source.Tint = Color.FromRgb(0xE0, 0x30, 0x30);
        var tinted = RenderToPixels(source);

        Assert.IsGreaterThan(0.05, MismatchRatio(untinted, tinted), "Tint did not change the drawing.");
    }

    [TestMethod]
    public void Tint_LeavesColorDeclaredOnTheShapeAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // Every shape here declares its own paint, so nothing inherits from the root.
        var source = SimpleSvgSource.FromString(SHAPES_AND_GROUPS);
        var untinted = RenderToPixels(source);

        source.Tint = Color.FromRgb(0xE0, 0x30, 0x30);
        var tinted = RenderToPixels(source);

        Assert.AreEqual(0.0, MismatchRatio(untinted, tinted), 0.0001);
    }

    [TestMethod]
    public void PaintOrder_PutsTheStrokeUnderTheFill()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var stroked = RenderToPixels(SimpleSvgSource.FromString(PAINT_ORDER_DEFAULT));
        var strokeFirst = RenderToPixels(SimpleSvgSource.FromString(PAINT_ORDER_STROKE_FIRST));

        // A thick stroke straddles the outline, so drawing it under the fill widens the white centre
        // by half the stroke. The pixel just inside the outline flips from stroke black to fill white.
        int probe = ((RENDER_SIZE / 2) * RENDER_SIZE + (RENDER_SIZE / 2) + 13) * 4;
        Assert.IsLessThan(0x40, (int)stroked[probe + 2], "Expected the stroke to cover this pixel by default.");
        Assert.IsGreaterThan(0xC0, (int)strokeFirst[probe + 2], "Expected the fill to cover this pixel under paint-order.");
    }

    [TestMethod]
    public void FromString_ThrowsOnMalformedMarkup()
    {
        Assert.ThrowsExactly<FormatException>(() => SimpleSvgSource.FromString("<svg><path d="));
    }

    private static byte[] RenderToPixels(IVectorImageSource source)
    {
        var factory = (GdiGraphicsFactory)Application.DefaultGraphicsFactory!;
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(RENDER_SIZE, RENDER_SIZE, 1));
        using var context = factory.CreateContext(surface);

        context.BeginFrame(surface);
        try
        {
            ((ICpuPixelSurface)surface).Clear(Color.Transparent);
            source.Render(context, FitRect(source.IntrinsicSize));
        }
        finally
        {
            context.EndFrame();
        }

        return ((ICpuPixelSurface)surface).CopyPixels();
    }

    // Mirrors what the Image control hands a vector source: a uniform fit, centred.
    private static Rect FitRect(Size intrinsic)
    {
        if (intrinsic.Width <= 0 || intrinsic.Height <= 0)
        {
            return new Rect(0, 0, RENDER_SIZE, RENDER_SIZE);
        }

        double scale = Math.Min(RENDER_SIZE / intrinsic.Width, RENDER_SIZE / intrinsic.Height);
        double width = intrinsic.Width * scale;
        double height = intrinsic.Height * scale;
        return new Rect((RENDER_SIZE - width) / 2, (RENDER_SIZE - height) / 2, width, height);
    }

    private static double MismatchRatio(byte[] left, byte[] right)
    {
        Assert.HasCount(left.Length, right, "Surfaces differ in size.");

        int differing = 0;
        int pixels = left.Length / 4;
        for (int i = 0; i < left.Length; i += 4)
        {
            if (Math.Abs(left[i] - right[i]) > CHANNEL_TOLERANCE ||
                Math.Abs(left[i + 1] - right[i + 1]) > CHANNEL_TOLERANCE ||
                Math.Abs(left[i + 2] - right[i + 2]) > CHANNEL_TOLERANCE ||
                Math.Abs(left[i + 3] - right[i + 3]) > CHANNEL_TOLERANCE)
            {
                differing++;
            }
        }

        return pixels == 0 ? 0 : (double)differing / pixels;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MewUI.Dev.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not locate the repository root from the test output folder.");
        return directory.FullName;
    }
}
