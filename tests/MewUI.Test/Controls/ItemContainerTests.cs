using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// An items control wraps each item in an <see cref="ItemContainer"/> only while a container hook is
/// registered, and keeps that container free of the previous item's state.
/// </summary>
[TestClass]
public sealed class ItemContainerTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    [TestMethod]
    public void NoHook_LeavesTheTemplateRootAsTheContainer()
    {
        var box = MakeListBox();
        Layout(box);

        Assert.AreEqual(0, CountRealized<ItemContainer>(box),
            "Applications that do not use the hooks must not pay for a wrapper element.");
        Assert.IsGreaterThan(0, CountRealized<TextBlock>(box));
    }

    [TestMethod]
    public void PrepareContainer_WrapsEachItemAndReportsIndex()
    {
        var seen = new List<(int Index, string Item)>();
        var box = MakeListBox();
        box.PrepareContainer<string>((container, item, index, _) =>
        {
            seen.Add((index, item));
            Assert.AreEqual(index, container.Index);
        });

        Layout(box);

        Assert.IsGreaterThan(0, CountRealized<ItemContainer>(box));
        Assert.IsGreaterThan(0, seen.Count);
        for (int i = 0; i < seen.Count; i++)
        {
            Assert.AreEqual("Item " + seen[i].Index, seen[i].Item);
        }
    }

    [TestMethod]
    public void RegisteringAHookLater_RebuildsTheContainers()
    {
        var box = MakeListBox();
        Layout(box);
        Assert.AreEqual(0, CountRealized<ItemContainer>(box));

        box.PrepareContainer<string>((_, _, _, _) => { });

        // Registering a hook drops the pooled containers, exactly as assigning a new ItemTemplate
        // does. Realization resumes on the next layout that actually re-arranges the range.
        box.ScrollIntoView(40);
        Layout(box);

        Assert.IsGreaterThan(0, CountRealized<ItemContainer>(box));
        Assert.AreEqual(0, CountRealized<TextBlock>(box), "every realized container is now wrapped");
    }

    [TestMethod]
    public void ContainerProperties_DoNotSurviveIntoTheNextItem()
    {
        var box = MakeListBox();
        var menu = new ContextMenu();
        // Only the first item gets a menu. A recycled container must not carry it to another item.
        box.PrepareContainer<string>((container, _, index, _) =>
        {
            if (index == 0)
            {
                container.ContextMenu = menu;
            }
        });
        Layout(box);

        box.ScrollIntoView(60);
        Layout(box);

        VisitRealized(box, (index, element) =>
        {
            if (element is ItemContainer container && index != 0)
            {
                Assert.IsNull(container.ContextMenu, $"index {index} kept the first item's menu");
            }
        });
    }

    [TestMethod]
    public void IsSelected_FollowsTheSelectionWithoutRebinding()
    {
        int binds = 0;
        var box = MakeListBox(onBind: () => binds++);
        box.PrepareContainer<string>((_, _, _, _) => { });
        Layout(box);

        binds = 0;
        box.SelectedIndex = 2;
        Layout(box);

        Assert.AreEqual(0, binds, "selection must not rebind");
        VisitRealized(box, (index, element) =>
        {
            if (element is ItemContainer container)
            {
                Assert.AreEqual(index == 2, container.IsSelected, $"index {index}");
            }
        });

        box.SelectedIndex = 5;
        Layout(box);

        VisitRealized(box, (index, element) =>
        {
            if (element is ItemContainer container)
            {
                Assert.AreEqual(index == 5, container.IsSelected, $"index {index}");
            }
        });
    }

    [TestMethod]
    public void ContextSubscriptions_DoNotAccumulateAcrossBinds()
    {
        var source = new Ticker();
        var box = MakeListBox();
        box.PrepareContainer<string>((_, _, _, context) => context.Subscribe(
            source,
            static (s, h) => s.Ticked += h,
            static (s, h) => s.Ticked -= h,
            () => { }));
        Layout(box);

        int afterFirst = source.HandlerCount;
        Assert.IsGreaterThan(0, afterFirst);

        for (int i = 0; i < 10; i++)
        {
            box.ScrollIntoView((i * 17) % 100);
            Layout(box);
        }

        Assert.AreEqual(afterFirst, source.HandlerCount,
            "one handler per realized container, no matter how often they rebind");
    }

    [TestMethod]
    public void ClearContainer_RunsBeforeTheContainerTakesAnotherItem()
    {
        var cleared = new List<int>();
        var box = MakeListBox();
        box.PrepareContainer<string>((_, _, _, _) => { });
        box.ClearContainer<string>((_, _, index, _) => cleared.Add(index));
        Layout(box);

        Assert.AreEqual(0, cleared.Count, "nothing has been released yet");

        box.ScrollIntoView(80);
        Layout(box);

        Assert.IsGreaterThan(0, cleared.Count);
    }

    private static ListBox MakeListBox(Action? onBind = null)
        => new()
        {
            ItemsSource = ItemsView.Create(Enumerable.Range(0, 100).Select(i => "Item " + i).ToArray()),
            ItemTemplate = new DelegateTemplate<object?>(
                build: _ => new TextBlock(),
                bind: (view, item, _, _) =>
                {
                    onBind?.Invoke();
                    ((TextBlock)view).Text = item as string ?? string.Empty;
                }),
            Width = WIDTH,
            Height = HEIGHT,
        };

    private static void Layout(ListBox box)
    {
        box.Measure(new Size(WIDTH, HEIGHT));
        box.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }

    private static void VisitRealized(ListBox box, Action<int, FrameworkElement> visitor)
        => box.VisitRealizedContainers(visitor);

    private static int CountRealized<T>(ListBox box) where T : FrameworkElement
    {
        int count = 0;
        box.VisitRealizedContainers((_, element) =>
        {
            if (element is T)
            {
                count++;
            }
        });
        return count;
    }

    private sealed class Ticker
    {
        private Action? _ticked;

        public event Action Ticked
        {
            add => _ticked += value;
            remove => _ticked -= value;
        }

        public int HandlerCount => _ticked?.GetInvocationList().Length ?? 0;
    }
}

