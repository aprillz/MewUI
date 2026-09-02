using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The container a <see cref="GridView"/> realizes for one row: it hosts that row's cells, draws the
/// selection, hover and grid lines, and routes row input.
/// </summary>
/// <remarks>
/// Reachable from <c>PrepareContainer</c>, so an application can attach behavior to the whole row
/// rather than repeating it in every cell template. It also supplies <see cref="Item"/> as the
/// operand of commands invoked from within it.
/// </remarks>
public sealed class GridViewRow : Panel, ICommandArgumentSource
{
    private readonly GridView _owner;
    private readonly List<Cell> _cells = new();
    private int _rowIndex;
    private uint _lastDpi;
    private int _lastColumnsVersion = -1;
    private Theme? _lastTheme;

    private static readonly MewPropertyKey<bool> IsSelectedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<GridViewRow>(nameof(IsSelected), false);

    /// <summary>Whether the item this row holds is selected.</summary>
    public static readonly MewProperty<bool> IsSelectedProperty = IsSelectedPropertyKey.Property;

    private static readonly MewPropertyKey<object?> ItemPropertyKey =
        MewProperty<object?>.RegisterReadOnly<GridViewRow>(nameof(Item), null);

    /// <summary>The item this row currently holds.</summary>
    public static readonly MewProperty<object?> ItemProperty = ItemPropertyKey.Property;

    internal GridViewRow(GridView owner)
    {
        _owner = owner;
        IsHitTestVisible = true;
    }

    /// <summary>Gets the index of the item this row currently holds, or -1 when it holds none.</summary>
    public int Index => _rowIndex;

    /// <summary>
    /// Gets whether the item this row holds is selected. The grid keeps this current; the row draws
    /// the selection from the grid's own state, not from this property.
    /// </summary>
    public bool IsSelected => GetValue(IsSelectedProperty);

    /// <summary>
    /// Gets the item this row currently holds, or null when it holds none.
    /// </summary>
    public object? Item => GetValue(ItemProperty);

    object? ICommandArgumentSource.CommandArgument => Item;

    internal void SetIsSelected(bool isSelected) => SetValue(IsSelectedPropertyKey, isSelected);

    /// <summary>
    /// Clears the local values a prepare hook may have assigned, so a recycled row does not carry
    /// the previous item's state. Bindings survive: the template context clears those.
    /// </summary>
    internal void ResetForItem()
    {
        ClearLocalValue(ContextMenuProperty);
        ClearLocalValue(ToolTipProperty);
        ClearLocalValue(IsEnabledProperty);
        ClearLocalValue(IsHitTestVisibleProperty);
        ClearLocalValue(CursorProperty);
        ClearLocalValue(OpacityProperty);
        ClearLocalValue(TagProperty);
        IsHitTestVisible = true;
    }

    // OnRender reads IsMouseOver directly (no style trigger), so the framework's
    // visual-state path doesn't invalidate for us. Schedule a render explicitly.
    protected override void OnMouseEnter() => InvalidateVisual();

    protected override void OnMouseLeave() => InvalidateVisual();

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (!_owner.IsEffectivelyEnabled)
        {
            return;
        }

        _owner.HandleRowPointerDown(_rowIndex, e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Handled || e.Button != MouseButton.Left || !_owner.IsEffectivelyEnabled)
        {
            return;
        }

