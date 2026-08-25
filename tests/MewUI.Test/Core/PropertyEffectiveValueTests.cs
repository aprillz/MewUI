using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

[TestClass]
public sealed class PropertyEffectiveValueTests
{
    private static readonly Color INHERITED = Color.FromRgb(240, 240, 240);
    private static readonly Color STYLE = Color.FromRgb(80, 80, 80);
    private static readonly Color LOCAL = Color.FromRgb(20, 120, 20);
    private static readonly Color ANIMATED = Color.FromRgb(20, 20, 180);

    [TestMethod]
    public void Clear_RevealsEveryDurableSourceInPriorityOrder()
    {
        var owner = new ValueOwner();
        owner.PropertyStore.SetStyle(ValueOwner.ValueProperty, 10);
        owner.PropertyStore.SetValue(ValueOwner.ValueProperty, 20, ValueSource.Binding);
        owner.PropertyStore.SetElementTrigger(ValueOwner.ValueProperty, 30);
        owner.PropertyStore.SetLocal(ValueOwner.ValueProperty, 40);

        Assert.AreEqual(40, owner.Value);

        var local = owner.PropertyStore.ClearSource(ValueOwner.ValueProperty.Id, ValueSource.Local);
        AssertMutation(local, 40, 30, ValueSource.Local, ValueSource.ElementTrigger);

        var trigger = owner.PropertyStore.ClearSource(ValueOwner.ValueProperty.Id, ValueSource.ElementTrigger);
        AssertMutation(trigger, 30, 20, ValueSource.ElementTrigger, ValueSource.Binding);

        var binding = owner.PropertyStore.ClearSource(ValueOwner.ValueProperty.Id, ValueSource.Binding);
        AssertMutation(binding, 20, 10, ValueSource.Binding, ValueSource.Style);

        var style = owner.PropertyStore.ClearSource(ValueOwner.ValueProperty.Id, ValueSource.Style);
        AssertMutation(style, 10, 0, ValueSource.Style, ValueSource.Default);
        Assert.AreEqual(0, owner.Value);
    }

    [TestMethod]
    public void ClearStyle_ReResolvesInheritedValue_WhenNoCacheExists()
    {
        var child = new TrackingControl();
        child.PropertyStore.SetStyle(TextElement.ForegroundProperty, STYLE);
        var parent = new Border { Foreground = INHERITED, Child = child };
        child.ResetChangeCount();

        var result = child.PropertyStore.ClearSource(
            TextElement.ForegroundProperty.Id,
            ValueSource.Style);

        Assert.AreEqual(STYLE, result.OldValue);
        Assert.AreEqual(INHERITED, result.NewValue);
        Assert.AreEqual(ValueSource.Style, result.OldSource);
        Assert.AreEqual(ValueSource.Inherited, result.NewSource);
        Assert.AreEqual(INHERITED, child.Foreground);
        Assert.AreEqual(1, child.ChangeCount);
        GC.KeepAlive(parent);
    }

    [TestMethod]
    public void ClearStyle_RevealsMetadataDefault_WhenDetached()
    {
        var child = new TrackingControl();
        child.PropertyStore.SetStyle(TextElement.ForegroundProperty, STYLE);
        child.ResetChangeCount();

        var result = child.PropertyStore.ClearSource(
            TextElement.ForegroundProperty.Id,
            ValueSource.Style);

        Assert.AreEqual(Color.Black, result.NewValue);
        Assert.AreEqual(ValueSource.Default, result.NewSource);
        Assert.AreEqual(Color.Black, child.Foreground);
        Assert.AreEqual(1, child.ChangeCount);
    }

    [TestMethod]
    public void ShadowedSourceMutation_DoesNotNotify()
    {
        var owner = new TrackingControl { Foreground = LOCAL };
        owner.ResetChangeCount();

        var set = owner.PropertyStore.SetValue(
            TextElement.ForegroundProperty,
            STYLE,
            ValueSource.Style);
        var clear = owner.PropertyStore.ClearSource(
            TextElement.ForegroundProperty.Id,
            ValueSource.Style);

        Assert.IsFalse(set.IsEffectiveChange);
        Assert.IsFalse(clear.IsEffectiveChange);
        Assert.AreEqual(LOCAL, owner.Foreground);
        Assert.AreEqual(0, owner.ChangeCount);
    }

