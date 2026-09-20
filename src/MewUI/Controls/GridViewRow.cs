using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The container a <see cref="GridView"/> realizes for one row: it hosts that row's cells, draws the
/// row background and grid lines, and routes row input. Its background in each state comes from its
/// style: the row reports whether it is selected or under the pointer in its visual state.
/// </summary>
/// <remarks>
/// Reachable from <c>PrepareContainer</c>, so an application can attach behavior to the whole row
/// rather than repeating it in every cell template. It also supplies <see cref="Item"/> as the
/// operand of commands invoked from within it.
/// </remarks>
public sealed class GridViewRow : Control, IVisualTreeHost, ICommandArgumentSource
{
    // A style without setters keeps the themed border of the Control base style off the row, which
    // would inset every cell. Later triggers win, so selection covers the hover.
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<GridViewRow>(static () => new Style(typeof(GridViewRow))
        {
            Setters =
            [
                Setter.Create(CornerRadiusProperty, theme => Math.Max(0, theme.Metrics.ControlCornerRadius - ROW_CORNER_INSET)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground.Lerp(theme.Palette.Accent, HOVER_ACCENT_SHARE))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.SelectionBackground)],
                },
            ],
        });

    private const double ROW_CORNER_INSET = 2;
    private const double HOVER_ACCENT_SHARE = 0.15;

    private readonly GridView _owner;
    private readonly List<Cell> _cells = new();
    private int _rowIndex;
    private uint _lastDpi;
    private int _lastColumnsVersion = -1;
    private Theme? _lastTheme;

    // Alternating-row fill, pushed by the grid on every bind; drawn under selection and hover.
    private bool _isAlternate;
    private Color _alternateBackground;

    private static readonly MewPropertyKey<bool> IsSelectedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<GridViewRow>(nameof(IsSelected), false,
            MewPropertyOptions.AffectsRender);

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

    /// <summary>Gets whether the item this row holds is selected; the row draws its selection from it.</summary>
    public bool IsSelected => GetValue(IsSelectedProperty);

    /// <summary>
    /// Gets the item this row currently holds, or null when it holds none.
    /// </summary>
    public object? Item => GetValue(ItemProperty);

    object? ICommandArgumentSource.CommandArgument => Item;

    internal void SetIsSelected(bool isSelected)
    {
        if (IsSelected != isSelected)
        {
            SetValue(IsSelectedPropertyKey, isSelected);
            InvalidateVisualState();
        }
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        var flags = state.Flags;

        // A disabled grid shows no row under the pointer, though its rows still see the pointer.
        if (!_owner.IsEffectivelyEnabled)
        {
            flags &= ~VisualStateFlags.Hot;
        }

        if (IsSelected)
        {
            flags |= VisualStateFlags.Selected;
        }

        return state with { Flags = flags };
    }

    /// <summary>Sets whether this row is an alternating row and the background it fills when it is.</summary>
    internal void SetAlternate(bool isAlternate, Color background)
    {
        if (_isAlternate == isAlternate && _alternateBackground == background)
        {
            return;
        }

        _isAlternate = isAlternate;
        _alternateBackground = background;
        InvalidateVisual();
    }

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
            cell.View.Parent = this;
        }

        while (_cells.Count > columns.Count)
        {
            int idx = _cells.Count - 1;
            _cells[idx].Unbind();
            _cells[idx].Context.Dispose();
            _cells[idx].View.Parent = null;
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
        SetAlternate(false, Color.Transparent);
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

        // The alternating fill sits under the selection and hover backgrounds, which cover it.
        if (_isAlternate && _alternateBackground.A != 0)
        {
            context.FillRectangle(snapped, _alternateBackground);
        }

        // Selected or under the pointer, by the triggers of the style.
        var background = Background;
        if (background.A != 0)
        {
            double cornerRadius = CornerRadius;
            if (cornerRadius > 0)
            {
                context.FillRoundedRectangle(snapped, cornerRadius, cornerRadius, background);
            }
            else
            {
                context.FillRectangle(snapped, background);
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

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        // A cell in a collapsed column is visited too: it stays bound and has to hear of theme and DPI changes.
        for (int index = 0; index < _cells.Count; index++)
        {
            if (!visitor(_cells[index].View))
            {
                return false;
            }
        }

        return true;
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!Bounds.Contains(point) || !IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        for (int index = _cells.Count - 1; index >= 0; index--)
        {
            // A collapsed column shows nothing, so nothing of it can be hit.
            if (_owner._core.Columns[index].ActualWidth <= 0.01)
            {
                continue;
            }

            var hit = _cells[index].View.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return this;
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

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(0);
        for (int index = 0; index < _cells.Count; index++)
        {
            // The same cells RenderSubtree draws: a collapsed column keeps its cell but shows none of it.
            if (_owner._core.Columns[index].ActualWidth <= 0.01)
            {
                continue;
            }

            builder.Child(_cells[index].View);
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
            View.Parent = null;
            built.Parent = row;
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
