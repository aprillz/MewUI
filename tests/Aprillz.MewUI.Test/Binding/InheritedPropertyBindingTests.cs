using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class InheritedPropertyBindingTests
{
    // A property-to-property binding sourced on an inherited property must update when the inherited
    // value changes (parent change / theme / disabled trigger). Before the fix, inherited changes
    // cleared the cache + invalidated render but never notified bindings.
    [TestMethod]
    public void BindingToInheritedForeground_UpdatesWhenParentChanges()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;

        var colorA = Color.FromRgb(10, 20, 30);
        var colorB = Color.FromRgb(40, 50, 60);

        parent.Foreground = colorA;
        _ = child.Foreground; // force inherited resolution + cache

        // Bind the child's BorderBrush to its own (inherited) Foreground.
        child.Bind(Control.BorderBrushProperty, child, Control.ForegroundProperty, (Color c) => c);
        Assert.AreEqual(colorA, child.BorderBrush, "initial sync");

        parent.Foreground = colorB;
        Assert.AreEqual(colorB, child.BorderBrush, "binding follows inherited change");
    }

    // The binding must work even when the descendant hasn't resolved/cached the inherited value yet.
    [TestMethod]
    public void BindingToInheritedForeground_UpdatesAcrossIntermediateHost()
    {
        var root = new Border();
        var middle = new Border();
        var leaf = new Border();
        root.Child = middle;
        middle.Child = leaf;

        var colorA = Color.FromRgb(1, 2, 3);
        var colorB = Color.FromRgb(7, 8, 9);

        root.Foreground = colorA;
        _ = leaf.Foreground; // force inherited resolution through the intermediate host
        leaf.Bind(Control.BorderBrushProperty, leaf, Control.ForegroundProperty, (Color c) => c);
        Assert.AreEqual(colorA, leaf.BorderBrush);

        root.Foreground = colorB;
        Assert.AreEqual(colorB, leaf.BorderBrush);
    }

    // Regression: descendants without observers keep working via lazy render-time resolution.
    [TestMethod]
    public void InheritedForeground_ResolvesWithoutObservers()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;

        parent.Foreground = Color.FromRgb(11, 22, 33);
        Assert.AreEqual(Color.FromRgb(11, 22, 33), child.Foreground);

        parent.Foreground = Color.FromRgb(44, 55, 66);
        Assert.AreEqual(Color.FromRgb(44, 55, 66), child.Foreground);
    }

    // A child that overrides Foreground locally must not be affected by the parent's inherited change.
    [TestMethod]
    public void LocalOverride_StopsInheritedBindingPropagation()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;

        var local = Color.FromRgb(100, 100, 100);
        child.Foreground = local;
        child.Bind(Control.BorderBrushProperty, child, Control.ForegroundProperty, (Color c) => c);
        Assert.AreEqual(local, child.BorderBrush);

        parent.Foreground = Color.FromRgb(200, 0, 0);
        Assert.AreEqual(local, child.BorderBrush, "local override is unaffected by parent change");
    }
}