/// <summary>
/// The container hook reaches every items control and every realization engine, since it is applied
/// by decorating the item template rather than by touching container pooling.
/// </summary>
[TestClass]
public sealed class ItemContainerReachTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    [TestMethod]
    public void ItemsControl_PrepareContainer_WrapsEachItem()
    {
        var seen = new List<int>();
        var control = new ItemsControl
        {
            ItemsSource = ItemsView.Create(Items()),
            ItemTemplate = TextTemplate(),
            Width = WIDTH,
            Height = HEIGHT,
        };
        control.PrepareContainer<string>((container, item, index, _) =>
        {
            seen.Add(index);
            Assert.AreEqual(index, container.Index);
            Assert.AreEqual("Item " + index, item);
        });

        control.Measure(new Size(WIDTH, HEIGHT));
        control.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        Assert.IsGreaterThan(0, seen.Count);
    }

    [TestMethod]
    public void EveryPresenter_RunsTheHook()
    {
        (string Name, Func<ListBox, ListBox> Select)[] presenters =
        [
            ("fixed", static box => box.FixedHeightPresenter()),
            ("variable", static box => box.VariableHeightPresenter()),
            ("stack", static box => box.StackPresenter()),
        ];

        foreach (var presenter in presenters)
        {
            int prepared = 0;
            var box = new ListBox
            {
                ItemsSource = ItemsView.Create(Items()),
                ItemTemplate = TextTemplate(),
                Width = WIDTH,
                Height = HEIGHT,
            };
            presenter.Select(box);
            box.PrepareContainer<string>((_, _, _, _) => prepared++);

            box.Measure(new Size(WIDTH, HEIGHT));
            box.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

            Assert.IsGreaterThan(0, prepared, presenter.Name);
        }
    }

    private static string[] Items() => Enumerable.Range(0, 30).Select(i => "Item " + i).ToArray();

    private static IDataTemplate TextTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new TextBlock(),
            bind: (view, item, _, _) => ((TextBlock)view).Text = item as string ?? string.Empty);
}

/// <summary>
/// The wrapper must be invisible to layout: it adds no inset and does not swallow the alignment the
/// template asked for, or a right-aligned chat bubble would jump to the left once a hook is added.
/// </summary>
[TestClass]
public sealed class ItemContainerLayoutTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    [TestMethod]
    public void WrappingDoesNotMoveOrResizeTheContent()
    {
        var withoutHook = Measure(hooked: false);
        var withHook = Measure(hooked: true);

        Assert.AreEqual(withoutHook.Bounds, withHook.Bounds,
            "the bubble must land in the same place with and without a container hook");
    }

    [TestMethod]
    public void ContentAlignmentSurvivesTheWrapper()
    {
        var right = Measure(hooked: true, alignment: HorizontalAlignment.Right);
        var left = Measure(hooked: true, alignment: HorizontalAlignment.Left);

        Assert.IsGreaterThan(left.Bounds.X, right.Bounds.X,
            "a right-aligned bubble must sit further right than a left-aligned one");
    }

    private static Border Measure(bool hooked, HorizontalAlignment alignment = HorizontalAlignment.Right)
    {
        Border? first = null;
        var box = new ListBox
        {
            ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }),
            ItemTemplate = new DelegateTemplate<object?>(
                build: _ => new Border { Width = 120, Height = 40, Margin = new Thickness(16, 8) },
                bind: (view, _, index, _) =>
                {
                    var border = (Border)view;
                    border.HorizontalAlignment = alignment;
                    if (index == 0)
                    {
                        first = border;
                    }
                }),
            Width = WIDTH,
            Height = HEIGHT,
        };
        if (hooked)
        {
            box.PrepareContainer<string>((_, _, _, _) => { });
        }

        box.Measure(new Size(WIDTH, HEIGHT));
        box.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        Assert.IsNotNull(first);
        return first;
    }
}
