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
}
