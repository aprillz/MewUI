using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// An adorner is a layer over the element it adorns, not a sheet across it: it takes the pointer only
/// where it draws, and what it carries resolves against that element the way popup content resolves
/// against its owner.
[TestClass]
[DoNotParallelize]
public sealed class AdornerLayerTests
{
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
}
