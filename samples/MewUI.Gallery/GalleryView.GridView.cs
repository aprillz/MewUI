using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement GridViewPage() =>
        CardGrid(
            SimpleGridViewCard(),

            ComplexGridViewBindingCard(),

            TemplateGridViewCard());

    private FrameworkElement SimpleGridViewCard()
    {
        var gridItems = Enumerable.Range(1, 64)
            .Select(i => new SimpleGridRow(i, $"Item {i}", (i % 6) switch { 1 => "Warning", 2 => "Error", _ => "Normal" }))
            .ToArray();
        GridView simple = null!;
        var gridHitText = new ObservableValue<string>("Click: (none)");
        return Card(
            "GridView",
            new DockPanel()
                .Height(240)
                .Spacing(6)
                .Children(
                    new Label()
                        .DockBottom()
                        .BindText(gridHitText)
                        .FontSize(11),

                    new GridView()
                        .Ref(out simple)
                        .Height(240)
                        .ItemsSource(gridItems)
                        .OnMouseDown(e =>
                        {
                            if (simple.TryGetCellIndexAt(e, out int rowIndex, out int columnIndex, out bool isHeader))
                            {
                                gridHitText.Value = isHeader
                                    ? $"Click: Header  Col={columnIndex}"
                                    : $"Click: Row={rowIndex}  Col={columnIndex}";
                            }
                            else
                            {
                                gridHitText.Value = "Click: (none)";
                            }
                        })
                        .Columns(
                            new GridViewColumn<SimpleGridRow>()
                                .Header("#")
                                .Width(60)
                                .Text(row => row.Id.ToString()),

                            new GridViewColumn<SimpleGridRow>()
                                .Header("Name")
                                .Width(100)
                                .Text(row => row.Name),

                            new GridViewColumn<SimpleGridRow>()
                                .Header("Status")
                                .Width(100)
                                .Template(
                                    build: _ => new Label().Padding(8, 0).CenterVertical(),
                                    bind: (view, row) => view
                                        .Text(row.Status)
                                        .WithTheme((t, c) => c.Foreground(GetColor(t, row.Status)))
                                )
                        )
                )
        );

        Color GetColor(Theme t, string status) => status switch
        {
            "Warning" => Color.Orange,
            "Error" => Color.Red,
            _ => t.Palette.WindowText
        };
    }

    private FrameworkElement ComplexGridViewBindingCard()
    {
        var query = new ObservableValue<string>(string.Empty);
        var onlyErrors = new ObservableValue<bool>(false);
        var minAmount = new ObservableValue<double>(0);
        var sortKey = new ObservableValue<int>(0); // 0=Id,1=Name,2=Amount,3=Status
        var sortDesc = new ObservableValue<bool>(false);

        var summaryText = new ObservableValue<string>("Rows: -");
        var selectedText = new ObservableValue<string>("Selected: (none)");

        var all = Enumerable.Range(1, 800)
            .Select(i => new ComplexGridRow(
                id: i,
                name: $"User {i:00}",
                amount: Math.Round((i * 13.37) % 100, 2),
                hasError: i % 11 == 0 || i % 17 == 0,
                isActive: i % 9 != 0))
            .ToList();

        GridView grid = null!;

        void ApplyView()
        {
            IEnumerable<ComplexGridRow> rows = all;

            var q = (query.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                rows = rows.Where(r =>
                    r.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (r.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (onlyErrors.Value)
            {
                rows = rows.Where(r => r.HasError.Value);
            }

            rows = rows.Where(r => r.Amount.Value >= minAmount.Value);

            rows = sortKey.Value switch
            {
                1 => sortDesc.Value
                    ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
                2 => sortDesc.Value
                    ? rows.OrderByDescending(r => r.Amount.Value)
                    : rows.OrderBy(r => r.Amount.Value),
                3 => sortDesc.Value
                    ? rows.OrderByDescending(r => r.StatusText.Value, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(r => r.StatusText.Value, StringComparer.OrdinalIgnoreCase),
                _ => sortDesc.Value
                    ? rows.OrderByDescending(r => r.Id)
                    : rows.OrderBy(r => r.Id)
            };

            var view = rows.ToList();
            grid.SetItemsSource(view);

            int errorCount = view.Count(r => r.HasError.Value);
            double sum = view.Sum(r => r.Amount.Value);
            summaryText.Value = $"Rows: {view.Count}/{all.Count}   Errors: {errorCount}   Sum: {sum:0.##}";
        }

        void TriggerApply() => ApplyView();

        query.Changed += TriggerApply;
        onlyErrors.Changed += TriggerApply;
        minAmount.Changed += TriggerApply;
        sortKey.Changed += TriggerApply;
        sortDesc.Changed += TriggerApply;

        foreach (var r in all)
        {
            r.Amount.Changed += TriggerApply;
            r.HasError.Changed += TriggerApply;
            r.IsActive.Changed += TriggerApply;
        }

        grid = new GridView()
            .Height(190)
            .ItemsSource(all)
            .Apply(g => g.SelectionChanged += obj =>
            {
                if (obj is ComplexGridRow row)
                {
                    selectedText.Value = $"Selected: #{row.Id}  {row.Name}  {row.StatusText.Value}";
                }
                else
                {
                    selectedText.Value = "Selected: (none)";
                }
            })
            .Columns(
                new GridViewColumn<ComplexGridRow>()
                    .Header("#")
                    .Width(44)
                    .Text(r => r.Id.ToString()),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Name")
                    .Width(110)
                    .Text(x => x.Name),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Amount")
                    .Width(110)
                    .Template(
                        build: _ => new NumericUpDown().Padding(6, 0).CenterVertical().Minimum(0).Maximum(100).Step(0.5).Format("0.##"),
                        bind: (view, row) => view.BindValue(row.Amount)
                    ),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Error")
                    .Width(60)
                    .Template(
                        build: _ => new CheckBox().Center(),
                        bind: (view, row) => view.BindIsChecked(row.HasError)
                    ),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Status")
                    .Width(110)
                    .Template(
                        build: _ => new Label().Padding(8, 0).CenterVertical(),
                        bind: (view, row) => view.BindText(row.StatusText)
                    )
            );

        ApplyView();

        return Card(
            "GridView (Complex binding)",
            new DockPanel()
                .Height(240)
                .Spacing(8)
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new TextBox()
                                .Width(120)
                                .Placeholder("Search (id/name)")
                                .BindText(query),

                            new CheckBox()
                                .Text("Errors only")
                                .BindIsChecked(onlyErrors),

                            new Label().Text("Min amount").CenterVertical().FontSize(11),
                            new NumericUpDown()
                                .Width(90)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(1)
                                .Format("0")
                                .BindValue(minAmount),

                            new ComboBox()
                                .Width(80)
                                .Items(["Id", "Name", "Amount", "Status"])
                                .BindSelectedIndex(sortKey),

                            new CheckBox()
                                .Text("Desc")
                                .BindIsChecked(sortDesc)
                        ),

                    new StackPanel()
                        .DockBottom()
                        .Vertical()
                        .Spacing(2)
                        .Children(
                            new Label().BindText(summaryText).FontSize(11),
                            new Label().BindText(selectedText).FontSize(11)
                        ),

                    grid
                ),
            minWidth: 520
        );
    }

    private FrameworkElement TemplateGridViewCard()
    {
        var complexUsers = Enumerable.Range(1, 250)
            .Select(i => new TemplateComplexPersonRow(
                name: $"User {i:0000}",
                roleIndex: i % 3,
                isOnline: i % 5 != 0,
                progress: i % 101,
                score: (i * 7.3) % 100))
            .ToList();

        return Card(
            "GridView (complex cell templates)",
            new DockPanel()
                .Width(600)
                .Height(240)
                .Spacing(8)
                .Children(
                    new Label()
                        .DockTop()
                        .Text("Shows delegate-based complex cell templates (nested layout + multiple bound controls)")
                        .TextWrapping(TextWrapping.Wrap),

                    ComplexCellsGrid()
                        .ItemsSource(complexUsers)
                )
        );

        GridView ComplexCellsGrid() => new GridView()
            .RowHeight(44)
            .ZebraStriping()
            .Columns(
                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("")
                    .Width(36)
                    .Bind(
                        build: _ => new CheckBox().Padding(0).Center(),
                        bind: (view, item) => ((CheckBox)view).BindIsChecked(item.IsSelected)),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("User")
                    .Width(240)
                    .Bind(
                        build: ctx => new StackPanel()
                            .Vertical()
                            .Spacing(2)
                            .Padding(6, 2)
                            .Children(
                                new Label()
                                    .Register(ctx, "Name")
                                    .Bold(),
                                new StackPanel()
                                    .Horizontal()
                                    .Spacing(8)
                                    .Children(
                                        new Label()
                                            .Register(ctx, "Role")
                                            .FontSize(11),
                                        new Label()
                                            .Register(ctx, "Online")
                                            .FontSize(11),
                                        new Label()
                                            .Register(ctx, "Score")
                                            .FontSize(11)
                                    )
                            ),
                        bind: (_, item, _, ctx) =>
                        {
                            ctx.Get<Label>("Name").BindText(item.Name);
                            ctx.Get<Label>("Role").BindText(item.RoleIndex, role => role switch { 1 => "Admin", 2 => "Guest", _ => "User" });
                            ctx.Get<Label>("Online").BindText(item.IsOnline, v => v ? "Online" : "Offline");
                            ctx.Get<Label>("Score").BindText(item.Score, v => $"Score: {v:0.#}");
                        }),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("Role")
                    .Width(120)
                    .Bind(
                        build: _ => new ComboBox()
                            .Items(["User", "Admin", "Guest"])
                            .Padding(6, 0)
                            .CenterVertical(),
                        bind: (view, item) => ((ComboBox)view).BindSelectedIndex(item.RoleIndex)),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("Progress")
                    .Width(200)
                    .Bind(
                        build: _ => new ProgressBar()
                            .Minimum(0)
                            .Maximum(100)
                            .Height(10)
                            .Margin(6, 0)
                            .CenterVertical(),
                        bind: (view, item) => ((ProgressBar)view).BindValue(item.Progress)),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("Online")
                    .Width(80)
                    .Bind(
                        build: _ => new ToggleSwitch().Center(),
                        bind: (view, item) => ((ToggleSwitch)view).BindIsChecked(item.IsOnline))
            );
    }
}

sealed class ComplexGridRow
{
    public ComplexGridRow(int id, string name, double amount, bool hasError, bool isActive)
    {
        Id = id;
        Name = name;
        Amount = new ObservableValue<double>(amount, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
        HasError = new ObservableValue<bool>(hasError);
        IsActive = new ObservableValue<bool>(isActive);

        StatusText = new ObservableValue<string>(string.Empty);

        void Recompute()
        {
            if (!IsActive.Value)
            {
                StatusText.Value = "Inactive";
                return;
            }

            StatusText.Value = HasError.Value ? "Error" : "OK";
        }

        HasError.Changed += Recompute;
        IsActive.Changed += Recompute;
        Recompute();
    }

    public int Id { get; }

    public string Name { get; }

    public ObservableValue<double> Amount { get; }

    public ObservableValue<bool> HasError { get; }

    public ObservableValue<bool> IsActive { get; }

    public ObservableValue<string> StatusText { get; }
}

sealed class TemplatePerson
{
    public TemplatePerson(string name, bool isOnline, double progress)
    {
        Name = name ?? string.Empty;
        IsChecked = new ObservableValue<bool>(false);
        IsOnline = new ObservableValue<bool>(isOnline);
        Progress = new ObservableValue<double>(progress, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
    }

    public ObservableValue<bool> IsChecked { get; }

    public string Name { get; }

    public ObservableValue<bool> IsOnline { get; }

    public ObservableValue<double> Progress { get; }
}

sealed class TemplateComplexPersonRow
{
    public TemplateComplexPersonRow(string name, int roleIndex, bool isOnline, double progress, double score)
    {
        Name = new ObservableValue<string>(name ?? string.Empty);
        RoleIndex = new ObservableValue<int>(roleIndex, v => Math.Clamp(v, 0, 2));
        IsOnline = new ObservableValue<bool>(isOnline);
        IsSelected = new ObservableValue<bool>(false);
        Progress = new ObservableValue<double>(progress, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
        Score = new ObservableValue<double>(score, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
    }

    public ObservableValue<bool> IsSelected { get; }

    public ObservableValue<string> Name { get; }

    public ObservableValue<int> RoleIndex { get; }

    public ObservableValue<bool> IsOnline { get; }

    public ObservableValue<double> Progress { get; }

    public ObservableValue<double> Score { get; }
}
sealed record SimpleGridRow(int Id, string Name, string Status);
