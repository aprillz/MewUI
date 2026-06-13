using System.Collections.ObjectModel;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ImageSource iconFolderOpen = ImageSource.FromFile(CombineBaseDirectory("Resources/folder-horizontal-open.png"));
    private ImageSource iconFolderClose = ImageSource.FromFile(CombineBaseDirectory("Resources/folder-horizontal.png"));
    private ImageSource iconFile = ImageSource.FromFile(CombineBaseDirectory("Resources/document.png"));

    private FrameworkElement ListsPage()
    {
        var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();

        ObservableCollection<DemoUser> users =
        [
            new DemoUser(1, "Alice", "Admin", IsOnline: true),
            new DemoUser(2, "Bob", "Editor", IsOnline: false),
            new DemoUser(3, "Charlie", "Viewer", IsOnline: true),
            new DemoUser(4, "Diana", "Editor", IsOnline: true),
            new DemoUser(5, "Eve", "Viewer", IsOnline: false),
            new DemoUser(6, "Frank", "Admin", IsOnline: true),
            new DemoUser(7, "Grace", "Viewer", IsOnline: true),
            new DemoUser(8, "Heidi", "Editor", IsOnline: false),
            new DemoUser(9, "Ivan", "Viewer", IsOnline: true),
            new DemoUser(10, "Judy", "Admin", IsOnline: true),
            new DemoUser(11, "Mallory", "Editor", IsOnline: false),
            new DemoUser(12, "Niaj", "Viewer", IsOnline: true),
            new DemoUser(13, "Olivia", "Viewer", IsOnline: true),
            new DemoUser(14, "Peggy", "Editor", IsOnline: false),
            new DemoUser(15, "Sybil", "Admin", IsOnline: true),
        ];

        return CardGrid(
            Card(
                "ListBox",
                new ListBox()
                    .Height(120)
                    .Width(200)
                    .Items(items)
            ),

            Card(
                "ListBox (class items)",
                ListBoxClassItemsCard()
            ),

            Card(
                "ListBox (ItemsView + ItemTemplate)",
                ListBoxItemsViewTemplateCard()
            ),

            Card(
                "TreeView",
                TreeViewCard()
            ),

            Card(
                "TreeView (Async children)",
                AsyncTreeViewCard()
            ),

            Card(
                "ListBox (WrapPresenter)",
                ListBoxWrapPresenterCard()
            ),

            Card(
                "ItemsControl (WrapPresenter)",
                ItemsControlWrapPresenterCard()
            ),

            ChatVariableHeightCard()
        );

        FrameworkElement ListBoxWrapPresenterCard()
        {
            var colors = new[]
            {
                Color.FromRgb(230, 100, 100), Color.FromRgb(100, 180, 230),
                Color.FromRgb(100, 200, 130), Color.FromRgb(220, 180, 80),
                Color.FromRgb(180, 120, 220), Color.FromRgb(240, 140, 100),
                Color.FromRgb(130, 200, 200), Color.FromRgb(200, 140, 170),
            };
            var wrapItems = Enumerable.Range(0, 1000).Select(i => $"Tile {i + 1}").ToArray();

            var selectedText = new TextBlock { Text = "Selected: (none)" };

            var listBox = new ListBox()
                .ItemPadding(new(2))
                .Height(240)
                .Width(402)
                .WrapPresenter(80, 80)
                .Items(wrapItems)
                .ItemTemplate(new DelegateTemplate<string>(
                    build: ctx => new Border()
                        .Register(ctx, "Bg")
                        .CornerRadius(6)
                        .Child(new TextBlock()
                            .Register(ctx, "Label")
                            .Center()
                            .FontSize(11)),
                    bind: (view, item, index, ctx) =>
                    {
                        ctx.Get<Border>("Bg").Background(colors[index % colors.Length].WithAlpha(180));
                        ctx.Get<TextBlock>("Label").Text(item ?? "");
                    }))
                .OnSelectionChanged(obj =>
                {
                    selectedText.Text = obj is string s ? $"Selected: {s}" : "Selected: (none)";
                });

            return new StackPanel()
                .Vertical()
                .Spacing(6)
                .Children(
                    listBox,
                    selectedText);
        }

        FrameworkElement ItemsControlWrapPresenterCard()
        {
            var colors = new[]
            {
                Color.FromRgb(230, 100, 100), Color.FromRgb(100, 180, 230),
                Color.FromRgb(100, 200, 130), Color.FromRgb(220, 180, 80),
                Color.FromRgb(180, 120, 220), Color.FromRgb(240, 140, 100),
                Color.FromRgb(130, 200, 200), Color.FromRgb(200, 140, 170),
            };
            var wrapItems = Enumerable.Range(0, 4800).Select(i => $"Tile {i + 1}").ToArray();

            var itemsControl = new ItemsControl()
                .ItemPadding(new(2))
                .Height(240)
                .Width(402)
                .WrapPresenter(80, 80)
                .ItemsSource(ItemsView.Create(wrapItems))
                .ItemTemplate(new DelegateTemplate<string>(
                    build: ctx => new Border()
                        .Register(ctx, "Bg")
                        .CornerRadius(6)
                        .Child(new TextBlock()
                            .Register(ctx, "Label")
                            .Center()
                            .FontSize(11)),
                    bind: (view, item, index, ctx) =>
                    {
                        ctx.Get<Border>("Bg").Background(colors[index % colors.Length].WithAlpha(120));
                        ctx.Get<TextBlock>("Label").Text(item ?? "");
                    }));

            return itemsControl;
        }

        FrameworkElement ListBoxClassItemsCard()
        {
            TextBlock selectedText = null!;

            var listBox = new ListBox()
                .Height(160)
                .Width(240)
                .Items(users, u => $"{u.Name} ({u.Role})", keySelector: u => u.Id)
                .OnSelectionChanged(obj =>
                {
                    var u = obj as DemoUser;
                    selectedText.Text = u == null ? "Selected: (none)" : $"Selected: {u.Name} ({u.Role})";
                });

            return new DockPanel()
                .Spacing(6)
                .Children(
                    new TextBlock()
                        .DockBottom()
                        .Ref(out selectedText)
                        .FontSize(11)
                        .Text("Selected: (none)"),
                    listBox
                );
        }

        FrameworkElement ListBoxItemsViewTemplateCard()
        {
            var view = new ItemsView<DemoUser>(
                users,
                textSelector: u => u.Name,
                keySelector: u => u.Id);

            var nextId = users.Max(u => u.Id) + 1;

            ListBox listBox = null!;
            TextBlock selectedText = null!;

            var add = new Button()
                .Content("Add")
                .OnClick(() =>
                {
                    var id = nextId++;
                    users.Add(new DemoUser(id, $"User {id}", "Viewer", IsOnline: id % 2 == 0));
                });

            var remove = new Button()
                .Content("Remove")
                .OnClick(() =>
                {
                    if (users.Count > 0)
                    {
                        users.RemoveAt(users.Count - 1);
                    }
                });

            var panel = new DockPanel()
                .Spacing(6)
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .Children(add, remove),
                    new TextBlock()
                        .DockBottom()
                        .Ref(out selectedText)
                        .FontSize(11)
                        .Text("Selected: (none)"),
                    new ListBox()
                        .Ref(out listBox)
                        .Height(170)
                        .Width(260)
                        .ItemHeight(40)
                        .ItemsSource(view)
                        .OnSelectionChanged(obj =>
                        {
                            var u = obj as DemoUser;
                            selectedText.Text = u == null ? "Selected: (none)" : $"Selected: {u.Name} ({u.Role})";
                        })
                );

            listBox.ItemTemplate<DemoUser>(
                build: ctx => new Border()
                    .Padding(6, 4)
                    .Child(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Ellipse()
                                    .Register(ctx, "Dot")
                                    .Size(10, 10)
                                    .CenterVertical(),
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(0)
                                    .Children(
                                        new TextBlock()
                                            .Register(ctx, "Name")
                                            .FontSize(12)
                                            .Bold(),
                                        new TextBlock()
                                            .Register(ctx, "Role")
                                            .FontSize(10)
                                    ))),
                bind: (root, u, _, ctx) =>
                {
                    ctx.Get<TextBlock>("Name").Text = u.Name;
                    ctx.Get<TextBlock>("Role").Text = u.Role;

                    var dot = ctx.Get<Ellipse>("Dot");
                    dot.WithTheme((t, b) =>
                    {
                        b.Fill(u.IsOnline ? t.Palette.Accent : t.Palette.ControlBorder);
                        b.Stroke((u.IsOnline ? t.Palette.Accent : t.Palette.ControlBorder).Lerp(t.Palette.WindowText, 0.5));
                    });
                });

            return panel;
        }

        FrameworkElement TreeViewCard()
        {
            TreeViewNode[] Get(params string[] texts) => texts.Select(x => new TreeViewNode(x)).ToArray();

            var treeItems = new[]
            {
                new TreeViewNode("src",
                [
                    new TreeViewNode("MewUI",
                    [
                        new TreeViewNode("Controls",Get("Button.cs", "TextBox.cs", "TreeView.cs"))
                    ]),
                    new TreeViewNode("Rendering",
                    [
                        new TreeViewNode("Gdi", Get("GdiMeasurementContext.cs","GdiGrapchisContext.cs","GdiGraphicsFactory.cs")),
                        new TreeViewNode("Direct2D", Get("Direct2DMeasurementContext.cs","Direct2DGrapchisContext.cs","Direct2DGraphicsFactory.cs")),
                        new TreeViewNode("OpenGL", Get("OpenGLMeasurementContext.cs","OpenGLGrapchisContext.cs","OpenGLGraphicsFactory.cs")),
                    ])
                ]),
                new TreeViewNode("README.md"),
                new TreeViewNode("assets",
                [
                    new TreeViewNode("logo.png"),
                    new TreeViewNode("icon.ico")
                ])
            };

            TextBlock selectedNodeText = null!;

            var treeView = new TreeView()
                .Width(200)
                .ItemsSource(treeItems)
                .ExpandTrigger(TreeViewExpandTrigger.DoubleClickNode)
                .OnSelectionChanged(obj =>
                {
                    var n = obj as TreeViewNode;
                    selectedNodeText.Text = n == null ? "Selected: (none)" : $"Selected: {n.Text}";
                });

            treeView.ItemTemplate<TreeViewNode>(
                build: ctx => new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        new Image()
                            .Register(ctx, "Icon")
                            .Size(16, 16)
                            .StretchMode(Stretch.None)
                            .CenterVertical(),
                        new TextBlock()
                            .Register(ctx, "Text")
                            .CenterVertical()
                    ),
                bind: (view, item, _, ctx) =>
                {
                    ctx.Get<Image>("Icon").Source(item.HasChildren ? (treeView.IsExpanded(item) ? iconFolderOpen : iconFolderClose) : iconFile);
                    ctx.Get<TextBlock>("Text").Text(item.Text);
                });

            treeView.Expand(treeItems[0]);
            treeView.Expand(treeItems[0].Children[0]);

            return new DockPanel()
                        .Height(240)
                        .Spacing(6)
                        .Children(
                            new TextBlock()
                                .DockBottom()
                                .Ref(out selectedNodeText)
                                .FontSize(11)
                                .Text("Selected: (none)"),
                            treeView
                        );
        }

        FrameworkElement AsyncTreeViewCard()
        {
            LazyTreeNode CreateFolder(string name, int depth) =>
                new(name, depth, canLoadChildren: true);

            void CancelAndClear(LazyTreeNode node)
            {
                foreach (var child in node.Children)
                {
                    CancelAndClear(child);
                }

                node.LoadCancellation?.Cancel();
                node.IsLoading.Value = false;
                node.IsLoaded = false;
                node.Children.Clear();
            }

            var remoteRoot = CreateFolder("remote", depth: 0);
            var treeView = new TreeView()
                .Width(240)
                .Items(
                    [remoteRoot],
                    node => node.Children,
                    textSelector: node => node.Name,
                    keySelector: node => node,
                    isExpandableSelector: node =>
                        node.Children.Count > 0 || node.CanLoadChildren)
                .OnExpanding(async e =>
                {
                    if (e.Item is not LazyTreeNode node ||
                        !node.CanLoadChildren ||
                        node.IsLoaded ||
                        node.LoadCancellation != null)
                    {
                        return;
                    }

                    var cancellation = new CancellationTokenSource();
                    node.LoadCancellation = cancellation;
                    node.IsLoading.Value = true;
                    try
                    {
                        await Task.Delay(1200 + node.Depth * 300, cancellation.Token);

                        if (node.Depth < 3)
                        {
                            for (int i = 1; i <= 6; i++)
                            {
                                node.Children.Add(CreateFolder(
                                    $"folder-{node.Depth + 1}-{i:00}",
                                    node.Depth + 1));
                            }
                        }

                        for (int i = 1; i <= 18; i++)
                        {
                            node.Children.Add(new LazyTreeNode(
                                $"file-{node.Depth}-{i:00}.dat",
                                node.Depth + 1,
                                canLoadChildren: false));
                        }

                        node.IsLoaded = true;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        if (ReferenceEquals(node.LoadCancellation, cancellation))
                        {
                            node.LoadCancellation = null;
                        }

                        node.IsLoading.Value = false;
                        cancellation.Dispose();
                    }
                })
                .OnCollapsing(e =>
                {
                    if (e.Item is LazyTreeNode node)
                    {
                        CancelAndClear(node);
                    }
                });

            treeView.ItemTemplate<LazyTreeNode>(
                build: ctx => new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        new Grid()
                            .Width(16).Height(16)
                            .CenterVertical()
                            .Children(
                                new Image()
                                    .Register(ctx, "Icon")
                                    .Size(16, 16)
                                    .StretchMode(Stretch.None),
                                new ProgressRing()
                                    .Register(ctx, "Loading")
                                    .Size(16, 16)
                                    .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                            ),

                        new TextBlock()
                            .Register(ctx, "Text")
                            .CenterVertical()),
                bind: (view, item, index, ctx) =>
                {
                    bool isFolder = item.Children.Count > 0 || item.CanLoadChildren;

                    var icon = ctx.Get<Image>("Icon");
                    icon.Source(isFolder ? (treeView.ItemsSource.GetIsExpanded(index) ? iconFolderOpen : iconFolderClose) : iconFile);

                    ctx.Get<TextBlock>("Text").Text(item.Name);

                    var loading = ctx.Get<ProgressRing>("Loading");
                    icon.BindIsVisible(item.IsLoading, isLoading => !isLoading);
                    loading.Bind(ProgressRing.IsActiveProperty, item.IsLoading);
                    loading.BindIsVisible(item.IsLoading);
                });

            return new DockPanel()
                .Height(240)
                .Children(treeView);
        }
    }

    private FrameworkElement ChatVariableHeightCard()
    {
        long nextId = 1;
        var messages = new ObservableCollection<ChatMessage>();

        void Add(bool mine, string sender, string text)
            => messages.Add(new ChatMessage(nextId++, sender, text ?? string.Empty, mine, DateTimeOffset.Now));

        void Prepend(int count)
        {
            var start = nextId;
            // Insert in reverse so the final order is chronological.
            for (int i = count - 1; i >= 0; i--)
            {
                var text = SampleChatText((int)(start + i));
                messages.Insert(0, new ChatMessage(nextId++, "Bot", text, Mine: false, DateTimeOffset.Now.AddMinutes(-(count - i))));
            }
        }

        // Initial content with varying lengths to demonstrate wrapping + variable-height virtualization.
        Add(false, "Bot", "This is a chat-style ItemsControl sample.\nVariable-height virtualization is enabled.");
        Add(true, "You", "Try scrolling, then click 'Prepend 20' to insert older messages at the top.");
        Add(false, "Bot", SampleChatText(1));
        Add(true, "You", SampleChatText(2));
        Add(false, "Bot", SampleChatText(3));
        Add(false, "Bot", SampleChatText(4));
        Add(true, "You", SampleChatText(5));
        for (int i = 0; i < 40; i++)
        {
            Add(i % 3 == 0, i % 3 == 0 ? "You" : "Bot", SampleChatText(10 + i));
        }

        var view = new ItemsView<ChatMessage>(
            messages,
            textSelector: m => m.Text,
            keySelector: m => m.Id);

        ItemsControl list = null!;
        var input = new ObservableValue<string>(string.Empty);

        void ScrollToBottom()
        {
            if (list == null)
            {
                return;
            }

            list.ScrollIntoView(messages.Count - 1);
        }

        void Send()
        {
            var text = (input.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            messages.Add(new ChatMessage(nextId++, "You", text, Mine: true, DateTimeOffset.Now));
            input.Value = string.Empty;
            ScrollToBottom();
        }

        var status = new ObservableValue<string>(string.Empty);
        void UpdateStatus() => status.Value = $"Messages: {messages.Count}   OffsetY: {list.VerticalOffset:0.#}";

        return Card(
            "ItemsControl (chat / variable height)",
            new DockPanel()
                .MinWidth(640)
                .MaxWidth(960)
                .Height(320)
                .Spacing(6)
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new Button()
                                .Content("Prepend 20")
                                .OnClick(() =>
                                {
                                    Prepend(20);
                                    UpdateStatus();
                                }),

                            new Button()
                                .Content("To bottom")
                                .OnClick(() =>
                                {
                                    ScrollToBottom();
                                    UpdateStatus();
                                }),

                            new TextBlock()
                                .BindText(status)
                                .FontSize(11)
                                .CenterVertical()
                        ),

                    new DockPanel()
                        .DockBottom()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .DockRight()
                                .Content("Send")
                                .DockRight()
                                .OnClick(() =>
                                {
                                    Send();
                                    UpdateStatus();
                                }),

                            new TextBox()
                                .Placeholder("Type a message...")
                                .BindText(input)
                                .OnKeyDown(e =>
                                {
                                    if (e.Key == Key.Enter)
                                    {
                                        e.Handled = true;
                                        Send();
                                        //UpdateStatus();
                                    }
                                })
                        ),

                    new ItemsControl()
                        .Ref(out list)
                        .HorizontalAlignment(HorizontalAlignment.Stretch)
                        .VariableHeightPresenter()
                        .WithTheme((t, _) => list
                            .BorderBrush(t.Palette.ControlBorder)
                            .BorderThickness(t.Metrics.ControlBorderThickness))
                        .ItemsSource(view)
                        .ItemPadding(Thickness.Zero)
                        .ItemTemplate(new DelegateTemplate<ChatMessage>(
                            build: ctx => new Border()
                                .Register(ctx, "Bubble")
                                .BorderThickness(1)
                                .CornerRadius(10)
                                .Margin(16, 8)
                                .Padding(10, 6)
                                .Child(
                                    new StackPanel()
                                        .Vertical()
                                        .Spacing(2)
                                        .Children(
                                            new TextBlock()
                                                .Register(ctx, "Sender")
                                                .FontSize(10)
                                                .Bold(),
                                            new TextBlock()
                                                .Register(ctx, "Text")
                                                .TextWrapping(TextWrapping.Wrap)
                                        )),
                            bind: (view, msg, _, ctx) =>
                            {
                                var bubble = ctx.Get<Border>("Bubble");
                                var sender = ctx.Get<TextBlock>("Sender");
                                var text = ctx.Get<TextBlock>("Text");

                                sender.Text = msg.Sender;
                                sender.IsVisible = !msg.Mine;
                                text.Text = msg.Text;

                                bubble.HorizontalAlignment = msg.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

                                bubble.WithTheme((t, b) =>
                                {
                                    if (msg.Mine)
                                    {
                                        b.Background(t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.85));
                                        b.BorderBrush(t.Palette.Accent.Lerp(t.Palette.WindowText, 0.15));
                                    }
                                    else
                                    {
                                        b.Background(t.Palette.ControlBackground);
                                        b.BorderBrush(t.Palette.ControlBorder);
                                    }
                                });

                                text.WithTheme((t, l) =>
                                {
                                    l.Foreground(msg.Mine ? t.Palette.WindowText : t.Palette.WindowText);
                                });
                            }))
                        .Apply(_ => UpdateStatus())
                        .Apply(_ => ScrollToBottom())
                ),
            minWidth: 420);

        static string SampleChatText(int seed)
        {
            return (seed % 7) switch
            {
                0 => "Short message.",
                1 => "A bit longer message that should wrap depending on the width of the viewport.",
                2 => "Multiline:\n- line 1\n- line 2\n- line 3",
                3 => "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
                4 => "Numbers: 123 456 7890. Symbols: !@#$%^&*()",
                5 => "Wrapping test: The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.",
                _ => "Edge-case: superlongword_superlongword_superlongword_superlongword_superlongword"
            };
        }
    }
}

sealed record DemoUser(int Id, string Name, string Role, bool IsOnline);
sealed record ChatMessage(long Id, string Sender, string Text, bool Mine, DateTimeOffset Time);

sealed class LazyTreeNode(string name, int depth, bool canLoadChildren)
{
    public string Name { get; } = name;
    public int Depth { get; } = depth;
    public bool CanLoadChildren { get; } = canLoadChildren;
    public ObservableCollection<LazyTreeNode> Children { get; } = [];
    public bool IsLoaded { get; set; }
    public ObservableValue<bool> IsLoading { get; } = new(false);
    public CancellationTokenSource? LoadCancellation { get; set; }
}
