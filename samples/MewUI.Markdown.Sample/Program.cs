using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Markdown;

Win32Platform.Register();
Direct2DBackend.Register();
var status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap };
var title = new TextBlock { FontSize = 20, FontWeight = FontWeight.Bold };
var description = new TextBlock { TextWrapping = TextWrapping.Wrap };
var source = new MultiLineTextBox { FontFamily = "Consolas", FontSize = 13 };
var viewer = new MarkdownViewer
{
    FontSize = 16, Padding = new Thickness(16), CornerRadius = 0,
    BaseUri = new Uri("https://example.test/docs/"), ImageResolver = new DemoImageResolver()
};
viewer.LinkRequested += link => status.Text = $"Link: {link.Url} | Resolved: {link.ResolvedUri} | Source: {link.SourceStart}+{link.SourceLength}";
void SelectCase(ReviewCase entry)
{
    title.Text = entry.Name;
    description.Text = entry.Notes;
    source.Text = entry.Markdown;
    viewer.Markdown = entry.Markdown;
    status.Text = $"{entry.Name} | {entry.Markdown.Length:N0} characters";
}
var navigation = new StackPanel { Spacing = 4 };
foreach (var entry in ReviewCases.All)
{
    var selected = entry;
    var button = new Button { Content = new TextBlock { Text = entry.Name }, HorizontalAlignment = HorizontalAlignment.Stretch };
    button.Click += () => SelectCase(selected);
    navigation.Add(button);
}
var apply = new Button { Content = new TextBlock { Text = "Render source" } };
apply.Click += () => { viewer.Markdown = source.Text; status.Text = "Source applied"; };
var light = new Button { Content = new TextBlock { Text = "Light" } };
light.Click += () => Application.Current.SetTheme(ThemeVariant.Light);
var dark = new Button { Content = new TextBlock { Text = "Dark" } };
dark.Click += () => Application.Current.SetTheme(ThemeVariant.Dark);
var gfm = new Button { Content = new TextBlock { Text = "GFM: on" } };
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
var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
toolbar.AddRange(apply, light, dark, gfm);
var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
header.AddRange(title, description, toolbar);
var panes = new Grid();
panes.ColumnDefinitions.Add(new ColumnDefinition { Width = 200 });
panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Stars(1) });
panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Stars(1.5) });
var cases = new ScrollViewer { Content = navigation, Margin = new Thickness(0, 0, 8, 0) };
var sourcePane = new DockPanel { Spacing = 6, Margin = new Thickness(0, 0, 10, 0) };
sourcePane.Add(new TextBlock { Text = "MARKDOWN SOURCE", FontWeight = FontWeight.Bold }.DockTop());
sourcePane.Add(source);
var previewPane = new DockPanel { Spacing = 6 };
previewPane.Add(new TextBlock { Text = "MEWUI RENDERED OUTPUT", FontWeight = FontWeight.Bold }.DockTop());
previewPane.Add(viewer);
Grid.SetColumn(sourcePane, 1);
Grid.SetColumn(previewPane, 2);
panes.AddRange(cases, sourcePane, previewPane);
var root = new DockPanel { Padding = 14, Spacing = 8 };
root.Add(header.DockTop());
root.Add(status.DockBottom());
root.Add(panes);
SelectCase(args.Contains("--tables") ? ReviewCases.All.First(entry => entry.Name.StartsWith("08 ")) : ReviewCases.All[0]);
Application.Run(new Window().Resizable(1440, 940).Title("MewUI Markdown - Comprehensive Visual Review").Content(root));
