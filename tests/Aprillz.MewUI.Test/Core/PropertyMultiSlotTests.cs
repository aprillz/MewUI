using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

/// <summary>
/// Characterizes the multi-slot store's defining behavior (subplan 06-B): a cleared source reveals
/// the preserved lower-priority slot instead of dropping to the default, so the control layer no
/// longer has to re-derive it. The single-slot store lost the shadowed value on overwrite; these
/// tests would fail against it.
/// </summary>
[TestClass]
public sealed class PropertyMultiSlotTests
{
    private sealed class SlotOwner : Control
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<SlotOwner>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class NullableOwner : MewObject
    {
        public static readonly MewProperty<string?> ValueProperty =
            MewProperty<string?>.Register<NullableOwner>(nameof(Value), null);

        public int ChangeCount { get; private set; }

        public string? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        protected override void OnMewPropertyChanged(MewProperty property)
        {
            if (property == ValueProperty)
            {
                ChangeCount++;
            }
        }
    }

    private static readonly MewProperty<int> Prop = SlotOwner.ValueProperty;

    [TestMethod]
    public void ClearTrigger_RevealsPreservedStyle()
    {
        var owner = new SlotOwner();
        owner.PropertyStore.SetStyle(Prop, 10);
        owner.PropertyStore.SetTrigger(Prop, 20);
        Assert.AreEqual(20, owner.Value);

        owner.PropertyStore.ClearSource(Prop.Id, ValueSource.Trigger);

        Assert.AreEqual(10, owner.Value, "clearing the trigger must reveal the preserved style value, not the default");
    }

    [TestMethod]
    public void ClearTrigger_WithNoStyle_FallsBackToDefault()
    {
        var owner = new SlotOwner();
        owner.PropertyStore.SetTrigger(Prop, 20);

        owner.PropertyStore.ClearSource(Prop.Id, ValueSource.Trigger);

        Assert.AreEqual(0, owner.Value);
    }

    [TestMethod]
    public void ClearLocal_RevealsTriggerThenStyle()
    {
        var owner = new SlotOwner();
        owner.PropertyStore.SetStyle(Prop, 10);
        owner.PropertyStore.SetTrigger(Prop, 20);
        owner.PropertyStore.SetLocal(Prop, 30);
        Assert.AreEqual(30, owner.Value);

        owner.PropertyStore.ClearLocal(Prop);
        Assert.AreEqual(20, owner.Value, "clearing local reveals the trigger");

        owner.PropertyStore.ClearSource(Prop.Id, ValueSource.Trigger);
        Assert.AreEqual(10, owner.Value, "clearing the trigger then reveals the style");
    }

    [TestMethod]
    public void OverwriteSameSource_DoesNotStrandLowerSlot()
    {
        var owner = new SlotOwner();
        owner.PropertyStore.SetStyle(Prop, 10);
        owner.PropertyStore.SetTrigger(Prop, 20);
        owner.PropertyStore.SetTrigger(Prop, 21); // update the trigger while style is shadowed
        Assert.AreEqual(21, owner.Value);

        owner.PropertyStore.ClearSource(Prop.Id, ValueSource.Trigger);
        Assert.AreEqual(10, owner.Value, "updating the trigger must not lose the shadowed style");
    }

    [TestMethod]
    public void LocalNull_HidesStyle_AndClearRevealsIt()
    {
        var owner = new SlotOwner();
        var strProp = MewProperty<string?>.Register<SlotOwner>("Str", "default");

        owner.PropertyStore.SetStyle(strProp, "styled");
        Assert.AreEqual("styled", owner.PropertyStore.GetValue(strProp));

        // An explicit local null must win at the Local tier and hide the style value.
        owner.PropertyStore.SetLocal(strProp, (string?)null);
        Assert.IsNull(owner.PropertyStore.GetValue(strProp));

        // Clearing the local null reveals the preserved style value.
        owner.PropertyStore.ClearLocal(strProp);
        Assert.AreEqual("styled", owner.PropertyStore.GetValue(strProp));
    }

    [TestMethod]
    public void NonNullToNull_RaisesPropertyChanged()
    {
        var owner = new NullableOwner { Value = "set" };

        owner.Value = null;

        Assert.AreEqual(2, owner.ChangeCount);
    }
}
