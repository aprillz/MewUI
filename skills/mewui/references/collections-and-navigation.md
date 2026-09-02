# Display collections and switch pages

Use `ObservableCollection<T>` when the UI must react to add and remove operations. Wrap it in `ItemsView<T>` when the control needs typed text and stable keys.

```csharp
using System.Collections.ObjectModel;

var people = new ObservableCollection<Person>
{
    new(1, "Ada", "Rendering", true),
    new(2, "Grace", "Controls", true),
    new(3, "Linus", "Platform", false),
};

var peopleView = new ItemsView<Person>(
    people,
    textSelector: person => person.Name,
    keySelector: person => person.Id);

var selected = new ObservableValue<string>("No selection");

var list = new ListBox()
    .ItemsSource(peopleView)
    .ItemHeight(48)
    .OnSelectionChanged(value =>
    {
        selected.Value = value is Person person
            ? $"Selected: {person.Name}"
            : "No selection";
    });
```

## Typed item template

```csharp
list.ItemTemplate<Person>(
    build: context => new Grid()
        .Columns("Auto,*")
        .Rows("Auto,Auto")
        .Children(
            new Ellipse()
                .Register(context, "Status")
                .Size(10, 10)
                .CenterVertical()
                .RowSpan(2),
            new TextBlock()
                .Register(context, "Name")
                .Bold()
                .Margin(10, 0, 0, 0)
                .Column(1),
            new TextBlock()
                .Register(context, "Role")
                .FontSize(ThemeFontSize.Small)
                .Margin(10, 0, 0, 0)
                .Row(1)
                .Column(1)),
    bind: (_, person, _, context) =>
    {
        context.Get<TextBlock>("Name").Text = person.Name;
        context.Get<TextBlock>("Role").Text = person.Role;
        context.Get<Ellipse>("Status").WithTheme((theme, dot) =>
            dot.Fill(person.IsOnline
                ? theme.Palette.Accent
                : theme.Palette.ControlBorder));
    });

sealed record Person(int Id, string Name, string Role, bool IsOnline);
```

`build` creates reusable visuals. `bind` applies the current item and may run many times because containers are recycled. Do not retain the previous item or attach duplicate event handlers during rebinding.

Use `ListBox` for selection, `ComboBox` for compact selection, `TreeView` for hierarchy, `GridView` for typed columns, and `NavigationView` for application-level navigation. For a small application, tabs or replacing one content host from explicit state is simpler than introducing a navigation service.

## Typed table

Use `GridViewColumn<T>` so display and sorting logic stays tied to the row type.

```csharp
var rows = new[]
{
    new StatusRow(1, "Restore packages", "Done"),
    new StatusRow(2, "Build application", "Running"),
};

var table = new GridView()
    .ItemsSource(rows)
    .Columns(
        new GridViewColumn<StatusRow>()
            .Header("#")
            .PixelWidth(52)
            .Text(row => row.Id.ToString())
            .SortBy(row => row.Id),
        new GridViewColumn<StatusRow>()
            .Header("Task")
            .StarWidth(minWidth: 140)
            .Text(row => row.Name)
            .SortBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
        new GridViewColumn<StatusRow>()
            .Header("Status")
            .AutoWidth(minWidth: 90, maxWidth: 160)
            .Text(row => row.Status));

sealed record StatusRow(int Id, string Name, string Status);
```

For editable cells, use `Template(build:, bind:)`. Build the editor once and bind it to the current row in `bind`; do not rebuild `ItemsSource` while that editor is active because doing so replaces its container and loses focus.

## Application navigation

`NavigationView.Items` takes typed entries plus selectors for the label, optional icon, and page content. Icons are regular `Element` instances.

```csharp
var pages = new[]
{
    new AppPage("Home", new TextBlock().Text("H").Center(), () => new TextBlock().Text("Home page")),
    new AppPage("Settings", new TextBlock().Text("S").Center(), () => new TextBlock().Text("Settings page")),
};

var navigation = new NavigationView
{
    PaneWidth = 180,
    PaneDisplayMode = PaneDisplayMode.Inline,
};

navigation.Items(
    pages,
    page => page.Title,
    icon: page => page.Icon,
    content: page => new Border().Padding(20).Child(page.Build()));
navigation.SelectedIndex = 0;

sealed record AppPage(string Title, Element Icon, Func<UIElement> Build);
```

Choose `PaneDisplayMode.Auto` when the pane should adapt to available width. Set an initial `SelectedIndex` when the window must not open with an empty content area.
