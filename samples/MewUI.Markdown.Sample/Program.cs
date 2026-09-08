using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Markdown;
using Aprillz.MewUI.Text;

Win32Platform.Register();
Direct2DBackend.Register();

Application.Run(new DemoWindow());

public class DemoWindow : Window
{
    public DemoWindow()
    {
    }

    protected override void OnBuild()
    {
        base.OnBuild();

        var status = new TextBlock()
            .Text("Ready")
            .TextWrapping(TextWrapping.Wrap);
        var title = new TextBlock()
            .FontSize(20)
            .FontWeight(FontWeight.Bold);
        var description = new TextBlock().TextWrapping(TextWrapping.Wrap);
        var source = new MultiLineTextBox()
            .FontFamily("Consolas")
            .FontSize(13);
        var viewer = new MarkdownViewer()
            .FontSize(16)
            .Padding(new Thickness(16))
            .CornerRadius(0)
            .BaseUri(new Uri("https://example.test/docs/"))
            .ImageResolver(new DemoImageResolver());
        viewer.LinkRequested += link => status.Text = $"Link: {link.Url} | Resolved: {link.ResolvedUri} | Source: {link.SourceStart}+{link.SourceLength}";

        void SelectCase(ReviewCase entry)
        {
            title.Text = entry.Name;
            description.Text = entry.Notes;
            source.Text = entry.Markdown;
            viewer.Markdown = entry.Markdown;
            status.Text = $"{entry.Name} | {entry.Markdown.Length:N0} characters";
        }

        string NavigationLabel(ReviewCase entry)
        {
            int separator = entry.Name.IndexOf(' ');
            return separator >= 0 ? entry.Name[(separator + 1)..] : entry.Name;
        }

        var apply = new Button().Content(new TextBlock().Text("Render source"));
        apply.OnClick(handler: () => { viewer.Markdown(source.Text); status.Text("Source applied"); });
        var gfm = new Button().Content(new TextBlock().Text("GFM: on"));
        bool enabled = true;
        gfm.Click += () =>
        {
            enabled = !enabled;
            viewer.Options = new MarkdownOptions
            {
                UsePipeTables = enabled,
                UseTaskLists = enabled,
                UseAutoLinks = enabled,
                UseStrikethrough = enabled,
                UseInserted = enabled,
                UseMarked = enabled
            };
            ((TextBlock)gfm.Content!).Text = enabled ? "GFM: on" : "GFM: off";
        };
        var toolbar = new StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8);
        toolbar.AddRange(apply, gfm);
        var header = new StackPanel()
            .Spacing(6)
            .Margin(new Thickness(0, 0, 0, 10));
        header.AddRange(title, description, toolbar);
        var panes = new Grid();
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Stars(1) });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Stars(1.5) });
        var sourcePane = new DockPanel()
            .Spacing(6)
            .Margin(new Thickness(0, 0, 10, 0));
        sourcePane.Add(new TextBlock()
            .Text("MARKDOWN SOURCE")
            .FontWeight(FontWeight.Bold).DockTop());
        sourcePane.Add(source);
        var previewPane = new DockPanel().Spacing(6);
        previewPane.Add(new TextBlock()
            .Text("MEWUI RENDERED OUTPUT")
            .FontWeight(FontWeight.Bold).DockTop());
        previewPane.Add(viewer);
        Grid.SetColumn(previewPane, 1);
        panes.AddRange(sourcePane, previewPane);
        var content = new DockPanel()
            .Padding(14)
            .Spacing(8);
        content.Add(header.DockTop());
        content.Add(status.DockBottom());
        content.Add(panes);
        var navigation = new NavigationView().PaneWidth(220);
        navigation.Items(
            ReviewCases.All,
            NavigationLabel,
            icon: entry => new Border()
                .Size(24)
                .CornerRadius(12)
                .Center()
                .WithTheme((t, c) => c.Background(t.Palette.ControlBackground))
                .Child(
                    new TextBlock()
                        .Text(entry.Name[..2])
                        .LineBoxTrim(LineBoxTrim.CapAndBaseline)
                        .Bold()
                        .Center()
                ),
            content: _ => content);
        navigation.SelectionChanged += item =>
        {
            if (item is ReviewCase entry)
            {
                SelectCase(entry);
            }
        };
        navigation.SelectedIndex = 0;


        this.Resizable(1440, 940)
            .Padding(0)
            .Title("MewUI Markdown - Comprehensive Visual Review")
            .Content(new DockPanel()
                .Children(
                    new Border()
                        .WithTheme((t, c) => c
                            .BorderBrush(t.Palette.ControlBorder)
                            .BorderThickness(new Thickness(0, 0, 0, t.Metrics.ControlBorderThickness)))
                        .Child(
                            new DockPanel()
                                .Margin(8)
                                .Children(
                                    new StackPanel()
                                        .DockRight()
                                        .Spacing(10)
                                        .Horizontal()
                                        .Children(
                                            new Button()
                                                .Width(60)
                                                .CenterVertical()
                                                .Content("Light")
                                                .OnClick(() => Application.Current.SetTheme(ThemeVariant.Light)),

                                            new Button()
                                                .Width(60)
                                                .CenterVertical()
                                                .Content("Dark")
                                                .OnClick(() => Application.Current.SetTheme(ThemeVariant.Dark))
                                        ),
                                    new TextBlock()
                                        .Text("MewUI Markdown")
                                        .Bold()
                                        .WithTheme((t, c) => c.FontSize(t.Metrics.FontSizeExtraLarge))
                                )
                        )
                        .DockTop(),

                    navigation
                ));
    }
}