    [TestMethod]
    public void ShadowedSourceMutation_PreservesAnimationOverlay()
    {
        var owner = new TrackingControl { Foreground = LOCAL };
        owner.PropertyStore.SetAnimatedValue(TextElement.ForegroundProperty.Id, ANIMATED);
        owner.ResetChangeCount();

        owner.PropertyStore.SetValue(
            TextElement.ForegroundProperty,
            STYLE,
            ValueSource.Style);
        Assert.AreEqual(
            ANIMATED,
            owner.PropertyStore.GetCurrentVisualValue(TextElement.ForegroundProperty.Id));

        owner.PropertyStore.ClearSource(TextElement.ForegroundProperty.Id, ValueSource.Style);
        Assert.AreEqual(
            ANIMATED,
            owner.PropertyStore.GetCurrentVisualValue(TextElement.ForegroundProperty.Id));
        Assert.AreEqual(0, owner.ChangeCount);
    }

    [TestMethod]
    public void CoerceValue_ReevaluatesThePreservedRawCandidate()
    {
        var owner = new CoerceOwner { Limit = 5 };
        owner.Value = 10;
        Assert.AreEqual(5, owner.Value);

        owner.Limit = 20;
        owner.RecoerceValue();

        Assert.AreEqual(10, owner.Value);
    }

    [TestMethod]
    public void ShadowedRawCandidate_IsCoercedWhenRevealed()
    {
        var owner = new CoerceOwner { Limit = 5 };
        owner.PropertyStore.SetStyle(CoerceOwner.ValueProperty, 10);
        owner.Value = 2;
        Assert.AreEqual(2, owner.Value);

        owner.Limit = 20;
        owner.PropertyStore.ClearLocalValue(CoerceOwner.ValueProperty);

        Assert.AreEqual(10, owner.Value);
        Assert.AreEqual(ValueSource.Style, owner.PropertyStore.GetSource(CoerceOwner.ValueProperty.Id));
    }

    [TestMethod]
    public void AnimationCandidate_IsCoercedWithoutReplacingTheRawBase()
    {
        var owner = new CoerceOwner { Limit = 5, Value = 3 };

        owner.PropertyStore.SetAnimatedValue(CoerceOwner.ValueProperty.Id, 10);
        Assert.AreEqual(5, owner.Value);

        owner.PropertyStore.ClearAnimatedValue(CoerceOwner.ValueProperty.Id);

        Assert.AreEqual(3, owner.Value);
    }

    private static void AssertMutation(
        ValueMutationResult result,
        int oldValue,
        int newValue,
        ValueSource oldSource,
        ValueSource newSource)
    {
        Assert.AreEqual(oldValue, result.OldValue);
        Assert.AreEqual(newValue, result.NewValue);
        Assert.AreEqual(oldSource, result.OldSource);
        Assert.AreEqual(newSource, result.NewSource);
        Assert.IsTrue(result.IsEffectiveChange);
    }

    private sealed class ValueOwner : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<ValueOwner>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class TrackingControl : Control
    {
        public int ChangeCount { get; private set; }

        public void ResetChangeCount() => ChangeCount = 0;

        protected override void OnMewPropertyChanged(MewProperty property)
        {
            base.OnMewPropertyChanged(property);
            if (property == TextElement.ForegroundProperty)
            {
                ChangeCount++;
            }
        }
    }

    private sealed class CoerceOwner : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<CoerceOwner>(
                nameof(Value),
                0,
                coerce: static (owner, value) => Math.Min(value, owner.Limit));

        public int Limit { get; set; }

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public void RecoerceValue() => CoerceValue(ValueProperty);
    }
}
