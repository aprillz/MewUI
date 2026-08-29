namespace Aprillz.MewUI.Controls;

/// <summary>Collapses middle path segments while keeping the file dialog's real FlatButton crumbs.</summary>
internal sealed class FileDialogBreadcrumb : StackPanel
{
    private readonly List<Entry> _entries = [];
    private readonly Action _enterPathEdit;
    private readonly Func<string, IReadOnlyList<string>> _listSubdirs;
    private Action<string>? _navigate;
    private double _lastAvailableWidth = double.NaN;

    public FileDialogBreadcrumb(Action enterPathEdit, Func<string, IReadOnlyList<string>> listSubdirs)
    {
        _enterPathEdit = enterPathEdit;
        _listSubdirs = listSubdirs;
        Orientation = Orientation.Horizontal;
        Spacing = 0;
    }

    public void SetEntries(IEnumerable<(string Label, string Path)> entries, Action<string> navigate)
    {
        _navigate = navigate;
        _entries.Clear();
        _entries.AddRange(entries.Select(entry => new Entry(entry.Label, entry.Path, () => navigate(entry.Path))));
        _lastAvailableWidth = double.NaN;
        InvalidateMeasure();
    }

    public Button? LastCrumbButton
        => Children.OfType<Button>().LastOrDefault();

    protected override Size MeasureContent(Size availableSize)
    {
        EnsureChildren(availableSize.Width);
        return base.MeasureContent(availableSize);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        // Grid measures an auto column with infinite width first.  Its arranged width is the
        // authoritative constraint, so rebuild here as well before placing the child buttons.
        if (EnsureChildren(bounds.Width))
        {
            base.MeasureContent(bounds.Size);
        }

        base.ArrangeContent(bounds);
    }

    private bool EnsureChildren(double availableWidth)
    {
        if (_entries.Count == 0 || (!double.IsNaN(_lastAvailableWidth) && Math.Abs(_lastAvailableWidth - availableWidth) < 0.5))
        {
            return false;
        }

        _lastAvailableWidth = availableWidth;
        double[] widths = MeasureCrumbWidths();
        double separatorWidth = MeasureElement(CreateSeparatorElement());
        double overflowWidth = MeasureElement(CreateCrumbButton("…", static () => { }));
        bool constrained = !double.IsPositiveInfinity(availableWidth);
        var visible = new List<int> { 0 };

        if (_entries.Count > 1)
        {
            visible.Add(_entries.Count - 1);
            for (int index = _entries.Count - 2; index > 0; index--)
            {
                visible.Insert(1, index);
                bool hasHiddenEntries = index > 1;
                double candidate = MeasureLayoutWidth(visible, hasHiddenEntries, widths, separatorWidth, overflowWidth);
                if (constrained && candidate > availableWidth)
                {
                    visible.RemoveAt(1);
                    break;
                }
            }
        }

        var visibleSet = new HashSet<int>(visible);
        Entry[] hidden = _entries.Where((_, index) => index is > 0 && index < _entries.Count - 1 && !visibleSet.Contains(index)).ToArray();

        Clear();
        AddCrumb(_entries[0]);
        if (hidden.Length > 0)
        {
            Add(CreateSeparatorElement(_entries[0].Path));
            var menu = new ContextMenu();
            var commandScope = new CommandScope();
            menu.SetCommandTarget(CommandTarget.From(commandScope));
            foreach (Entry entry in hidden)
            {
                var command = new Command($"fileDialog.navigate.{entry.Path}", entry.Label);
                commandScope.Register(command, entry.Navigate);
                menu.Item(command);
            }

            Button overflow = CreateCrumbButton("…", static () => { });
            menu.Placement = MenuPlacement.Below;
            overflow.OnClick(() => menu.Show(overflow));
            Add(overflow);
        }

        for (int index = 1; index < visible.Count; index++)
        {
            Add(CreateSeparatorElement(_entries[visible[index] - 1].Path));
            AddCrumb(_entries[visible[index]]);
        }

        return true;
    }

    private double MeasureLayoutWidth(
        IReadOnlyList<int> visible,
        bool hasOverflow,
        IReadOnlyList<double> widths,
        double separatorWidth,
        double overflowWidth)
    {
        double total = widths[0];
        int elementCount = 1;

        if (hasOverflow)
        {
            total += separatorWidth + overflowWidth;
            elementCount += 2;
        }

        for (int index = 1; index < visible.Count; index++)
        {
            total += separatorWidth + widths[visible[index]];
            elementCount += 2;
        }

        return total + (elementCount - 1) * Spacing;
    }

    // The chevron after a crumb opens that folder's subfolders (siblings of the next crumb) as a dropdown;
    // picking one navigates there. An empty path is a measurement-only probe.
    private BreadcrumbDropDown CreateSeparatorElement(string leftPath = "")
        => new(leftPath, _listSubdirs, _navigate ?? (static _ => { }));

    private void AddCrumb(Entry entry)
        => Add(CreateCrumbButton(entry.Label, entry.Navigate));

    private Button CreateCrumbButton(string label, Action navigate)
        => new Button().Content(label, false).TabIndex(2).StyleName(BuiltInStyles.FlatButton)
            .OnClick(navigate)
            .OnKeyDown(e =>
            {
                if (e.Key == Key.F2 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
                {
                    _enterPathEdit();
                    e.Handled = true;
                }
            });

    private double[] MeasureCrumbWidths()
    {
        var probes = new List<Button>(_entries.Count);
        Clear();
        foreach (Entry entry in _entries)
        {
            Button probe = CreateCrumbButton(entry.Label, static () => { });
            Add(probe);
            probes.Add(probe);
        }

        foreach (Button probe in probes)
        {
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        double[] widths = probes.Select(probe => probe.DesiredSize.Width).ToArray();
        Clear();
        return widths;
    }

    private double MeasureElement(Element element)
    {
        Add(element);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = element.DesiredSize.Width;
        Clear();
        return width;
    }

    private sealed record Entry(string Label, string Path, Action Navigate);
}
