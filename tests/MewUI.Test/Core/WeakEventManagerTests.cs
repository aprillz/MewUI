using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Aprillz.MewUI;

namespace MewUI.Test.Core;

[TestClass]
public sealed class WeakEventManagerTests
{
    private static readonly WeakEventKey<ActionSource, Action> ChangedEvent = new(
        static (source, handler) => source.Changed += handler,
        static (source, handler) => source.Changed -= handler);

    private static readonly WeakEventKey<ActionSource, Action> AlternateChangedEvent = new(
        static (source, handler) => source.Changed += handler,
        static (source, handler) => source.Changed -= handler);

    private static readonly WeakEventKey<INotifyCollectionChanged, NotifyCollectionChangedEventHandler>
        CollectionChangedEvent = new(
            static (source, handler) => source.CollectionChanged += handler,
            static (source, handler) => source.CollectionChanged -= handler);

    [TestMethod]
    public void ActionHandler_InvokesTarget()
    {
        var source = new ActionSource();
        var target = new Target();

        WeakEventManager.AddHandler(
            ChangedEvent,
            source,
            target,
            static current => current.ActionCount++);

        source.Raise();

        Assert.AreEqual(1, target.ActionCount);
    }

    [TestMethod]
    public void RemoveHandler_RemovesOnlySpecifiedEventKey()
    {
        var source = new ActionSource();
        var target = new Target();

        WeakEventManager.AddHandler(
            ChangedEvent,
            source,
            target,
            static current => current.ActionCount++);
        WeakEventManager.AddHandler(
            AlternateChangedEvent,
            source,
            target,
            static current => current.AlternateCount++);

        WeakEventManager.RemoveHandler(ChangedEvent, source, target);
        source.Raise();

        Assert.AreEqual(0, target.ActionCount);
        Assert.AreEqual(1, target.AlternateCount);
    }

    [TestMethod]
    public void DuplicateRegistration_Throws()
    {
        var source = new ActionSource();
        var target = new Target();

        WeakEventManager.AddHandler(
            ChangedEvent,
            source,
            target,
            static current => current.ActionCount++);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            WeakEventManager.AddHandler(
                ChangedEvent,
                source,
                target,
                static current => current.ActionCount++));
    }

    [TestMethod]
    public void CapturedCallback_Throws()
    {
        var source = new ActionSource();
        var target = new Target();

        Assert.ThrowsExactly<ArgumentException>(() =>
            WeakEventManager.AddHandler(
                ChangedEvent,
                source,
                target,
                _ => target.ActionCount++));
    }

    [TestMethod]
    public void CapturedEventAccessor_Throws()
    {
        var capture = new object();

        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new WeakEventKey<ActionSource, Action>(
                (source, handler) =>
                {
                    GC.KeepAlive(capture);
                    source.Changed += handler;
                },
                static (source, handler) => source.Changed -= handler));
    }

    [TestMethod]
    public void Registration_DoesNotKeepTargetAlive()
    {
        var source = new ActionSource();
        var targetReference = RegisterTemporaryTarget(source);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        Assert.AreEqual(1, source.HandlerCount);

        source.Raise();

        Assert.AreEqual(0, source.HandlerCount);
    }

    [TestMethod]
    public void CollectionChangedHandler_InvokesTarget()
    {
        var source = new ObservableCollection<int>();
        var target = new Target();

        WeakEventManager.AddHandler(
            CollectionChangedEvent,
            source,
            target,
            static (current, _, args) => current.CollectionAction = args.Action);

        source.Add(1);

        Assert.AreEqual(NotifyCollectionChangedAction.Add, target.CollectionAction);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterTemporaryTarget(ActionSource source)
    {
        var target = new Target();
        WeakEventManager.AddHandler(
            ChangedEvent,
            source,
            target,
            static current => current.ActionCount++);
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

    private sealed class ActionSource
    {
        private Action? _changed;

        public event Action Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        public void Raise() => _changed?.Invoke();
    }

    private sealed class Target
    {
        public int ActionCount { get; set; }
        public int AlternateCount { get; set; }
        public NotifyCollectionChangedAction? CollectionAction { get; set; }
    }
}
