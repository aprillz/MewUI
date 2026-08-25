using System.Runtime.CompilerServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class WeakBindingTests
{
    [TestMethod]
    public void ObservableBinding_DoesNotKeepTargetAlive()
    {
        var source = new ObservableValue<int>(1);
        var targetReference = CreateObservableBinding(source);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        source.Value = 2;
    }

    [TestMethod]
    public void MewPropertyBinding_DoesNotKeepTargetAlive()
    {
        var source = new TestObject { Value = 1 };
        var targetReference = CreatePropertyBinding(source);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        source.Value = 2;
    }

    [TestMethod]
    public void DisposedBinding_StopsObservableUpdates()
    {
        var source = new ObservableValue<int>(1);
        var target = new TestObject();
        target.SetBinding(TestObject.ValueProperty, source);
        target.DisposeBindings();

        source.Value = 2;

        Assert.AreEqual(1, target.Value);
    }

    [TestMethod]
    public void BindingPath_DoesNotKeepTargetAlive()
    {
        var root = new PathRoot();
        var targetReference = CreatePathBinding(root);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        root.Value.Value = 2;
    }

    [TestMethod]
    public void ClearedBindingPath_ReleasesRootWhileTargetStaysAlive()
    {
        var rootReference = CreateRootHeldByPath(out var target);

        Collect(rootReference);
        Assert.IsTrue(rootReference.IsAlive, "an active target binding owns its root");

        target.DisposeBindings();
        Collect(rootReference);

        Assert.IsFalse(rootReference.IsAlive);
        GC.KeepAlive(target);
    }

    [TestMethod]
    public void RewiredBindingPath_ReleasesPreviousDownstreamObject()
    {
        var oldLeafReference = CreateRewiredPath(
            out var root,
            out var target);

        Collect(oldLeafReference);

        Assert.IsFalse(oldLeafReference.IsAlive);
        GC.KeepAlive(root);
        GC.KeepAlive(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateObservableBinding(ObservableValue<int> source)
    {
        var target = new TestObject();
        target.SetBinding(TestObject.ValueProperty, source);
        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreatePropertyBinding(TestObject source)
    {
        var target = new TestObject();
        target.SetBinding(TestObject.ValueProperty, source, TestObject.ValueProperty);
        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreatePathBinding(PathRoot root)
    {
        var target = new TestObject();
        var path = BindingPath.From<PathRoot>().Then(static value => value.Value);
        target.SetBinding(TestObject.ValueProperty, root, path, BindingMode.OneWay);
        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRootHeldByPath(out TestObject target)
    {
        var root = new PathRoot();
        target = new TestObject();
        var path = BindingPath.From<PathRoot>().Then(static value => value.Value);
        target.SetBinding(TestObject.ValueProperty, root, path, BindingMode.OneWay);
        return new WeakReference(root);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRewiredPath(
        out RewireRoot root,
        out TestObject target)
    {
        var oldLeaf = new RewireLeaf(1);
        var node = new RewireNode(oldLeaf);
        root = new RewireRoot(node);
        target = new TestObject();
        var path = BindingPath
            .From<RewireRoot>()
            .Then(static value => value.Node)
            .Then(static value => value.Leaf)
            .Then(static value => value!.Value);
        target.SetBinding(TestObject.ValueProperty, root, path, BindingMode.OneWay);

        node.Leaf.Value = new RewireLeaf(2);
        return new WeakReference(oldLeaf);
    }

    private static void Collect(WeakReference reference)
    {
        for (int i = 0; i < 5 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class TestObject : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<TestObject>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public void DisposeBindings() => DisposePropertyBindings();
    }

    private sealed class PathRoot
    {
        public ObservableValue<int> Value { get; } = new(1);
    }

    private sealed class RewireRoot(RewireNode node)
    {
        public ObservableValue<RewireNode> Node { get; } = new(node);
    }

    private sealed class RewireNode(RewireLeaf leaf)
    {
        public ObservableValue<RewireLeaf?> Leaf { get; } = new(leaf);
    }

    private sealed class RewireLeaf(int value)
    {
        public ObservableValue<int> Value { get; } = new(value);
    }
}
