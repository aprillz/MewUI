using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Binding;

/// <summary>
/// A binding sourced from an inherited property captures whatever the source resolves to when the
/// binding is created, which for a detached element is the registered default. Attaching gives the
/// subtree a new inherited context, so the binding must be re-evaluated then: nothing re-reads a
/// property whose value is pushed rather than pulled.
/// </summary>
[TestClass]
public sealed class InheritedBindingAttachTests
{
    private static readonly Color FIRST = Color.FromRgb(10, 20, 30);
    private static readonly Color SECOND = Color.FromRgb(200, 210, 220);
    private static readonly Color LOCAL = Color.FromRgb(70, 80, 90);

    private static PathShape BoundIcon()
    {
        var icon = new PathShape { Data = PathGeometry.Parse("M 0 0 L 8 0 L 8 8 Z") };
        icon.Bind(Shape.FillProperty, icon, TextElement.ForegroundProperty,
            (Color color) => (Brush)new SolidColorBrush(color));
        return icon;
    }

    private static Color FillColor(PathShape icon)
        => icon.Fill is SolidColorBrush solid ? solid.Color : default;

    [TestMethod]
    public void Binding_TakesInheritedValue_OnAttach()
    {
        var icon = BoundIcon();
        var host = new Border { Foreground = FIRST };

        host.Child = icon;

        Assert.AreEqual(FIRST, FillColor(icon));
    }

    [TestMethod]
    public void Binding_PicksUpAncestorChange_MadeWhileDetached()
    {
        var icon = BoundIcon();
        var host = new Border { Foreground = FIRST };
        host.Child = icon;

        host.Child = null;
        host.Foreground = SECOND;
        host.Child = icon;

        Assert.AreEqual(SECOND, FillColor(icon));
    }

    [TestMethod]
    public void Binding_FollowsAncestorChange_WhileAttached()
    {
        var icon = BoundIcon();
        var host = new Border { Foreground = FIRST };
        host.Child = icon;

        host.Foreground = SECOND;

        Assert.AreEqual(SECOND, FillColor(icon));
    }

    [TestMethod]
    public void DirectBinding_PreservesSourceLocalValue_OnAttach()
    {
        var source = new Border { Foreground = LOCAL };
        var targets = BindAll(source);
        var host = new Border { Foreground = FIRST };

        host.Child = source;

        AssertTargets(LOCAL, targets);
    }

    [TestMethod]
    public void Binding_TakesDefaultValue_OnDetach()
    {
        var source = new Border();
        var targets = BindAll(source);
        var host = new Border { Foreground = FIRST };
        host.Child = source;
        AssertTargets(FIRST, targets);

        host.Child = null;

        AssertTargets(Color.Black, targets);
    }

    [TestMethod]
    public void Binding_FollowsContextParentOverride_SetReplaceAndClear()
    {
        var source = new Border();
        var slot = new Border { Child = source };
        var visualParent = new Border { Foreground = FIRST, Child = slot };
        var firstOwner = new Border { Foreground = SECOND };
        var secondOwner = new Border { Foreground = LOCAL };
        var targets = BindAll(source);

        AssertTargets(FIRST, targets);

        slot.ContextParentOverride = firstOwner;
        AssertTargets(SECOND, targets);

        slot.ContextParentOverride = secondOwner;
        AssertTargets(LOCAL, targets);

        slot.ContextParentOverride = null;
        AssertTargets(FIRST, targets);

        GC.KeepAlive(visualParent);
    }

    [TestMethod]
    public void EqualContextValue_DoesNotRepushBindingTarget()
    {
        var source = new Border();
        var slot = new Border { Child = source };
        var visualParent = new Border { Foreground = FIRST, Child = slot };
        var owner = new Border { Foreground = FIRST };
        var target = new ColorTarget();
        target.SetBinding(ColorTarget.ValueProperty, source, TextElement.ForegroundProperty);
        int changeCount = target.ChangeCount;

        slot.ContextParentOverride = owner;

        Assert.AreEqual(FIRST, target.Value);
        Assert.AreEqual(changeCount, target.ChangeCount);
        GC.KeepAlive(visualParent);
    }

    private static BoundTargets BindAll(Border source)
    {
        var direct = new ColorTarget();
        direct.SetBinding(ColorTarget.ValueProperty, source, TextElement.ForegroundProperty);

        var converted = new UIntTarget();
        converted.SetBinding(
            UIntTarget.ValueProperty,
            source,
            TextElement.ForegroundProperty,
            static color => color.ToArgb());

        var path = BindingPath.From<Border>().Then(TextElement.ForegroundProperty);
        var pathTarget = new ColorTarget();
        pathTarget.SetBinding(ColorTarget.ValueProperty, source, path);

        return new BoundTargets(direct, converted, pathTarget);
    }

    private static void AssertTargets(Color expected, BoundTargets targets)
    {
        Assert.AreEqual(expected, targets.Direct.Value, "direct MewProperty binding");
        Assert.AreEqual(expected.ToArgb(), targets.Converted.Value, "converted MewProperty binding");
        Assert.AreEqual(expected, targets.Path.Value, "BindingPath");
    }

    private sealed record BoundTargets(ColorTarget Direct, UIntTarget Converted, ColorTarget Path);

    private sealed class ColorTarget : MewObject
    {
        public static readonly MewProperty<Color> ValueProperty =
            MewProperty<Color>.Register<ColorTarget>(nameof(Value), Color.Black);

        public int ChangeCount { get; private set; }

        public Color Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        protected override void OnMewPropertyChanged(MewProperty property)
        {
            base.OnMewPropertyChanged(property);
            if (property == ValueProperty)
            {
                ChangeCount++;
            }
        }
    }

    private sealed class UIntTarget : MewObject
    {
        public static readonly MewProperty<uint> ValueProperty =
            MewProperty<uint>.Register<UIntTarget>(nameof(Value), Color.Black.ToArgb());

        public uint Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
