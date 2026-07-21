using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class BindingPathTests
{
    [TestMethod]
    public void GetterLeaf_ProvidesInitialValueWithoutObservingReplacement()
    {
        var root = new Root { PlainNumber = 3 };
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.PlainNumber);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);
        root.PlainNumber = 7;

        Assert.AreEqual(3, target.Value);
    }

    [TestMethod]
    public void ObservableLeaf_ProvidesInitialValueAndUpdates()
    {
        var root = new Root();
        root.Number.Value = 4;
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.Number);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);
        root.Number.Value = 8;

        Assert.AreEqual(8, target.Value);
    }

    [TestMethod]
    public void MixedPath_RewiresWhenObservedIntermediateChanges()
    {
        var oldLeaf = new Leaf(1);
        var oldNode = new Node(oldLeaf);
        var root = new Root();
        root.OptionalNode.Value = oldNode;
        var target = new TestObject();
        var path = CreateAmountPath();

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);

        var newLeaf = new Leaf(2);
        oldNode.Leaf.Value = newLeaf;
        newLeaf.Amount.Value = 3;
        oldLeaf.Amount.Value = 99;

        Assert.AreEqual(3, target.Value);
    }

    [TestMethod]
    public void FourSegmentPath_RewiresWhenSecondSegmentObjectIsReplaced()
    {
        var oldThird = new FourDepthThird(1);
        var oldSecond = new FourDepthSecond(oldThird);
        var first = new FourDepthFirst(oldSecond);
        var root = new FourDepthRoot(first);
        var target = new TestObject();
        var path = BindingPath
            .From<FourDepthRoot>()
            .Then(static value => value.First)
            .Then(static value => value!.Second)
            .Then(static value => value!.Third)
            .Then(static value => value!.Value);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);

        var newThird = new FourDepthThird(2);
        first.Second.Value = new FourDepthSecond(newThird);
        Assert.AreEqual(2, target.Value);

        newThird.Value.Value = 3;
        Assert.AreEqual(3, target.Value);

        oldThird.Value.Value = 99;
        Assert.AreEqual(3, target.Value);
    }

    [TestMethod]
    public void NullIntermediate_AppliesFallbackAndRecovers()
    {
        var root = new Root();
        var target = new TestObject();
        var path = CreateAmountPath();

        target.SetBinding(
            TestObject.ValueProperty,
            root,
            path,
            mode: BindingMode.OneWay,
            fallbackValue: -1);
        Assert.AreEqual(-1, target.Value);

        root.OptionalNode.Value = new Node(new Leaf(7));
        Assert.AreEqual(7, target.Value);

        root.OptionalNode.Value = null;
        Assert.AreEqual(-1, target.Value);
    }

    [TestMethod]
    public void NullLeaf_IsAValueRatherThanFallback()
    {
        var root = new Root();
        root.Text.Value = null;
        var target = new TestObject { Text = "old" };
        var path = BindingPath.From<Root>().Then(static value => value.Text);

        target.SetBinding(
            TestObject.TextProperty,
            root,
            path,
            mode: BindingMode.OneWay,
            fallbackValue: "fallback");

        Assert.IsNull(target.Text);
    }

    [TestMethod]
    public void PlainGetterIntermediate_ReevaluatesOnlyAfterUpstreamNotification()
    {
        var first = new Leaf(1);
        var node = new Node(first) { PlainLeaf = first };
        var root = new Root(node);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(static value => value.PlainLeaf)
            .Then(static value => value.Amount);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);

        var second = new Leaf(2);
        node.PlainLeaf = second;
        second.Amount.Value = 3;
        Assert.AreEqual(1, target.Value);

        root.RequiredNode.NotifyChanged();
        second.Amount.Value = 4;
        Assert.AreEqual(4, target.Value);
    }

    [TestMethod]
    public void MewPropertyLeaf_ProvidesInitialValueAndUpdates()
    {
        var node = new Node { Value = 5 };
        var root = new Root(node);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(Node.ValueProperty);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);
        node.Value = 9;

        Assert.AreEqual(9, target.Value);
    }

    [TestMethod]
    public void MewPropertyLeaf_ReadsAndObservesInheritedEffectiveValue()
    {
        var parent = new Border();
        var source = new Border();
        parent.Child = source;
        var first = Color.FromRgb(10, 20, 30);
        var second = Color.FromRgb(40, 50, 60);
        parent.Foreground = first;
        _ = source.Foreground;
        var target = new ColorObject();
        var path = BindingPath.From<Border>().Then(Control.ForegroundProperty);

        target.SetBinding(ColorObject.ValueProperty, source, path, mode: BindingMode.OneWay);
        parent.Foreground = second;

        Assert.AreEqual(second, target.Value);
    }

    [TestMethod]
    public void ReadOnlyMewPropertyLeaf_IsValidForOneWay()
    {
        var node = new Node();
        node.SetReadOnlyValue(5);
        var root = new Root(node);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(Node.ReadOnlyValueProperty);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);
        node.SetReadOnlyValue(8);

        Assert.AreEqual(8, target.Value);
    }

    [TestMethod]
    public void MewPropertyIntermediate_RewiresDownstream()
    {
        var oldLeaf = new Leaf(1);
        var node = new Node { Child = oldLeaf };
        var root = new Root(node);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(Node.ChildProperty)
            .Then(static value => value!.Amount);

        target.SetBinding(
            TestObject.ValueProperty,
            root,
            path,
            mode: BindingMode.OneWay,
            fallbackValue: -1);

        var newLeaf = new Leaf(2);
        node.Child = newLeaf;
        newLeaf.Amount.Value = 3;
        oldLeaf.Amount.Value = 99;

        Assert.AreEqual(3, target.Value);
    }

    [TestMethod]
    public void ObservableLeaf_TwoWayUpdatesCurrentSource()
    {
        var root = new Root();
        root.Number.Value = 2;
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.Number);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay);
        target.Value = 6;

        Assert.AreEqual(6, root.Number.Value);
    }

    [TestMethod]
    public void TwoWayPath_WritesOnlyToRewiredLeaf()
    {
        var oldLeaf = new Leaf(1);
        var root = new Root();
        root.OptionalNode.Value = new Node(oldLeaf);
        var target = new TestObject();
        var path = CreateAmountPath();

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay);
        var newLeaf = new Leaf(2);
        root.OptionalNode.Value = new Node(newLeaf);
        target.Value = 5;

        Assert.AreEqual(1, oldLeaf.Amount.Value);
        Assert.AreEqual(5, newLeaf.Amount.Value);
    }

    [TestMethod]
    public void TwoWayPath_DropsTargetChangesWhileUnavailable()
    {
        var root = new Root();
        var target = new TestObject();
        var path = CreateAmountPath();

        target.SetBinding(
            TestObject.ValueProperty,
            root,
            path,
            mode: BindingMode.TwoWay,
            fallbackValue: -1);
        target.Value = 20;

        var leaf = new Leaf(7);
        root.OptionalNode.Value = new Node(leaf);

        Assert.AreEqual(7, target.Value);
        Assert.AreEqual(7, leaf.Amount.Value);
    }

    [TestMethod]
    public void MewPropertyLeaf_SupportsTwoWay()
    {
        var node = new Node { Value = 3 };
        var root = new Root(node);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(Node.ValueProperty);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay);
        target.Value = 11;

        Assert.AreEqual(11, node.Value);
    }

    [TestMethod]
    public void MewPropertyLeaf_TwoWayPreservesCoercion()
    {
        var node = new Node { ClampedValue = 3 };
        var root = new Root(node);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(Node.ClampedValueProperty);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay);
        target.Value = 99;

        Assert.AreEqual(10, node.ClampedValue);
        Assert.AreEqual(10, target.Value);
    }

    [TestMethod]
    public void GetterLeaf_RejectsTwoWayWithoutReplacingExistingBinding()
    {
        var existing = new ObservableValue<int>(1);
        var target = new TestObject();
        target.SetBinding(TestObject.ValueProperty, existing, BindingMode.OneWay);
        var root = new Root { PlainNumber = 2 };
        var path = BindingPath.From<Root>().Then(static value => value.PlainNumber);

        Assert.ThrowsExactly<ArgumentException>(() =>
            target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay));

        existing.Value = 4;
        Assert.AreEqual(4, target.Value);
    }

    [TestMethod]
    public void ReadOnlyMewPropertyLeaf_RejectsTwoWay()
    {
        var root = new Root(new Node());
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(Node.ReadOnlyValueProperty);

        Assert.ThrowsExactly<ArgumentException>(() =>
            target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay));
    }

    [TestMethod]
    public void ConvertedOneWayPath_UpdatesTarget()
    {
        var root = new Root();
        root.Number.Value = 2;
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.Number);

        target.SetBinding(
            TestObject.TextProperty,
            root,
            path,
            static value => $"Value:{value}",
            mode: BindingMode.OneWay,
            fallbackValue: "-");
        root.Number.Value = 4;

        Assert.AreEqual("Value:4", target.Text);
    }

    [TestMethod]
    public void ConvertedTwoWayPath_RequiresConvertBack()
    {
        var root = new Root();
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.Number);

        Assert.ThrowsExactly<ArgumentException>(() =>
            target.SetBinding(
                TestObject.TextProperty,
                root,
                path,
                static value => value.ToString(),
                mode: BindingMode.TwoWay));
    }

    [TestMethod]
    public void ConvertedTwoWayPath_UsesConvertBack()
    {
        var root = new Root();
        root.Number.Value = 2;
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.Number);

        target.SetBinding(
            TestObject.TextProperty,
            root,
            path,
            static value => value.ToString(),
            static value => int.Parse(value!),
            mode: BindingMode.TwoWay);
        target.Text = "12";

        Assert.AreEqual(12, root.Number.Value);
    }

    [TestMethod]
    public void TwoWayPath_ReflectsSourceCoercionBackToTarget()
    {
        var root = new Root();
        root.ClampedNumber.Value = 2;
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.ClampedNumber);

        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.TwoWay);
        target.Value = 99;

        Assert.AreEqual(10, root.ClampedNumber.Value);
        Assert.AreEqual(10, target.Value);
    }

    [TestMethod]
    public void ClearBinding_StopsAllPathUpdates()
    {
        var root = new Root();
        var target = new TestObject();
        var path = BindingPath.From<Root>().Then(static value => value.Number);
        target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay);

        target.ClearBinding(TestObject.ValueProperty);
        root.Number.Value = 9;

        Assert.AreEqual(0, target.Value);
    }

    [TestMethod]
    public void Descriptor_CanBeReusedAcrossBindings()
    {
        var path = BindingPath.From<Root>().Then(static value => value.Number);
        var firstRoot = new Root();
        var secondRoot = new Root();
        firstRoot.Number.Value = 1;
        secondRoot.Number.Value = 2;
        var firstTarget = new TestObject();
        var secondTarget = new TestObject();

        firstTarget.Bind(TestObject.ValueProperty, firstRoot, path, BindingMode.OneWay);
        secondTarget.Bind(TestObject.ValueProperty, secondRoot, path, BindingMode.OneWay);
        firstRoot.Number.Value = 3;
        secondRoot.Number.Value = 4;

        Assert.AreEqual(3, firstTarget.Value);
        Assert.AreEqual(4, secondTarget.Value);
    }

    [TestMethod]
    public void NullObservableSelector_Throws()
    {
        var root = new Root();
        var target = new TestObject();
        Func<Root, ObservableValue<int>> selector = static _ => null!;
        var path = BindingPath.From<Root>().Then(selector);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay));
    }

    [TestMethod]
    public void PartialAttachFailure_DetachesPreviouslyObservedSegments()
    {
        var root = new Root(new Node());
        var target = new TestObject();
        Func<Node, int> throwingGetter = static _ =>
            throw new InvalidOperationException("attach failed");
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.RequiredNode)
            .Then(throwingGetter);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            target.SetBinding(TestObject.ValueProperty, root, path, mode: BindingMode.OneWay));

        root.RequiredNode.NotifyChanged();
    }

    [TestMethod]
    public void ConverterFailure_LeavesTargetUnchangedAndPropagates()
    {
        var root = new Root();
        root.Number.Value = 1;
        var target = new TestObject { Text = "initial" };
        var path = BindingPath.From<Root>().Then(static value => value.Number);

        target.SetBinding(
            TestObject.TextProperty,
            root,
            path,
            static value => value == 2
                ? throw new InvalidOperationException("convert failed")
                : value.ToString(),
            mode: BindingMode.OneWay);

        Assert.ThrowsExactly<InvalidOperationException>(() => root.Number.Value = 2);
        Assert.AreEqual("1", target.Text);
    }

    private static BindingPath<Root, int> CreateAmountPath()
        => BindingPath
            .From<Root>()
            .Then(static value => value.OptionalNode)
            .Then(static value => value!.Leaf)
            .Then(static value => value!.Amount);

    private sealed class Root
    {
        public Root()
            : this(new Node())
        {
        }

        public Root(Node requiredNode)
        {
            RequiredNode = new ObservableValue<Node>(requiredNode);
        }

        public int PlainNumber { get; set; }

        public ObservableValue<int> Number { get; } = new();

        public ObservableValue<int> ClampedNumber { get; } = new(
            coerce: static value => Math.Clamp(value, 0, 10));

        public ObservableValue<string?> Text { get; } = new();

        public ObservableValue<Node?> OptionalNode { get; } = new();

        public ObservableValue<Node> RequiredNode { get; }
    }

    private sealed class Node : MewObject
    {
        private static readonly MewPropertyKey<int> ReadOnlyValuePropertyKey =
            MewProperty<int>.RegisterReadOnly<Node>(nameof(ReadOnlyValue), 0);

        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<Node>(nameof(Value), 0);

        public static readonly MewProperty<Leaf?> ChildProperty =
            MewProperty<Leaf?>.Register<Node>(nameof(Child), null);

        public static readonly MewProperty<int> ClampedValueProperty =
            MewProperty<int>.Register<Node>(
                nameof(ClampedValue),
                0,
                coerce: static (_, value) => Math.Clamp(value, 0, 10));

        public static MewProperty<int> ReadOnlyValueProperty => ReadOnlyValuePropertyKey.Property;

        public Node()
            : this(new Leaf())
        {
        }

        public Node(Leaf leaf)
        {
            Leaf = new ObservableValue<Leaf?>(leaf);
            PlainLeaf = leaf;
        }

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Leaf? Child
        {
            get => GetValue(ChildProperty);
            set => SetValue(ChildProperty, value);
        }

        public int ClampedValue
        {
            get => GetValue(ClampedValueProperty);
            set => SetValue(ClampedValueProperty, value);
        }

        public int ReadOnlyValue => GetValue(ReadOnlyValueProperty);

        public ObservableValue<Leaf?> Leaf { get; }

        public Leaf PlainLeaf { get; set; }

        public void SetReadOnlyValue(int value) => SetValue(ReadOnlyValuePropertyKey, value);
    }

    private sealed class Leaf
    {
        public Leaf(int amount = 0)
        {
            Amount = new ObservableValue<int>(amount);
        }

        public ObservableValue<int> Amount { get; }
    }

    private sealed class FourDepthRoot(FourDepthFirst first)
    {
        public ObservableValue<FourDepthFirst> First { get; } = new(first);
    }

    private sealed class FourDepthFirst(FourDepthSecond second)
    {
        public ObservableValue<FourDepthSecond> Second { get; } = new(second);
    }

    private sealed class FourDepthSecond(FourDepthThird third)
    {
        public ObservableValue<FourDepthThird> Third { get; } = new(third);
    }

    private sealed class FourDepthThird(int value)
    {
        public ObservableValue<int> Value { get; } = new(value);
    }

    private sealed class TestObject : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<TestObject>(
                nameof(Value),
                0,
                MewPropertyOptions.BindsTwoWayByDefault);

        public static readonly MewProperty<string?> TextProperty =
            MewProperty<string?>.Register<TestObject>(
                nameof(Text),
                null,
                MewPropertyOptions.BindsTwoWayByDefault);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }

    private sealed class ColorObject : MewObject
    {
        public static readonly MewProperty<Color> ValueProperty =
            MewProperty<Color>.Register<ColorObject>(nameof(Value), default);

        public Color Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
