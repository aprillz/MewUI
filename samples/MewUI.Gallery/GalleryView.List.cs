using System.Collections.ObjectModel;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ImageSource iconFolderOpen = ImageSource.FromResource<Program>("Aprillz.MewUI.Gallery.Resources.folder-horizontal-open.png");
    private ImageSource iconFolderClose = ImageSource.FromResource<Program>("Aprillz.MewUI.Gallery.Resources.folder-horizontal.png");
    private ImageSource iconFile = ImageSource.FromResource<Program>("Aprillz.MewUI.Gallery.Resources.document.png");
    
    private FrameworkElement ListsPage()
    {
        var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();

        return CardGrid(
            Card(
                "ListBox",
                new ListBox()
                    .Height(120)
                    .Width(200)
                    .Items(items)
            ),

            Card(
                "ComboBox",
                new StackPanel()
                    .Vertical()
                    .Width(200)
                    .Spacing(8)
                    .Children(
                        new ComboBox()
                            .Items(["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"])
                            .SelectedIndex(1),

                        new ComboBox()
                            .Placeholder("Select an item...")
                            .Items(items),

                        new ComboBox()
                            .Items(items)
                            .SelectedIndex(1)
                            .Disable()
                    )
            ),

            Card(
                "TreeView",
                TreeViewCard()
            ),

            ChatVariableHeightCard()
        );

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

            Label selectedNodeText = null!;

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
                            .StretchMode(ImageStretch.None)
                            .CenterVertical(),
                        new Label()
                            .Register(ctx, "Text")
                            .CenterVertical()
                    ),
                bind: (view, item, _, ctx) =>
                {
                    ctx.Get<Image>("Icon").Source(item.HasChildren ? (treeView.IsExpanded(item) ? iconFolderOpen : iconFolderClose) : iconFile);
                    ctx.Get<Label>("Text").Text(item.Text);
                });

            treeView.Expand(treeItems[0]);
            treeView.Expand(treeItems[0].Children[0]);

            return new DockPanel()
                        .Height(240)
                        .Spacing(6)
                        .Children(
                            new Label()
                                .DockBottom()
                                .Ref(out selectedNodeText)
                                .FontSize(11)
                                .Text("Selected: (none)"),
                            treeView
                        );
        }
    }

    FrameworkElement ChatVariableHeightCard()
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
            Application.Current.Dispatcher?.BeginInvoke(() => ScrollToBottom(), DispatcherPriority.Render);
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

                            new Label()
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
                        .PresenterMode(ItemsPresenterMode.Variable)
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
                                            new Label()
                                                .Register(ctx, "Sender")
                                                .FontSize(10)
                                                .Bold(),
                                            new Label()
                                                .Register(ctx, "Text")
                                                .TextWrapping(TextWrapping.Wrap)
                                        )),
                            bind: (view, msg, _, ctx) =>
                            {
                                var bubble = ctx.Get<Border>("Bubble");
                                var sender = ctx.Get<Label>("Sender");
                                var text = ctx.Get<Label>("Text");

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

sealed record ChatMessage(long Id, string Sender, string Text, bool Mine, DateTimeOffset Time);
