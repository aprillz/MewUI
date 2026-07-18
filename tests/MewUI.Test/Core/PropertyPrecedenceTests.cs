using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

/// <summary>
/// Characterizes the observable property-source precedence (Local &gt; Trigger &gt; Style &gt; Default)
/// that the value store must preserve. These are invariants: they lock the resolution order that any
/// future multi-slot store rewrite has to keep. The separate clear-reveals-lower behavior (which the
/// single-slot store does not do today - a cleared source drops to Default and the control layer
/// re-applies) is intentionally NOT locked here, because that store-level semantic is what a
/// multi-slot migration changes.
/// </summary>
[TestClass]
public sealed class PropertyPrecedenceTests
{
    private sealed class PrecedenceOwner : Control
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<PrecedenceOwner>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private static readonly MewProperty<int> Prop = PrecedenceOwner.ValueProperty;

    [TestMethod]
    public void Default_WhenNothingSet()
    {
        var owner = new PrecedenceOwner();
        Assert.AreEqual(0, owner.Value);
    }

    [TestMethod]
    public void Style_WinsOverDefault()
    {
        var owner = new PrecedenceOwner();
        owner.PropertyStore.SetStyle(Prop, 10);
        Assert.AreEqual(10, owner.Value);
    }

    [TestMethod]
    public void Trigger_WinsOverStyle()
    {
        var owner = new PrecedenceOwner();
        owner.PropertyStore.SetStyle(Prop, 10);
        owner.PropertyStore.SetTrigger(Prop, 20);
        Assert.AreEqual(20, owner.Value);
    }

    [TestMethod]
    public void Local_WinsOverTriggerAndStyle()
    {
        var owner = new PrecedenceOwner();
        owner.PropertyStore.SetStyle(Prop, 10);
        owner.PropertyStore.SetTrigger(Prop, 20);
        owner.PropertyStore.SetLocal(Prop, 30);
        Assert.AreEqual(30, owner.Value);
    }

    [TestMethod]
    public void LowerSource_DoesNotOverwriteHigher()
    {
        var owner = new PrecedenceOwner();
        owner.PropertyStore.SetLocal(Prop, 30);

        // A style/trigger arriving after a local value must not lower the effective value.
        owner.PropertyStore.SetStyle(Prop, 10);
        owner.PropertyStore.SetTrigger(Prop, 20);

        Assert.AreEqual(30, owner.Value);
    }

    [TestMethod]
    public void ResolutionOrder_IsIndependentOfSetOrder()
    {
        // Whatever order the sources are written, the highest-priority one wins.
        var a = new PrecedenceOwner();
        a.PropertyStore.SetLocal(Prop, 30);
        a.PropertyStore.SetStyle(Prop, 10);
        a.PropertyStore.SetTrigger(Prop, 20);

        var b = new PrecedenceOwner();
        b.PropertyStore.SetTrigger(Prop, 20);
        b.PropertyStore.SetStyle(Prop, 10);
        b.PropertyStore.SetLocal(Prop, 30);

        Assert.AreEqual(a.Value, b.Value);
        Assert.AreEqual(30, a.Value);
    }
}