        _owner.HandleRowPointerUp(_rowIndex, e);
    }

    internal void EnsureDpi(uint dpi)
    {
        if (_lastDpi == dpi)
        {
            return;
        }

        var old = _lastDpi;
        _lastDpi = dpi;

        VisualTree.Visit(this, e =>
        {
            if (e is FrameworkElement fe)
            {
                fe.NotifyDpiChanged(old, dpi);
            }
        });

        InvalidateMeasure();
    }

    internal void EnsureColumns(IReadOnlyList<GridView.GridViewCore.ColumnDefinition> columns, int columnsVersion)
    {
        if (_lastColumnsVersion == columnsVersion)
        {
            return;
        }

        _lastColumnsVersion = columnsVersion;

        while (_cells.Count < columns.Count)
        {
            var ctx = new TemplateContext();
            var cell = new Cell(this, ctx);
            _cells.Add(cell);
            Add(cell.View);
        }

        while (_cells.Count > columns.Count)
        {
            int idx = _cells.Count - 1;
            _cells[idx].Unbind();
            _cells[idx].Context.Dispose();
            RemoveAt(idx);
            _cells.RemoveAt(idx);
        }

        for (int i = 0; i < columns.Count; i++)
        {
            _cells[i].Template = columns[i].CellTemplate;
            _cells[i].EnsureViewBuilt(this);
        }

        InvalidateMeasure();
    }

    internal void EnsureTheme(Theme theme)
    {
        if (ReferenceEquals(_lastTheme, theme))
        {
            return;
        }

        // If this row was recycled during a theme change, it won't be in the window visual tree and will miss
        // the broadcast. Sync the whole subtree on reuse so templates don't render with a stale cached ThemeInternal.
        _lastTheme = theme;
        VisualTree.Visit(this, e =>
        {
            if (e is FrameworkElement fe && !ReferenceEquals(fe.ThemeInternal, theme))
            {
                fe.NotifyThemeChanged(fe.ThemeInternal, theme);
            }
        });
    }

    internal void Bind(object? item, int index)
    {
        _rowIndex = index;
        SetValue(ItemPropertyKey, item);
        for (int i = 0; i < _cells.Count; i++)
        {
            _cells[i].Bind(item, index);
        }

        InvalidateMeasure();
    }

    internal void Recycle()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            _cells[i].Unbind();
        }

        SetValue(ItemPropertyKey, null);
        InvalidateMeasure();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var pad = _owner.CellPadding;
        double padH = pad.HorizontalThickness;
        double padV = pad.VerticalThickness;
        double maxCellH = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            double h = double.IsPositiveInfinity(availableSize.Height)
                ? double.PositiveInfinity
                : Math.Max(0, availableSize.Height - padV);

            var column = _owner._core.Columns[i];
            if (column.Width.IsAuto)
            {
                _cells[i].View.Measure(new Size(double.PositiveInfinity, h));
                _owner.ReportAutoDesiredWidth(i, _cells[i].View.DesiredSize.Width + padH);
            }

            double w = Math.Max(0, column.ActualWidth - padH);
            _cells[i].View.Measure(new Size(w, h));
            if (_cells[i].View.DesiredSize.Height > maxCellH)
            {
                maxCellH = _cells[i].View.DesiredSize.Height;
            }
        }

        // Report measured max cell height + padding. FixedHeightItemsPresenter ignores
        // this and uses its own ItemHeight; VariableHeightItemsPresenter uses it as the
        // actual row height for prefix-sum bookkeeping and viewport layout.
        double rowH = double.IsPositiveInfinity(availableSize.Height)
            ? maxCellH + padV
            : availableSize.Height;
        return new Size(availableSize.Width, rowH);
    }

    internal void MeasureAutoColumn(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)_cells.Count)
        {
            return;
        }

        var pad = _owner.CellPadding;
        double rowHeight = Bounds.Height > 0 ? Bounds.Height : _owner.ResolveRowHeight();
        double availableHeight = Math.Max(0, rowHeight - pad.VerticalThickness);
        var view = _cells[columnIndex].View;
        view.Measure(new Size(double.PositiveInfinity, availableHeight));
        _owner._core.ReportAutoDesiredWidth(
            columnIndex,
            view.DesiredSize.Width + pad.HorizontalThickness);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        double x = bounds.X;
        var pad = _owner.CellPadding;
        for (int i = 0; i < _cells.Count; i++)
        {
            double w = Math.Max(0, _owner._core.Columns[i].ActualWidth);
            var cellRect = new Rect(
                x + pad.Left,
                bounds.Y + pad.Top,
                Math.Max(0, w - pad.HorizontalThickness),
                Math.Max(0, bounds.Height - pad.VerticalThickness));
            _cells[i].View.Arrange(cellRect);
            x += w;
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = Theme;
        var snapped = GetSnappedBorderBounds(Bounds);
        var isSelected = _owner._core.IsItemSelected(_rowIndex);

        var r = theme.Metrics.ControlCornerRadius - 2;
        if (isSelected)
        {
            if (r > 0)
            {
                context.FillRoundedRectangle(snapped, r, r, theme.Palette.SelectionBackground);
            }
            else
            {
                context.FillRectangle(snapped, theme.Palette.SelectionBackground);
            }
        }
        else if (IsMouseOver && _owner.IsEffectivelyEnabled)
        {
            var hoverBg = theme.Palette.ControlBackground.Lerp(theme.Palette.Accent, 0.15);

            if (r > 0)
            {
                context.FillRoundedRectangle(snapped, r, r, hoverBg);
            }
            else
            {
                context.FillRectangle(snapped, hoverBg);
            }
        }

        if (_owner.ShowGridLines)
        {
            var stroke = theme.Palette.ControlBorder;
            context.DrawLine(new Point(snapped.X, snapped.Bottom - 1), new Point(snapped.Right, snapped.Bottom - 1), stroke, 1, pixelSnap: true);

            double x = snapped.X;
            for (int i = 0; i < _owner._core.Columns.Count; i++)
            {
                x += Math.Max(0, _owner._core.Columns[i].ActualWidth);
                if (x >= snapped.Right - 0.5)
                {
                    break;
                }

                context.DrawLine(new Point(x, snapped.Y), new Point(x, snapped.Bottom), stroke, 1, pixelSnap: true);
            }
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            // Keep collapsed cells realized and bound so their column can be restored,
            // but do not render controls into a zero-width slot. Bordered controls would
            // otherwise collapse both edges into a visible vertical line.
            if (_owner._core.Columns[i].ActualWidth <= 0.01)
            {
                continue;
            }

            _cells[i].View.Render(context);
        }
    }

    private sealed class Cell
    {
        private readonly GridViewRow _row;
        private bool _built;

        public Cell(GridViewRow row, TemplateContext context)
        {
            _row = row;
            Context = context;
            View = new TextBlock();
        }

        public TemplateContext Context { get; }

        public IDataTemplate? Template { get; set; }

        public FrameworkElement View { get; private set; }

        public void Bind(object? item, int index)
        {
            Context.BindTemplate(View, Template!, item, index);
        }

        public void Unbind()
        {
            Context.UnbindTemplate(View);
        }

        public void EnsureViewBuilt(GridViewRow row)
        {
            if (_built || Template == null)
            {
                return;
            }

            var built = Template.Build(Context);
            built.Parent = row;

            int idx = -1;
            for (int i = 0; i < row.Children.Count; i++)
            {
                if (ReferenceEquals(row.Children[i], View))
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
            {
                row.RemoveAt(idx);
                row.Insert(idx, built);
            }

            View = built;
            _built = true;

            // MouseDown bubbles up the visual tree, so a single handler
            // on the root view catches clicks on all child elements.
            View.MouseDown += OnCellMouseDown;
        }

        private void OnCellMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButton.Left)
            {
                return;
            }

            if (e.Handled)
            {
                return;
            }

            if (!_row._owner.IsEffectivelyEnabled)
            {
                return;
            }

            _row._owner.HandleRowPointerDown(_row._rowIndex, e);
        }
    }
}
