using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class BindingValueSourceCharacterizationTests
{
    [TestMethod]
    public void BindingModes_MapToIndependentCapabilities()
    {
        Assert.AreEqual(
            new BindingCapabilities(true, true, false),
            BindingCapabilities.FromMode(BindingMode.OneWay));
        Assert.AreEqual(
            new BindingCapabilities(true, true, true),
            BindingCapabilities.FromMode(BindingMode.TwoWay));
    }

    [TestMethod]
    public void RegisteredBindingAndBindingValueSlot_AreIndependent()
    {
        var source = new ObservableValue<int>(0);
        var target = new Target();

        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        Assert.IsTrue(target.HasPropertyBinding(Target.ValueProperty.Id));
        Assert.IsFalse(target.HasBindingTargetValue(Target.ValueProperty.Id));

        source.Value = 1;

        Assert.IsTrue(target.HasBindingTargetValue(Target.ValueProperty.Id));
        Assert.AreEqual(ValueSource.Binding, target.PropertyStore.GetSource(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void BindingPushAndDirectWrite_UseDifferentSources()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        Assert.AreEqual(ValueSource.Binding, target.PropertyStore.GetSource(Target.ValueProperty.Id));

        target.Value = 2;

        Assert.AreEqual(ValueSource.Local, target.PropertyStore.GetSource(Target.ValueProperty.Id));
        Assert.IsFalse(target.HasPropertyBinding(Target.ValueProperty.Id));
        Assert.IsFalse(target.HasBindingTargetValue(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void ClearLocalValue_RemovesOnlyLocalAndPreservesBinding()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        target.PropertyStore.SetLocal(Target.ValueProperty, 2);
        Assert.AreEqual(2, target.Value);

        source.Value = 3;
        Assert.AreEqual(2, target.Value, "Local remains the effective source");

        target.ClearLocalValue(Target.ValueProperty);
        Assert.AreEqual(3, target.Value, "clearing Local reveals the latest Binding candidate");
        Assert.IsTrue(target.HasPropertyBinding(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void DirectWriteThenClearLocalValue_DoesNotRestoreBinding()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        target.Value = 2;
        source.Value = 3;
        target.ClearLocalValue(Target.ValueProperty);

        Assert.AreEqual(0, target.Value);
        Assert.IsFalse(target.HasPropertyBinding(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void ObservableBinding_ClearBindingRemovesItsValueSlot()
    {
        var source = new ObservableValue<int>(4);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        target.ClearBinding(Target.ValueProperty);
        source.Value = 5;

        Assert.AreEqual(0, target.Value);
    }

    [TestMethod]
    public void ObservableBinding_ReplacementDetachesThePreviousSource()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(2);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, first, BindingMode.OneWay);

        target.SetBinding(Target.ValueProperty, second, BindingMode.OneWay);
        first.Value = 3;
        Assert.AreEqual(2, target.Value);

        second.Value = 4;
        Assert.AreEqual(4, target.Value);
    }

    [TestMethod]
    public void SetCurrentValue_WithBindingUpdatesTargetWithoutWritingSource()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.SetCurrentValue(Target.ValueProperty, 2);

        Assert.AreEqual(2, target.Value);
        Assert.AreEqual(1, source.Value);
        Assert.IsTrue(target.HasPropertyBinding(Target.ValueProperty.Id));
        Assert.AreEqual(ValueSource.Binding, target.PropertyStore.GetSource(Target.ValueProperty.Id));

        source.Value = 3;

        Assert.AreEqual(3, target.Value);
    }

    [TestMethod]
    public void SetCurrentValue_WithoutBindingSetsLocalValue()
    {
        var target = new Target();

        target.SetCurrentValue(Target.ValueProperty, 2);

        Assert.AreEqual(2, target.Value);
        Assert.AreEqual(ValueSource.Local, target.PropertyStore.GetSource(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void ObservableTwoWayBinding_DirectWriteRemovesBindingWithoutUpdatingSource()
    {
        var source = new ObservableValue<int>(1);
        var sourceChangeCount = 0;
        source.Changed += () => sourceChangeCount++;
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.Value = 2;

        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(0, sourceChangeCount);
        Assert.AreEqual(2, target.Value);
        Assert.IsFalse(target.HasPropertyBinding(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void ObservableTwoWayBinding_EqualDirectWriteStillRemovesBinding()
    {
        var source = new ObservableValue<int>(1);
        var sourceChangeCount = 0;
        source.Changed += () => sourceChangeCount++;
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.Value = 1;

        Assert.AreEqual(0, sourceChangeCount);
        Assert.IsFalse(target.HasPropertyBinding(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void RejectedDirectWrite_DoesNotRemoveTheBinding()
    {
        var source = new ObservableValue<int>(1);
        var target = new ValidatedTarget();
        target.SetBinding(ValidatedTarget.ValueProperty, source, BindingMode.OneWay);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => target.Value = -1);

        Assert.IsTrue(target.HasPropertyBinding(ValidatedTarget.ValueProperty.Id));
        Assert.AreEqual(1, target.Value);

        source.Value = 2;
        Assert.AreEqual(2, target.Value);
    }

    [TestMethod]
    public void ObservableTwoWayBinding_TargetCommitReadsBackNormalizedSource()
    {
        var source = new ObservableValue<int>(1, static value => Math.Clamp(value, 0, 10));
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.Commit(99);

        Assert.AreEqual(10, source.Value, "the source coerces the submitted target value");
        Assert.AreEqual(10, target.Value);
        Assert.IsTrue(target.HasPropertyBinding(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void TargetCommit_SubmitsCoercedTargetValueExactlyOnce()
    {
        var source = new ObservableValue<int>(1);
        var sourceChangeCount = 0;
        source.Changed += () => sourceChangeCount++;
        var target = new CoercedTarget();
        target.SetBinding(CoercedTarget.ValueProperty, source, BindingMode.TwoWay);

        target.Commit(99);

        Assert.AreEqual(10, source.Value);
        Assert.AreEqual(10, target.Value);
        Assert.AreEqual(1, sourceChangeCount);
    }

    [TestMethod]
    public void ConvertedObservableTwoWayBinding_TargetCommitReadsBackNormalizedSource()
    {
        var source = new ObservableValue<int>(1, static value => Math.Clamp(value, 0, 10));
        var target = new TextTarget();
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        target.Commit("99");

        Assert.AreEqual(10, source.Value);
        Assert.AreEqual("10", target.Text);
    }

    [TestMethod]
    public void OneWayBinding_TargetCommitDoesNotUpdateSource()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        target.Commit(2);

        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(2, target.Value);
        Assert.IsTrue(target.HasPropertyBinding(Target.ValueProperty.Id));

        source.Value = 3;

        Assert.AreEqual(3, target.Value);
    }

    [TestMethod]
    public void TriggerChanges_DoNotWriteTwoWaySource()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.PropertyStore.SetElementTrigger(Target.ValueProperty, 9);
        target.PropertyStore.ClearSource(Target.ValueProperty.Id, ValueSource.ElementTrigger);

        Assert.AreEqual(1, source.Value);
    }

    [TestMethod]
    public void ShadowedTargetCommit_SubmitsBindingCandidate()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);
        target.PropertyStore.SetElementTrigger(Target.ValueProperty, 9);

        target.Commit(2);

        Assert.AreEqual(2, source.Value);
        Assert.AreEqual(9, target.Value);

        target.PropertyStore.ClearSource(Target.ValueProperty.Id, ValueSource.ElementTrigger);

        Assert.AreEqual(2, target.Value);
    }

    [TestMethod]
    public void ShadowedSourcePush_RefreshesBindingCandidateEvenWhenEffectiveValueMatches()
    {
        var source = new ObservableValue<int>(0);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);
        target.PropertyStore.SetElementTrigger(Target.ValueProperty, 2);

        source.Value = 2;
        target.PropertyStore.ClearSource(Target.ValueProperty.Id, ValueSource.ElementTrigger);

        Assert.AreEqual(2, target.Value);
        Assert.IsTrue(target.HasBindingTargetValue(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void ClearObservableTwoWayBinding_RemovesWriteBackCallback()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.ClearBinding(Target.ValueProperty);
        target.Value = 2;

        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(2, target.Value);
    }

    private sealed class Target : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<Target>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public void Commit(int value) => CommitTargetValue(ValueProperty, value);
    }

    private sealed class TextTarget : MewObject
    {
        public static readonly MewProperty<string> TextProperty =
            MewProperty<string>.Register<TextTarget>(nameof(Text), string.Empty);

        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public void Commit(string value) => CommitTargetValue(TextProperty, value);
    }

    private sealed class CoercedTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<CoercedTarget>(
                nameof(Value),
                0,
                coerce: static (_, value) => Math.Clamp(value, 0, 10));

        public int Value => GetValue(ValueProperty);

        public void Commit(int value) => CommitTargetValue(ValueProperty, value);
    }

    private sealed class ValidatedTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<ValidatedTarget>(
                nameof(Value),
                0,
                validate: static (_, value) =>
                {
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }
                });

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
