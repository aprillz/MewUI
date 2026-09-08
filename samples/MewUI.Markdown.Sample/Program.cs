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
        new MarkdownViewer()
            .Ref(out var viewer)
            .FontSize(16)
            .Padding(new Thickness(16))
            .CornerRadius(0)
            .BaseUri(new Uri("https://example.test/docs/"))
            .ImageResolver(new DemoImageResolver())
            .OnLinkRequested(link => status.Text = $"Link: {link.Url} | Resolved: {link.ResolvedUri} | Source: {link.SourceStart}+{link.SourceLength}");

        void SelectCase(ReviewCase entry)
        {
            title.Text(entry.Name);
            description.Text(entry.Notes);
            source.Text(entry.Markdown);
            viewer.Markdown(entry.Markdown);
            status.Text($"{entry.Name} | {entry.Markdown.Length:N0} characters");
        }

        string NavigationLabel(ReviewCase entry)
        {
            int separator = entry.Name.IndexOf(' ');
            return separator >= 0 ? entry.Name[(separator + 1)..] : entry.Name;
        }

        new Button()
            .Ref(out var apply)
            .Content(new TextBlock().Text("Render source"))
            .OnClick(handler: () => { viewer.Markdown(source.Text); status.Text("Source applied"); });
        var gfm = new Button().Content(new TextBlock().Text("GFM: on"));
        bool enabled = true;
        gfm.OnClick(() =>
        {
            enabled = !enabled;
            viewer.Options(new MarkdownOptions
            {
                UsePipeTables = enabled,
                UseTaskLists = enabled,
                UseAutoLinks = enabled,
                UseStrikethrough = enabled,
                UseInserted = enabled,
                UseMarked = enabled
            });
            ((TextBlock)gfm.Content!).Text(enabled ? "GFM: on" : "GFM: off");
        });
        new StackPanel()
            .Ref(out var toolbar)
            .Orientation(Orientation.Horizontal)
            .Spacing(8)
            .Children(apply, gfm);
        new StackPanel()
            .Ref(out var header)
            .Spacing(6)
            .Margin(new Thickness(0, 0, 0, 10))
            .Children(title, description, toolbar);
        var panes = new Grid();
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Stars(1) });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Stars(1.5) });
        new DockPanel()
            .Ref(out var sourcePane)
            .Spacing(6)
            .Margin(new Thickness(0, 0, 10, 0))
            .Children(
                new TextBlock()
                    .Text("MARKDOWN SOURCE")
                    .FontWeight(FontWeight.Bold)
                    .DockTop(),

                source
            );
        new DockPanel()
            .Ref(out var previewPane)
            .Spacing(6)
            .Children(
                new TextBlock()
                    .Text("MEWUI RENDERED OUTPUT")
                    .FontWeight(FontWeight.Bold)
                    .DockTop(),

                viewer
            )
            .Column(1);
        panes.Children(sourcePane, previewPane);
        new DockPanel()
            .Ref(out var content)
            .Padding(14)
            .Spacing(8)
            .Children(header.DockTop(), status.DockBottom(), panes);
        new NavigationView()
            .Ref(out var navigation)
            .PaneWidth(220)
            .Items(
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
                content: _ => content
            )
            .OnSelectionChanged(item =>
            {
                if (item is ReviewCase entry)
                {
                    SelectCase(entry);
                }
            });
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
