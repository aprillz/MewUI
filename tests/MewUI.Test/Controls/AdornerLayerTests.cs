using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// An adorner is a layer over the element it adorns, not a sheet across it: it takes the pointer only
/// where it draws, and what it carries resolves against that element the way popup content resolves
/// against its owner.
[TestClass]
[DoNotParallelize]
public sealed class AdornerLayerTests
{
    private const int WIDTH = 400;
    private const int HEIGHT = 200;
    private static readonly Color _badgeColor = Color.FromRgb(200, 30, 40);

    [TestMethod]
    public void TheSpaceAroundAnAdornerFallsThroughToTheContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 200);
        var content = new Button().Content("under");
        window.Content = content;
        window.PerformLayout();

        var badge = new Border { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var adorner = new Adorner(content, badge);
        AdornerLayer.GetAdornerLayer(content)!.Add(adorner);
        window.PerformLayout();

        var onBadge = new Point(badge.Bounds.X + badge.Bounds.Width / 2, badge.Bounds.Y + badge.Bounds.Height / 2);
        Assert.AreSame(badge, window.HitTest(onBadge), "the adorner's own content should take the pointer");

        var besideBadge = new Point(badge.Bounds.X - 40, badge.Bounds.Bottom + 40);
        var hit = window.HitTest(besideBadge);

        Assert.IsNotNull(hit, "the pointer landed on nothing where the content should have been");
        Assert.AreNotSame(adorner, hit, "the adorner answered for space it does not draw in");
    }

    [TestMethod]
    public void AThemeChangeReachesEverythingAnAdornerCarries()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 200);
        var content = new Border();
        window.Content = content;
        window.PerformLayout();

        int themedDepth = 0;
        var deep = new Border();
        deep.WithTheme((_, _) => themedDepth++);
        var badge = new Border { Child = deep };
        AdornerLayer.GetAdornerLayer(content)!.Add(new Adorner(content, badge));
        window.PerformLayout();

        int before = themedDepth;
        var theme = window.ThemeInternal;
        window.BroadcastThemeChanged(theme, theme);

        Assert.IsGreaterThan(before, themedDepth,
            "the theme change stopped at the adorner and never reached what it carries");
    }

    [TestMethod]
    public void WhatAnAdornerCarriesInheritsFromTheElementItAdorns()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var ink = Color.FromRgb(10, 120, 200);
        var window = HeadlessWindow.Create(400, 200);
        var content = new Border { Foreground = ink };
        window.Content = content;
        window.PerformLayout();

        var label = new TextBlock { Text = "badge" };
        AdornerLayer.GetAdornerLayer(content)!.Add(new Adorner(content, new Border { Child = label }));
        window.PerformLayout();

        Assert.AreEqual(ink, label.Foreground,
            "an inherited value stopped at the window instead of coming from the adorned element");
    }

    [TestMethod]
    public void AnAdornerIsNotShownWhileItsElementIsOutOfTheWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var factory = Application.DefaultGraphicsFactory;
        var target = new Border { Width = 200, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var holder = new ContentControl { Content = target };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = holder;
        window.PerformLayout();
        var badge = new Border { Background = _badgeColor, Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AdornerLayer.GetAdornerLayer(target)!.Add(new Adorner(target, badge));
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        var onBadge = RenderAndFindBadge(window, surface, badge);

        // As a tab switch does: the element leaves the tree and its adorner stays registered.
        holder.Content = null;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.AreNotEqual(_badgeColor, PixelAt(surface, onBadge), "the adorner is still drawn after its element left the window");
        Assert.AreNotSame(badge, window.HitTest(onBadge), "the adorner still takes the pointer after its element left the window");

        holder.Content = target;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.AreEqual(_badgeColor, PixelAt(surface, onBadge), "the adorner did not come back with its element");
        Assert.AreSame(badge, window.HitTest(onBadge));
    }

    [TestMethod]
    public void AnAdornerIsNotShownWhileAnAncestorOfItsElementIsHidden()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var factory = Application.DefaultGraphicsFactory;
        var target = new Border { Width = 200, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var holder = new ContentControl { Content = target };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = holder;
        window.PerformLayout();
        var badge = new Border { Background = _badgeColor, Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AdornerLayer.GetAdornerLayer(target)!.Add(new Adorner(target, badge));
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        var onBadge = RenderAndFindBadge(window, surface, badge);

        holder.IsVisible = false;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.AreNotEqual(_badgeColor, PixelAt(surface, onBadge), "the adorner is still drawn while an ancestor of its element is hidden");
        Assert.AreNotSame(badge, window.HitTest(onBadge), "the adorner still takes the pointer while an ancestor of its element is hidden");

        holder.IsVisible = true;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.AreEqual(_badgeColor, PixelAt(surface, onBadge), "the adorner did not come back when the ancestor was shown");
    }

    private static Point RenderAndFindBadge(Window window, IRenderSurface surface, Border badge)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        var onBadge = new Point(badge.Bounds.X + badge.Bounds.Width / 2, badge.Bounds.Y + badge.Bounds.Height / 2);
        Assert.AreEqual(_badgeColor, PixelAt(surface, onBadge), "the adorner was not drawn to begin with");
        Assert.AreSame(badge, window.HitTest(onBadge));
        return onBadge;
    }

    private static Color PixelAt(IRenderSurface surface, Point point)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int offset = ((int)point.Y * WIDTH + (int)point.X) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}
