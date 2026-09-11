using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class RunBaselineOffsetTests
{
    [TestMethod]
    public void DefaultsToZeroAndResolvesIntoTheStyle()
    {
        var run = new Run("x");
        Assert.AreEqual(0, run.BaselineOffset);
        var owner = new TextRunStyle("Segoe UI", 16);
        Assert.AreEqual(owner, run.ResolveStyle(owner));

        run.BaselineOffset = 4.5;
        Assert.AreEqual(4.5, run.ResolveStyle(owner).BaselineOffset);
        Assert.AreEqual(owner.FontSize, run.ResolveStyle(owner).FontSize, "the offset changed the font size");
    }

    [TestMethod]
    public void FluentSetterWritesTheProperty()
    {
        var run = new Run("x").BaselineOffset(-2);
        Assert.AreEqual(-2, run.BaselineOffset);
    }

    [TestMethod]
    public void ChangeRaisesALayoutNotification()
    {
        var run = new Run("x");
        var changes = new List<RunChange>();
        run.Changed += (_, change) => changes.Add(change);

        run.BaselineOffset = 3;
        run.BaselineOffset = 3;
        run.BaselineOffset = 0;

        CollectionAssert.AreEqual(new[] { RunChange.Layout, RunChange.Layout }, changes);
    }

    [TestMethod]
    public void BindingDrivesTheOffset()
    {
        var source = new ObservableValue<double>(0);
        var run = new Run("x");
        run.Bind(Run.BaselineOffsetProperty, source);
        Assert.AreEqual(0, run.BaselineOffset);

        source.Value = 6;
        Assert.AreEqual(6, run.BaselineOffset);
    }

    [TestMethod]
    public void TextBlockPassesTheOffsetToTheEngine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var block = new TextBlock { FontFamily = "Segoe UI", FontSize = 16 };
            block.Inlines.Add(new Run("x"));
            block.Inlines.Add(new Run("2") { BaselineOffset = 6 });
            using var window = HeadlessWindow.Create(400, 200);
            window.Content = block;
            window.PerformLayout();

            var plain = new TextBlock { FontFamily = "Segoe UI", FontSize = 16, Text = "x2" };
            using var reference = HeadlessWindow.Create(400, 200);
            reference.Content = plain;
            reference.PerformLayout();

            Assert.AreEqual(plain.DesiredSize.Height + 6, block.DesiredSize.Height, 0.51,
                "a run raised by 6 DIPs should add 6 DIPs above the line");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
