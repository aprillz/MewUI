using Aprillz.MewUI.Binding;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class ObservableValueTests
{
    [TestMethod]
    public void Constructor_SetsInitialValue()
    {
        var observable = new ObservableValue<int>(42);

        Assert.AreEqual(42, observable.Value);
    }

    [TestMethod]
    public void Constructor_WithDefaultValue_UsesTypeDefault()
    {
        var observable = new ObservableValue<int>();

        Assert.AreEqual(0, observable.Value);
    }

    [TestMethod]
    public void Value_Set_UpdatesValue()
    {
        var observable = new ObservableValue<int>(0);

        observable.Value = 42;

        Assert.AreEqual(42, observable.Value);
    }

    [TestMethod]
    public void Set_ReturnsTrue_WhenValueChanges()
    {
        var observable = new ObservableValue<int>(0);

        var result = observable.Set(42);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Set_ReturnsFalse_WhenValueIsSame()
    {
        var observable = new ObservableValue<int>(42);

        var result = observable.Set(42);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Changed_IsRaised_WhenValueChanges()
    {
        var observable = new ObservableValue<int>(0);
        var changedCount = 0;
        observable.Changed += () => changedCount++;

        observable.Value = 42;

        Assert.AreEqual(1, changedCount);
    }

    [TestMethod]
    public void Changed_IsNotRaised_WhenValueIsSame()
    {
        var observable = new ObservableValue<int>(42);
        var changedCount = 0;
        observable.Changed += () => changedCount++;

        observable.Value = 42;

        Assert.AreEqual(0, changedCount);
    }

    [TestMethod]
    public void Subscribe_AddsHandler()
    {
        var observable = new ObservableValue<int>(0);
        var changedCount = 0;
        void Handler() => changedCount++;

        observable.Subscribe(Handler);
        observable.Value = 42;

        Assert.AreEqual(1, changedCount);
    }

    [TestMethod]
    public void Unsubscribe_RemovesHandler()
    {
        var observable = new ObservableValue<int>(0);
        var changedCount = 0;
        void Handler() => changedCount++;

        observable.Subscribe(Handler);
        observable.Unsubscribe(Handler);
        observable.Value = 42;

        Assert.AreEqual(0, changedCount);
    }

    [TestMethod]
    public void NotifyChanged_RaisesChangedEvent()
    {
        var observable = new ObservableValue<int>(42);
        var changedCount = 0;
        observable.Changed += () => changedCount++;

        observable.NotifyChanged();

        Assert.AreEqual(1, changedCount);
    }

    [TestMethod]
    public void Coerce_AppliesOnConstruction()
    {
        var observable = new ObservableValue<int>(
            initialValue: -5,
            coerce: value => Math.Max(0, value));

        Assert.AreEqual(0, observable.Value);
    }

    [TestMethod]
    public void Coerce_AppliesOnSet()
    {
        var observable = new ObservableValue<int>(
            initialValue: 0,
            coerce: value => Math.Clamp(value, 0, 100));

        observable.Value = 150;

        Assert.AreEqual(100, observable.Value);
    }

    [TestMethod]
    public void CustomComparer_UsedForEquality()
    {
        var observable = new ObservableValue<string>(
            initialValue: "hello",
            comparer: StringComparer.OrdinalIgnoreCase);

        var result = observable.Set("HELLO");

        Assert.IsFalse(result);
        Assert.AreEqual("hello", observable.Value);
    }

    [TestMethod]
    public void MultipleHandlers_AllInvoked()
    {
        var observable = new ObservableValue<int>(0);
        var handler1Count = 0;
        var handler2Count = 0;
        observable.Changed += () => handler1Count++;
        observable.Changed += () => handler2Count++;

        observable.Value = 42;

        Assert.AreEqual(1, handler1Count);
        Assert.AreEqual(1, handler2Count);
    }

    [TestMethod]
    public void ReferenceType_WorksCorrectly()
    {
        var observable = new ObservableValue<string>("initial");
        var changedCount = 0;
        observable.Changed += () => changedCount++;

        observable.Value = "updated";

        Assert.AreEqual("updated", observable.Value);
        Assert.AreEqual(1, changedCount);
    }

    [TestMethod]
    public void NullValue_HandledCorrectly()
    {
        var observable = new ObservableValue<string?>("not null");
        var changedCount = 0;
        observable.Changed += () => changedCount++;

        observable.Value = null;

        Assert.IsNull(observable.Value);
        Assert.AreEqual(1, changedCount);
    }
}
