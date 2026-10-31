using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The framework draws a host's children from the data the host exposes: VisitChildren order, GetChildClip,
/// TryGetChildTransform, the empty-bounds rule and the held-out-of-view flag for children the host does not
/// want drawn. The scene builds the same structure into its plan.
/// Not parallelizable: the retained tests swap the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FrameworkDrawnChildrenTests
{
    private const int SURFACE_SIZE = 120;
    private static readonly Color _first = Color.FromArgb(255, 200, 40, 40);
    private static readonly Color _second = Color.FromArgb(255, 40, 40, 200);
    private static readonly Color _overlay = Color.FromArgb(255, 20, 160, 20);

    [TestMethod]
    public void Children_AreDrawnInVisitOrder_LaterOnesOnTop()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost();
        Lay(host);
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);

        Assert.AreEqual(_first, ReadPixel(pixels, 10, 10), "the first child shows where only it was drawn");
        Assert.AreEqual(_second, ReadPixel(pixels, 50, 50), "the second child covers the first where they overlap");
    }

    [TestMethod]
    public void Overlay_IsDrawnOverTheChildren_AndIsASecondSlot()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost { DrawsOverlay = true };
        Lay(host);
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);
        var plan = Plan(host);

        Assert.AreEqual(_overlay, ReadPixel(pixels, 50, 50), "the overlay covers the children");
        Assert.AreEqual(2, plan.SlotCount, "an overlay is the second content slot");
        Assert.AreEqual(CompositionEntryKind.Content, plan.Entries[^1].Kind);
        Assert.AreEqual(1, plan.Entries[^1].SlotIndex);
    }

    [TestMethod]
    public void AChildArrangedWithoutArea_IsNeitherDrawnNorPlanned()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost { HidesSecond = true };
        Lay(host);
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);
        var plan = Plan(host);

        Assert.AreEqual(_first, ReadPixel(pixels, 50, 50), "the hidden child left the first one showing");
        Assert.AreEqual(1, plan.Entries.Count(entry => entry.Kind == CompositionEntryKind.Child));
        Assert.IsNull(host.HitTest(new Point(70, 70)) as Tile, "a child without area is not hit");
    }

    [TestMethod]
    public void AChildHeldOutOfView_KeepsItsBounds_ButIsNeitherDrawnNorPlannedNorHit()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost();
        Lay(host);
        host.Second.IsHeldOutOfView = true;
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);
        var plan = Plan(host);

        Assert.AreEqual(new Rect(40, 40, 60, 60), host.Second.Bounds, "holding a child out of view leaves its layout alone");
        Assert.AreEqual(_first, ReadPixel(pixels, 50, 50), "the held child is not drawn");
        Assert.AreEqual(1, plan.Entries.Count(entry => entry.Kind == CompositionEntryKind.Child));
        Assert.IsNull(host.HitTest(new Point(70, 70)) as Tile, "the held child is not hit");

        host.Second.IsHeldOutOfView = false;

        Assert.AreSame(host.Second, host.HitTest(new Point(70, 70)), "released, the child is hit again without a new layout");
        Assert.AreEqual(2, Plan(host).Entries.Count(entry => entry.Kind == CompositionEntryKind.Child));
    }

    [TestMethod]
    public void AChildClip_CutsTheChild_AndBecomesAScope()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost { ClipsSecondTo = new Rect(40, 40, 20, 20) };
        Lay(host);
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);
        var plan = Plan(host);

        Assert.AreEqual(_second, ReadPixel(pixels, 50, 50), "inside the clip the second child shows");
        Assert.AreEqual(Color.White, ReadPixel(pixels, 70, 70), "outside the clip the second child is cut away");
        Assert.AreEqual(_first, ReadPixel(pixels, 45, 30), "the first child is not clipped");
        var scope = plan.Entries.Single(entry => entry.Kind == CompositionEntryKind.ClipRect);
        Assert.AreEqual(new Rect(40, 40, 20, 20), scope.Rect);
        Assert.AreEqual(1, scope.ScopeLength, "the clip encloses the second child only");
    }

    [TestMethod]
    public void AChildTransform_MovesTheChildren_AndBecomesAScope()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost { Shift = new Vector2(30, 0) };
        Lay(host);
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);
        var plan = Plan(host);

        Assert.AreEqual(Color.White, ReadPixel(pixels, 10, 10), "the first child moved away from its layout box");
        Assert.AreEqual(_first, ReadPixel(pixels, 40, 10));
        Assert.AreEqual(CompositionEntryKind.Transform, plan.Entries[1].Kind);
        Assert.AreEqual(2, plan.Entries[1].ScopeLength, "the transform encloses both children");
    }

    [TestMethod]
    public void AChildAttachedElsewhere_IsNotDrawnByTheVisitingHost()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var host = new TwoChildHost();
        var other = new TwoChildHost();
        Lay(host);
        other.Adopt(host.Second);
        using var factory = new GdiGraphicsFactory();

        var pixels = RenderSurface(factory, host.Render);
        var plan = Plan(host);

        Assert.AreEqual(_first, ReadPixel(pixels, 50, 50), "the child that moved to another host is not drawn here");
        Assert.AreEqual(1, plan.Entries.Count(entry => entry.Kind == CompositionEntryKind.Child));
    }

    private static void Lay(TwoChildHost host)
    {
        host.Measure(new Size(SURFACE_SIZE, SURFACE_SIZE));
        host.Arrange(new Rect(0, 0, SURFACE_SIZE, SURFACE_SIZE));
    }

    private static CompositionPlan Plan(UIElement element)
    {
        var builder = new CompositionPlanBuilder();
        element.WritePlan(builder);
        return builder.Build();
    }

    private static byte[] RenderSurface(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_SIZE, SURFACE_SIZE, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var copy = new byte[SURFACE_SIZE * SURFACE_SIZE * 4];
        for (int row = 0; row < SURFACE_SIZE; row++)
        {
            pixels.Slice(row * cpu.StrideBytes, SURFACE_SIZE * 4).CopyTo(copy.AsSpan(row * SURFACE_SIZE * 4));
        }

        return copy;
    }

    // The offscreen surface stores BGRA.
    private static Color ReadPixel(byte[] pixels, int x, int y)
    {
        int offset = (y * SURFACE_SIZE + x) * 4;
        return Color.FromArgb(255, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    /// <summary>Two tiles: the first fills the top-left 60x60, the second the 40..100 square, overlapping in the middle.</summary>
    private sealed class TwoChildHost : FrameworkElement, IVisualTreeHost
    {
        internal TwoChildHost()
        {
            First = new Tile { Fill = _first };
            Second = new Tile { Fill = _second };
            AttachChild(First);
            AttachChild(Second);
        }

        internal Tile First { get; }

        internal Tile Second { get; }

        internal bool DrawsOverlay { get; init; }

        internal bool HidesSecond { get; init; }

        internal Rect? ClipsSecondTo { get; init; }

        internal Vector2 Shift { get; init; }

        // Takes a tile over from another host, as a popup borrows a tool bar entry.
        internal void Adopt(Tile tile) => tile.Parent = this;

        protected override Size MeasureContent(Size availableSize)
        {
            First.Measure(availableSize);
            Second.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeContent(Rect bounds)
        {
            First.Arrange(new Rect(0, 0, 60, 60));
            Second.Arrange(HidesSecond ? Rect.Empty : new Rect(40, 40, 60, 60));
        }

        protected override void OnRenderOverlay(IGraphicsContext context)
        {
            if (DrawsOverlay)
            {
                context.FillRectangle(new Rect(45, 45, 10, 10), _overlay);
            }
        }

        protected override ChildClip? GetChildClip(UIElement child)
            => ReferenceEquals(child, Second) && ClipsSecondTo is Rect clip ? new ChildClip(clip) : null;

        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        {
            transform = Matrix3x2.CreateTranslation(Shift);
            return Shift != Vector2.Zero;
        }

        bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(First) && visitor(Second);
    }

    private sealed class Tile : FrameworkElement
    {
        internal Color Fill { get; init; }

        protected override Size MeasureContent(Size availableSize) => new(60, 60);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }
}
