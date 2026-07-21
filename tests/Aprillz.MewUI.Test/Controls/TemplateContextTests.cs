using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TemplateContextTests
{
    [TestMethod]
    public void Bind_ResetRemovesPropertyBinding()
    {
        var context = new TemplateContext();
        var source = new ObservableValue<string>("first");
        var target = new TextBlock();

        context.Bind(target, TextBlock.TextProperty, source);
        source.Value = "second";
        Assert.AreEqual("second", target.Text);

        context.Reset();
        source.Value = "third";

        Assert.AreEqual("second", target.Text);
    }

    [TestMethod]
    public void Bind_RebindingSamePropertyIsCleanedOnce()
    {
        var context = new TemplateContext();
        var first = new ObservableValue<string>("first");
        var second = new ObservableValue<string>("second");
        var target = new TextBlock();

        context.Bind(target, TextBlock.TextProperty, first);
        context.Bind(target, TextBlock.TextProperty, second);
        context.Reset();

        first.Value = "old";
        second.Value = "new";

        Assert.AreEqual("second", target.Text);
    }

    [TestMethod]
    public void Bind_WithConverter_ResetRemovesPropertyBinding()
    {
        var context = new TemplateContext();
        var source = new ObservableValue<int>(1);
        var target = new TextBlock();

        context.Bind(target, TextBlock.TextProperty, source, static value => value.ToString());
        Assert.AreEqual("1", target.Text);

        context.Reset();
        source.Value = 2;

        Assert.AreEqual("1", target.Text);
    }

    [TestMethod]
    public void Bind_WithBindingPath_ResetRemovesCompletePath()
    {
        var context = new TemplateContext();
        var source = new PathRoot();
        source.Node.Value = new PathNode("first");
        var target = new TextBlock();
        var path = BindingPath
            .From<PathRoot>()
            .Then(static value => value.Node)
            .Then(static value => value!.Text);

        context.Bind(
            target,
            TextBlock.TextProperty,
            source,
            path,
            BindingMode.OneWay,
            fallbackValue: "-");
        source.Node.Value!.Text.Value = "second";
        Assert.AreEqual("second", target.Text);

        context.Reset();
        source.Node.Value.Text.Value = "third";

        Assert.AreEqual("second", target.Text);
    }

    [TestMethod]
    public void Bind_WithBindingPath_AppliesFallbackAndRecovers()
    {
        var context = new TemplateContext();
        var source = new PathRoot();
        var target = new TextBlock();
        var path = BindingPath
            .From<PathRoot>()
            .Then(static value => value.Node)
            .Then(static value => value!.Text);

        context.Bind(
            target,
            TextBlock.TextProperty,
            source,
            path,
            BindingMode.OneWay,
            fallbackValue: "-");
        Assert.AreEqual("-", target.Text);

        source.Node.Value = new PathNode("available");

        Assert.AreEqual("available", target.Text);
    }

    [TestMethod]
    public void Bind_WithConvertedBindingPath_ResetRemovesBinding()
    {
        var context = new TemplateContext();
        var source = new PathRoot();
        source.Count.Value = 1;
        var target = new TextBlock();
        var path = BindingPath.From<PathRoot>().Then(static value => value.Count);

        context.Bind(
            target,
            TextBlock.TextProperty,
            source,
            path,
            static value => $"Count: {value}",
            mode: BindingMode.OneWay);
        Assert.AreEqual("Count: 1", target.Text);

        context.Reset();
        source.Count.Value = 2;

        Assert.AreEqual("Count: 1", target.Text);
    }

    [TestMethod]
    public void Subscribe_SupportsNotifyCollectionChangedEventHandler()
    {
        var context = new TemplateContext();
        var source = new ObservableCollection<int>();
        var calls = 0;
        NotifyCollectionChangedEventHandler handler = (_, _) => calls++;

        context.Subscribe(
            source,
            static (collection, value) => collection.CollectionChanged += value,
            static (collection, value) => collection.CollectionChanged -= value,
            handler);

        source.Add(1);
        context.Reset();
        source.Add(2);

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void Reset_RunsAllCleanupInReverseOrder()
    {
        var context = new TemplateContext();
        var first = new EventSource();
        var second = new EventSource();
        var calls = new List<int>();

        context.Subscribe(
            first,
            static (s, h) => s.Changed += h,
            (s, h) =>
            {
                calls.Add(1);
                s.Changed -= h;
            },
            () => { });
        context.Subscribe(
            second,
            static (s, h) => s.Changed += h,
            (s, h) =>
            {
                calls.Add(2);
                s.Changed -= h;
            },
            () => { });

        context.Reset();

        CollectionAssert.AreEqual(new[] { 2, 1 }, calls);
    }

    [TestMethod]
    public void Reset_KeepsNamedElements()
    {
        var context = new TemplateContext();
        var target = new TextBlock();
        context.Register("Target", target);

        context.Reset();

        Assert.AreSame(target, context.Get<TextBlock>("Target"));
    }

    [TestMethod]
    public void BindingState_RebindsInLifecycleOrder()
    {
        var calls = new List<string>();
        var context = new TemplateContext();
        var source = new EventSource();
        var view = new TextBlock();
        var template = new DelegateTemplate<string>(
            build: _ => view,
            bind: (_, item, _, ctx) =>
            {
                calls.Add($"bind:{item}");
                ctx.Subscribe(
                    source,
                    static (s, h) => s.Changed += h,
                    (s, h) =>
                    {
                        calls.Add("reset");
                        s.Changed -= h;
                    },
                    () => { });
            },
            unbind: (_, item, _, _) => calls.Add($"unbind:{item}"));

        context.BindTemplate(view, template, "first", 0);
        context.BindTemplate(view, template, "second", 1);

        CollectionAssert.AreEqual(
            new[] { "bind:first", "unbind:first", "reset", "bind:second" },
            calls);
    }

    private sealed class EventSource
    {
        private Action? _changed;

        public event Action Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }
    }

    private sealed class PathRoot
    {
        public ObservableValue<PathNode?> Node { get; } = new();

        public ObservableValue<int> Count { get; } = new();
    }

    private sealed class PathNode(string text)
    {
        public ObservableValue<string> Text { get; } = new(text);
    }
}
