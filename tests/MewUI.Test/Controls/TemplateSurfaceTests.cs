using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The contracts a template author writes against: what the interfaces forward, what the two
/// contexts reject, and which overloads resolve without spelling the types out.
/// </summary>
[TestClass]
public sealed class TemplateSurfaceTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    private sealed class TypedOnlyTemplate : IDataTemplate<string>
    {
        public string? Bound;
        public string? Unbound;

        public FrameworkElement Build(TemplateContext context) => new TextBlock();

        public void Bind(FrameworkElement view, string item, int index, TemplateContext context) => Bound = item;

        public void Unbind(FrameworkElement view, string item, int index, TemplateContext context) => Unbound = item;
    }

    [TestMethod]
    public void TypedTemplate_UntypedCallsForwardToTheTypedMembers()
    {
        var template = new TypedOnlyTemplate();
        IDataTemplate untyped = template;
        var context = new TemplateContext();
        var view = untyped.Build(context);

        untyped.Bind(view, "Alpha", 0, context);
        untyped.Unbind(view, "Alpha", 0, context);

        Assert.AreEqual("Alpha", template.Bound);
        Assert.AreEqual("Alpha", template.Unbound);
    }

    [TestMethod]
    public void TypedTemplate_NullPassesAsDefaultAndAnotherTypeThrows()
    {
        var template = new TypedOnlyTemplate { Bound = "stale" };
        IDataTemplate untyped = template;
        var context = new TemplateContext();
        var view = untyped.Build(context);

        untyped.Bind(view, null, 0, context);
        Assert.IsNull(template.Bound);

        Assert.ThrowsExactly<InvalidCastException>(() => untyped.Bind(view, 42, 0, context));
    }

    [TestMethod]
    public void Register_RejectsANameAlreadyTaken()
    {
        var context = new TemplateContext();
        context.Register("Text", new TextBlock());

        Assert.ThrowsExactly<InvalidOperationException>(() => context.Register("Text", new TextBlock()));
    }

    [TestMethod]
    public void Get_ThrowsTheSameExceptionForAMissingNameAndAWrongType()
    {
        var context = new TemplateContext();
        context.Register("Text", new TextBlock());

        Assert.ThrowsExactly<InvalidOperationException>(() => context.Get<TextBlock>("Missing"));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Get<Border>("Text"));
    }

    private sealed class Source
    {
        public event Action? Changed;
        public event Action<int>? ValueChanged;
        public event EventHandler<EventArgs>? Raised;

        public void Fire()
        {
            Changed?.Invoke();
            ValueChanged?.Invoke(7);
            Raised?.Invoke(this, EventArgs.Empty);
        }
    }

    [TestMethod]
    public void Subscribe_CommonHandlerShapesNeedNoTypeArguments()
    {
        var context = new TemplateContext();
        var source = new Source();
        int plain = 0;
        int value = 0;
        int raised = 0;

        context.Subscribe(source, static (target, handler) => target.Changed += handler, static (target, handler) => target.Changed -= handler, () => plain++);
        context.Subscribe(source, static (target, handler) => target.ValueChanged += handler, static (target, handler) => target.ValueChanged -= handler, (int next) => value = next);
        context.Subscribe(source, static (target, handler) => target.Raised += handler, static (target, handler) => target.Raised -= handler, (object? _, EventArgs _) => raised++);

        source.Fire();
        Assert.AreEqual(1, plain);
        Assert.AreEqual(7, value);
        Assert.AreEqual(1, raised);

        context.Reset();
        source.Fire();
        Assert.AreEqual(1, plain, "reset removes the handler");
        Assert.AreEqual(1, raised);
    }

    private sealed class Model : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    [TestMethod]
    public void Subscribe_PropertyChangesNeedOnlyTheHandler()
    {
        var context = new TemplateContext();
        var model = new Model();
        var names = new List<string?>();

        context.Subscribe(model, (_, args) => names.Add(args.PropertyName));
        model.Raise("Name");
        context.Reset();
        model.Raise("Role");

        CollectionAssert.AreEqual(new[] { "Name" }, names);
    }

    [TestMethod]
    public void Subscribe_ExplicitTypeArgumentsStillPickTheGeneralOverload()
    {
        var context = new TemplateContext();
        var source = new Source();
        int value = 0;

        context.Subscribe<Source, Action<int>>(
            source,
            static (target, handler) => target.ValueChanged += handler,
            static (target, handler) => target.ValueChanged -= handler,
            next => value = next);

        source.Fire();
        Assert.AreEqual(7, value);
    }

    [TestMethod]
    public void ControlTemplateBind_ConvertsTheOwnerValueNowAndOnEveryChange()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        Border? part = null;
        var template = new DelegateControlTemplate<ContentControl>((owner, ctx) =>
        {
            part = new Border();
            ctx.Bind(part, UIElement.OpacityProperty, UIElement.IsEnabledProperty, static enabled => enabled ? 1.0 : 0.25);
            return part;
        });

        var window = HeadlessWindow.Create();
        var host = new ContentControl { Template = template };
        window.Content = host;
        window.PerformLayout();

        Assert.IsNotNull(part);
        Assert.AreEqual(1.0, part.Opacity);

        host.IsEnabled = false;
        Assert.AreEqual(0.25, part.Opacity);
    }

    [TestMethod]
    public void ItemTemplate_TypedRootOverloadsBindWithoutACast()
    {
        var texts = new List<string>();
        var box = new ListBox { Width = WIDTH, Height = HEIGHT };
        box.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });
        box.ItemTemplate(
            build: _ => new TextBlock(),
            bind: (TextBlock label, string item) =>
            {
                label.Text = item;
                texts.Add(item);
            });

        Layout(box);

        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, texts);
    }

    private sealed record Person(string Name)
    {
        public override string ToString() => "person " + Name;
    }

    [TestMethod]
    public void Items_WithoutATextSelector_UseTheItemsOwnText()
    {
        var people = new[] { new Person("Ann"), new Person("Bo") };

        var combo = new ComboBox().Items(people);
        combo.SelectedIndex = 1;
        Assert.AreEqual("person Bo", combo.SelectedText);

        var list = new ListBox().Items(people, person => person.Name);
        list.SelectedIndex = 0;
        Assert.AreEqual("Ann", list.SelectedText, "a selector still decides the text when given");
    }

    [TestMethod]
    public void TryGetContainer_FindsARealizedItemAndReportsAnUnrealizedOne()
    {
        var box = new ListBox { Width = WIDTH, Height = HEIGHT };
        box.ItemsSource = ItemsView.Create(Enumerable.Range(0, 200).Select(index => "Item " + index).ToArray());
        Layout(box);

        Assert.IsTrue(box.TryGetContainer(0, out var container));
        Assert.AreEqual(0, container.Index);
        Assert.AreEqual("Item 0", container.Item);

        Assert.IsFalse(box.TryGetContainer(199, out _), "an item scrolled out of view has no container");
    }

    [TestMethod]
    public void GridView_TryGetContainer_ReachesTheCellAColumnBuilt()
    {
        var grid = new GridView { Width = WIDTH, Height = HEIGHT };
        grid.ItemsSource = ItemsView.Create(Enumerable.Range(0, 100).Select(index => "Item " + index).ToArray());
        grid.SetColumns(
        [
            new GridViewColumn<string>
            {
                Header = "Text",
                Width = 200,
                CellTemplate = new DelegateTemplate<string>(
                    build: context => new TextBlock().Register(context, "Label"),
                    bind: (view, item, _, _) => ((TextBlock)view).Text = item),
            },
        ]);
        grid.Measure(new Size(WIDTH, HEIGHT));
        grid.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        Assert.IsTrue(grid.TryGetContainer(1, out var row));
        Assert.IsTrue(row.TryGetCell(0, out var view, out var cellContext));
        Assert.AreEqual("Item 1", ((TextBlock)view).Text);
        Assert.AreSame(view, cellContext.Get<TextBlock>("Label"));
        Assert.IsFalse(row.TryGetCell(1, out _, out _));
    }

    [TestMethod]
    public void NavigationList_PrepareContainer_RunsForEachRow()
    {
        var seen = new List<string>();
        var list = new NavigationList { Width = WIDTH, Height = HEIGHT };
        list.ItemsSource = ItemsView.Create(new[] { "Home", "Settings" });
        list.PrepareContainer<string>((container, item, index, _) =>
        {
            Assert.AreEqual(index, container.Index);
            seen.Add(item);
        });

        list.Measure(new Size(WIDTH, HEIGHT));
        list.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        CollectionAssert.AreEquivalent(new[] { "Home", "Settings" }, seen.Distinct().ToArray());
    }

    [TestMethod]
    [DoNotParallelize]
    public void ComboBox_PrepareContainer_ReachesTheDropDownList()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var seen = new List<string>();
            var window = HeadlessWindow.Create(400, 300);
            var combo = new ComboBox { Width = 160 }.Items(["User", "Admin", "Guest"]);
            combo.PrepareContainer<string>((_, item, _, _) => seen.Add(item));
            window.Content = new StackPanel().Children(combo);
            window.PerformLayout();
            Assert.IsEmpty(seen, "the hook waits for the drop-down list to exist");

            combo.IsDropDownOpen = true;
            window.PerformLayout();
            window.PerformLayout();
            combo.IsDropDownOpen = false;

            CollectionAssert.Contains(seen, "User");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void ComboBox_ControlTemplate_ProjectsTheSelectedItemView()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        ContentPresenter? presenter = null;
        var window = HeadlessWindow.Create();
        var combo = new ComboBox { ItemsSource = ItemsView.Create(new[] { "Alpha", "Beta" }) };
        combo.SelectedItemTemplate<string, TextBlock>(
            build: _ => new TextBlock(),
            bind: (label, item) => label.Text = item);
        combo.Template = new DelegateControlTemplate<ComboBox>((_, _) =>
        {
            presenter = new ContentPresenter { ContentSource = ComboBox.SelectedItemViewProperty };
            return new Border { Width = 150, Height = 30, Child = presenter };
        });
        window.Content = new StackPanel().Children(combo);
        window.PerformLayout();

        Assert.IsNull(combo.SelectedItemView, "nothing is selected yet");

        combo.SelectedIndex = 1;
        window.PerformLayout();

        var view = combo.SelectedItemView as TextBlock;
        Assert.IsNotNull(view);
        Assert.AreEqual("Beta", view.Text);
        Assert.AreSame(presenter, view.Parent, "the presenter hosts the view under a control template");

        combo.Template = null;
        Assert.AreSame(combo, view.Parent, "without a template the ComboBox hosts it again");
    }

    [TestMethod]
    public void TemplateInstance_SurvivesAScaleChangeUnlessTheTemplateAsksForARebuild()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        Assert.AreEqual(1, BuildsAcrossAScaleChange(rebuild: false), "the default keeps the tree and what its parts hold");
        Assert.AreEqual(2, BuildsAcrossAScaleChange(rebuild: true));
    }

    private static int BuildsAcrossAScaleChange(bool rebuild)
    {
        int builds = 0;
        var template = new DelegateControlTemplate<ContentControl>((_, _) =>
        {
            builds++;
            return new Border();
        })
        {
            RebuildOnDpiChange = rebuild,
        };

        var window = HeadlessWindow.Create();
        window.Content = new ContentControl { Template = template };
        window.PerformLayout();

        window.SetDpi(144);
        window.PerformLayout();
        return builds;
    }

    private static void Layout(ListBox box)
    {
        box.Measure(new Size(WIDTH, HEIGHT));
        box.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }
}
