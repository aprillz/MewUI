using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Grid unit type for row/column sizing.
/// </summary>
public enum GridUnitType
{
    /// <summary>Size to content.</summary>
    Auto,

    /// <summary>Fixed pixel size.</summary>
    Pixel,

    /// <summary>Proportional size (star sizing).</summary>
    Star
}

/// <summary>
/// Represents a grid length value.
/// </summary>
public readonly struct GridLength
{
    /// <summary>
    /// Numeric value.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Unit type.
    /// </summary>
    public GridUnitType GridUnitType { get; }

    public GridLength(double value, GridUnitType type = GridUnitType.Pixel)
    {
        Value = value;
        GridUnitType = type;
    }

    /// <summary>
    /// Gets whether this is Auto sizing.
    /// </summary>
    public bool IsAuto => GridUnitType == GridUnitType.Auto;

    /// <summary>
    /// Gets whether this is Star sizing.
    /// </summary>
    public bool IsStar => GridUnitType == GridUnitType.Star;

    /// <summary>
    /// Gets whether this is absolute pixel sizing.
    /// </summary>
    public bool IsAbsolute => GridUnitType == GridUnitType.Pixel;

    /// <summary>
    /// Auto sizing (size to content).
    /// </summary>
    public static GridLength Auto => new(1, GridUnitType.Auto);

    /// <summary>
    /// Star sizing (1*).
    /// </summary>
    public static GridLength Star => new(1, GridUnitType.Star);

    /// <summary>
    /// Star sizing with specified value.
    /// </summary>
    /// <param name="value">Star value.</param>
    /// <returns>GridLength with star sizing.</returns>
    public static GridLength Stars(double value) => new(value, GridUnitType.Star);

    /// <summary>
    /// Absolute pixel sizing.
    /// </summary>
    /// <param name="value">Pixel value.</param>
    /// <returns>GridLength with pixel sizing.</returns>
    public static GridLength Pixels(double value) => new(value, GridUnitType.Pixel);

    public static implicit operator GridLength(double value) => new(value, GridUnitType.Pixel);
}

public abstract class GridDefinitionBase
{
    internal Grid? ParentGrid { get; private set; }

    internal double MeasureSize { get; set; }

    internal double FinalSize { get; set; }

    internal double Offset { get; set; }

    internal abstract GridLength UserSize { get; }

    internal abstract double UserMinSize { get; }

    internal abstract double UserMaxSize { get; }

    internal abstract double ActualSize { get; set; }

    internal void Attach(Grid grid)
    {
        ParentGrid = grid;
    }

    internal void Detach(Grid grid)
    {
        if (ReferenceEquals(ParentGrid, grid))
        {
            ParentGrid = null;
        }
    }

    protected void InvalidateParentGrid()
    {
        ParentGrid?.InvalidateMeasure();
    }
}

public sealed class RowDefinition : GridDefinitionBase
{
    private GridLength _height = GridLength.Star;
    private double _minHeight;
    private double _maxHeight = double.PositiveInfinity;

    public GridLength Height
    {
        get => _height;
        set
        {
            if (_height.Equals(value))
            {
                return;
            }

            _height = value;
            InvalidateParentGrid();
        }
    }

    public double MinHeight
    {
        get => _minHeight;
        set
        {
            if (_minHeight.Equals(value))
            {
                return;
            }

            _minHeight = value;
            InvalidateParentGrid();
        }
    }

    public double MaxHeight
    {
        get => _maxHeight;
        set
        {
            if (_maxHeight.Equals(value))
            {
                return;
            }

            _maxHeight = value;
            InvalidateParentGrid();
        }
    }

    public double ActualHeight
    {
        get => ActualSize;
        internal set => ActualSize = value;
    }

    internal override GridLength UserSize => Height;

    internal override double UserMinSize => MinHeight;

    internal override double UserMaxSize => MaxHeight;

    internal override double ActualSize { get; set; }
}

public sealed class ColumnDefinition : GridDefinitionBase
{
    private GridLength _width = GridLength.Star;
    private double _minWidth;
    private double _maxWidth = double.PositiveInfinity;

    public GridLength Width
    {
        get => _width;
        set
        {
            if (_width.Equals(value))
            {
                return;
            }

            _width = value;
            InvalidateParentGrid();
        }
    }

    public double MinWidth
    {
        get => _minWidth;
        set
        {
            if (_minWidth.Equals(value))
            {
                return;
            }

            _minWidth = value;
            InvalidateParentGrid();
        }
    }

    public double MaxWidth
    {
        get => _maxWidth;
        set
        {
            if (_maxWidth.Equals(value))
            {
                return;
            }

            _maxWidth = value;
            InvalidateParentGrid();
        }
    }

    public double ActualWidth
    {
        get => ActualSize;
        internal set => ActualSize = value;
    }

    internal override GridLength UserSize => Width;

    internal override double UserMinSize => MinWidth;

    internal override double UserMaxSize => MaxWidth;

    internal override double ActualSize { get; set; }
}

/// <summary>
/// A panel that arranges children in a grid of rows and columns.
/// </summary>
public class Grid : Panel
{
    private static readonly ConditionalWeakTable<Element, GridAttachedProperties> _attachedProperties = new();

    private sealed class GridAttachedProperties
    {
        public int Row;
        public bool HasRow;
        public int Column;
        public bool HasColumn;
        public int RowSpan = 1;
        public int ColumnSpan = 1;
    }

    #region Attached Properties

    /// <summary>
    /// Sets the row index for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="row">Row index.</param>
    public static void SetRow(Element element, int row)
    {
        var props = GetOrCreate(element);
        props.Row = row;
        props.HasRow = true;
    }

    /// <summary>
    /// Gets the row index of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The row index.</returns>
    public static int GetRow(Element element) => TryGet(element, out var props) ? props.Row : 0;

    internal static bool HasRow(Element element) => TryGet(element, out var props) && props.HasRow;

    /// <summary>
    /// Sets the column index for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="column">Column index.</param>
    public static void SetColumn(Element element, int column)
    {
        var props = GetOrCreate(element);
        props.Column = column;
        props.HasColumn = true;
    }

    /// <summary>
    /// Gets the column index of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The column index.</returns>
    public static int GetColumn(Element element) => TryGet(element, out var props) ? props.Column : 0;

    internal static bool HasColumn(Element element) => TryGet(element, out var props) && props.HasColumn;

    /// <summary>
    /// Sets the row span for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="span">Number of rows to span.</param>
    public static void SetRowSpan(Element element, int span) => GetOrCreate(element).RowSpan = span;

    /// <summary>
    /// Gets the row span of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The row span.</returns>
    public static int GetRowSpan(Element element) => TryGet(element, out var props) ? props.RowSpan : 1;

    /// <summary>
    /// Sets the column span for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="span">Number of columns to span.</param>
    public static void SetColumnSpan(Element element, int span) => GetOrCreate(element).ColumnSpan = span;

    /// <summary>
    /// Gets the column span of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The column span.</returns>
    public static int GetColumnSpan(Element element) => TryGet(element, out var props) ? props.ColumnSpan : 1;

    private static GridAttachedProperties GetOrCreate(Element element) => _attachedProperties.GetOrCreateValue(element);

    private static bool TryGet(Element element, [NotNullWhen(true)] out GridAttachedProperties? properties)
        => _attachedProperties.TryGetValue(element, out properties!);

    #endregion

    public static readonly MewProperty<bool> AutoIndexingProperty =
        MewProperty<bool>.Register<Grid>(nameof(AutoIndexing), true, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> SpacingProperty =
        MewProperty<double>.Register<Grid>(nameof(Spacing), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<bool> ShowGridLineProperty =
        MewProperty<bool>.Register<Grid>(nameof(ShowGridLine), false, MewPropertyOptions.AffectsRender);

    private readonly DefinitionCollection<RowDefinition> _rowDefinitions;
    private readonly DefinitionCollection<ColumnDefinition> _columnDefinitions;
    private readonly RowDefinition _implicitRow = new();
    private readonly ColumnDefinition _implicitColumn = new();
    private readonly IReadOnlyList<RowDefinition> _implicitRows;
    private readonly IReadOnlyList<ColumnDefinition> _implicitColumns;

    public Grid()
    {
        _rowDefinitions = new DefinitionCollection<RowDefinition>(this);
        _columnDefinitions = new DefinitionCollection<ColumnDefinition>(this);
        _implicitRows = [_implicitRow];
        _implicitColumns = [_implicitColumn];
    }

    public IList<RowDefinition> RowDefinitions => _rowDefinitions;

    public IList<ColumnDefinition> ColumnDefinitions => _columnDefinitions;

    public bool AutoIndexing
    {
        get => GetValue(AutoIndexingProperty);
        set => SetValue(AutoIndexingProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public bool ShowGridLine
    {
        get => GetValue(ShowGridLineProperty);
        set => SetValue(ShowGridLineProperty, value);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var rows = GetEffectiveRows();
        var columns = GetEffectiveColumns();
        var placements = BuildPlacements(rows.Count, columns.Count);
        var paddedSize = availableSize.Deflate(Padding);
        double spacing = Math.Max(0, Spacing);

        MeasureByGroups(placements, rows, columns, paddedSize, spacing);

        CommitActualSizes(columns, rows, useFinal: false);

        double totalWidth = SumMeasureSizes(columns, spacing);
        double totalHeight = SumMeasureSizes(rows, spacing);
        var result = new Size(totalWidth, totalHeight).Inflate(Padding);

        return result;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var rows = GetEffectiveRows();
        var columns = GetEffectiveColumns();
        var placements = BuildPlacements(rows.Count, columns.Count);
        var contentBounds = bounds.Deflate(Padding);
        double spacing = Math.Max(0, Spacing);

        var finalColumnSizes = CreateFinalSizes(columns, contentBounds.Width, spacing);
        var finalRowSizes = CreateFinalSizes(rows, contentBounds.Height, spacing);

        ApplyFinalSizes(columns, finalColumnSizes);
        ApplyFinalSizes(rows, finalRowSizes);
        CommitActualSizes(columns, rows, useFinal: true);
        CalculateOffsets(columns, spacing);
        CalculateOffsets(rows, spacing);

        foreach (var placement in placements)
        {
            double x = contentBounds.X + columns[placement.Column].Offset;
            double y = contentBounds.Y + rows[placement.Row].Offset;
            double width = GetRangeSize(columns, placement.Column, placement.ColumnSpan, spacing, useFinal: true);
            double height = GetRangeSize(rows, placement.Row, placement.RowSpan, spacing, useFinal: true);
            placement.Child.Arrange(new Rect(x, y, width, height));
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (!ShowGridLine)
        {
            return;
        }

        var rows = GetEffectiveRows();
        var columns = GetEffectiveColumns();
        if (rows.Count == 0 || columns.Count == 0)
        {
            return;
        }

        var contentBounds = Bounds.Deflate(Padding);
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return;
        }

        var lineColor = Theme.Palette.WindowText.WithAlpha(160);
        double spacing = Math.Max(0, Spacing);
        const double thickness = 1.0;

        for (int i = 1; i < columns.Count; i++)
        {
            double x = contentBounds.X + columns[i].Offset - spacing / 2.0;
            context.DrawLine(new Point(x, contentBounds.Y), new Point(x, contentBounds.Bottom), lineColor, thickness, false);
        }

        for (int i = 1; i < rows.Count; i++)
        {
            double y = contentBounds.Y + rows[i].Offset - spacing / 2.0;
            context.DrawLine(new Point(contentBounds.X, y), new Point(contentBounds.Right, y), lineColor, thickness, false);
        }
    }

    private IReadOnlyList<RowDefinition> GetEffectiveRows()
    {
        if (_rowDefinitions.Count > 0)
        {
            _implicitRow.Detach(this);
            return _rowDefinitions;
        }

        _implicitRow.Attach(this);
        return _implicitRows;
    }

    private IReadOnlyList<ColumnDefinition> GetEffectiveColumns()
    {
        if (_columnDefinitions.Count > 0)
        {
            _implicitColumn.Detach(this);
            return _columnDefinitions;
        }

        _implicitColumn.Attach(this);
        return _implicitColumns;
    }

    private List<Placement> BuildPlacements(int rowCount, int columnCount)
    {
        var placements = new List<Placement>();
        var occupied = new bool[rowCount, columnCount];

        foreach (var child in EnumerateVisibleChildren())
        {
            bool hasRow = Grid.HasRow(child);
            bool hasColumn = Grid.HasColumn(child);
            if (!hasRow || !hasColumn)
            {
                continue;
            }

            var placement = CreateClampedPlacement(child, rowCount, columnCount, Grid.GetRow(child), Grid.GetColumn(child));
            placements.Add(placement);
            MarkOccupied(occupied, placement);
        }

        foreach (var child in EnumerateVisibleChildren())
        {
            bool hasRow = Grid.HasRow(child);
            bool hasColumn = Grid.HasColumn(child);
            if (hasRow && hasColumn)
            {
                continue;
            }

            int row = hasRow ? Grid.GetRow(child) : 0;
            int column = hasColumn ? Grid.GetColumn(child) : 0;
            int rowSpan = Math.Max(1, Grid.GetRowSpan(child));
            int columnSpan = Math.Max(1, Grid.GetColumnSpan(child));

            var placement = CreateClampedPlacement(child, rowCount, columnCount, row, column);
            placement = placement with
            {
                RowSpan = Math.Clamp(rowSpan, 1, rowCount - placement.Row),
                ColumnSpan = Math.Clamp(columnSpan, 1, columnCount - placement.Column),
            };

            if (AutoIndexing)
            {
                placement = AutoPlace(occupied, placement, rowCount, columnCount, hasRow, hasColumn);
            }

            placements.Add(placement);
            MarkOccupied(occupied, placement);
        }

        return placements;
    }

    private static Placement AutoPlace(
        bool[,] occupied,
        Placement placement,
        int rowCount,
        int columnCount,
        bool hasRow,
        bool hasColumn)
    {
        if (hasRow && !hasColumn)
        {
            if (TryFindInRow(occupied, placement.Row, placement.RowSpan, placement.ColumnSpan, out int column))
            {
                return placement with { Column = column };
            }
        }
        else if (!hasRow && hasColumn)
        {
            if (TryFindInColumn(occupied, placement.Column, placement.RowSpan, placement.ColumnSpan, out int row))
            {
                return placement with { Row = row };
            }
        }
        else if (!hasRow && !hasColumn)
        {
            if (TryFindFirstFit(occupied, placement.RowSpan, placement.ColumnSpan, out int row, out int column))
            {
                return placement with { Row = row, Column = column };
            }
        }

        return placement;
    }

    private static Placement CreateClampedPlacement(Element child, int rowCount, int columnCount, int row, int column)
    {
        row = Math.Clamp(row, 0, rowCount - 1);
        column = Math.Clamp(column, 0, columnCount - 1);

        int rowSpan = Math.Clamp(Math.Max(1, Grid.GetRowSpan(child)), 1, rowCount - row);
        int columnSpan = Math.Clamp(Math.Max(1, Grid.GetColumnSpan(child)), 1, columnCount - column);

        return new Placement(child, row, column, rowSpan, columnSpan);
    }

    private void MeasureByGroups(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<RowDefinition> rows,
        IReadOnlyList<ColumnDefinition> columns,
        Size paddedSize,
        double spacing)
    {
        bool finiteWidth = !double.IsPositiveInfinity(paddedSize.Width);
        bool finiteHeight = !double.IsPositiveInfinity(paddedSize.Height);

        MeasurePlacements(placements, rows, columns, spacing, mode: MeasureMode.Initial, targetGroup: CellGroup.Group1, null, null, finiteWidth, finiteHeight);
        RecomputeMeasureSizes(columns, rows, placements);

        bool hasCyclicGroups =
            HasGroup(placements, rows, columns, CellGroup.Group2, finiteWidth, finiteHeight) &&
            HasGroup(placements, rows, columns, CellGroup.Group3, finiteWidth, finiteHeight);

        int maxPasses = hasCyclicGroups ? 4 : 1;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            var beforeColumns = CaptureMeasureSizes(columns);
            var beforeRows = CaptureMeasureSizes(rows);

            var columnConstraints = CreateMeasureConstraints(columns, paddedSize.Width, spacing);
            var rowConstraints = CreateMeasureConstraints(rows, paddedSize.Height, spacing);

            MeasurePlacements(placements, rows, columns, spacing, mode: MeasureMode.Constrained, targetGroup: CellGroup.Group2, columnConstraints, rowConstraints, finiteWidth, finiteHeight);
            MeasurePlacements(placements, rows, columns, spacing, mode: MeasureMode.Constrained, targetGroup: CellGroup.Group3, columnConstraints, rowConstraints, finiteWidth, finiteHeight);
            RecomputeMeasureSizes(columns, rows, placements);

            if (!hasCyclicGroups || IsStable(beforeColumns, columns) && IsStable(beforeRows, rows))
            {
                break;
            }
        }

        var finalColumnConstraints = CreateMeasureConstraints(columns, paddedSize.Width, spacing);
        var finalRowConstraints = CreateMeasureConstraints(rows, paddedSize.Height, spacing);

        MeasurePlacements(placements, rows, columns, spacing, mode: MeasureMode.Constrained, targetGroup: CellGroup.Group2, finalColumnConstraints, finalRowConstraints, finiteWidth, finiteHeight);
        MeasurePlacements(placements, rows, columns, spacing, mode: MeasureMode.Constrained, targetGroup: CellGroup.Group3, finalColumnConstraints, finalRowConstraints, finiteWidth, finiteHeight);
        RecomputeMeasureSizes(columns, rows, placements);

        var group4ColumnConstraints = CreateMeasureConstraints(columns, paddedSize.Width, spacing);
        var group4RowConstraints = CreateMeasureConstraints(rows, paddedSize.Height, spacing);
        MeasurePlacements(placements, rows, columns, spacing, mode: MeasureMode.Constrained, targetGroup: CellGroup.Group4, group4ColumnConstraints, group4RowConstraints, finiteWidth, finiteHeight);

        RecomputeMeasureSizes(columns, rows, placements);
    }

    private void MeasurePlacements(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<RowDefinition> rows,
        IReadOnlyList<ColumnDefinition> columns,
        double spacing,
        MeasureMode mode,
        CellGroup targetGroup,
        double[]? columnConstraints,
        double[]? rowConstraints,
        bool finiteWidth,
        bool finiteHeight)
    {
        foreach (var placement in placements)
        {
            if (GetCellGroup(placement, rows, columns, finiteWidth, finiteHeight) != targetGroup)
            {
                continue;
            }

            var constraint = CreateChildConstraint(placement, rows, columns, spacing, mode, columnConstraints, rowConstraints, finiteWidth, finiteHeight);
            placement.Child.Measure(constraint);
        }
    }

    private void RecomputeMeasureSizes(
        IReadOnlyList<ColumnDefinition> columns,
        IReadOnlyList<RowDefinition> rows,
        IReadOnlyList<Placement> placements)
    {
        ComputeMeasureSizes(columns, placements, isColumn: true);
        ComputeMeasureSizes(rows, placements, isColumn: false);
    }

    private static bool HasGroup(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<RowDefinition> rows,
        IReadOnlyList<ColumnDefinition> columns,
        CellGroup group,
        bool finiteWidth,
        bool finiteHeight)
    {
        foreach (var placement in placements)
        {
            if (GetCellGroup(placement, rows, columns, finiteWidth, finiteHeight) == group)
            {
                return true;
            }
        }

        return false;
    }

    private static double[] CaptureMeasureSizes<T>(IReadOnlyList<T> definitions) where T : GridDefinitionBase
        => definitions.Select(definition => definition.MeasureSize).ToArray();

    private static bool IsStable<T>(double[] before, IReadOnlyList<T> definitions) where T : GridDefinitionBase
    {
        if (before.Length != definitions.Count)
        {
            return false;
        }

        for (int i = 0; i < before.Length; i++)
        {
            if (Math.Abs(before[i] - definitions[i].MeasureSize) > 0.1)
            {
                return false;
            }
        }

        return true;
    }

    private static CellGroup GetCellGroup(
        Placement placement,
        IReadOnlyList<RowDefinition> rows,
        IReadOnlyList<ColumnDefinition> columns,
        bool finiteWidth,
        bool finiteHeight)
    {
        bool rowHasStar = SpanHas(rows, placement.Row, placement.RowSpan, definition => GetMeasureUnitType(definition, finiteHeight) == GridUnitType.Star);
        bool rowHasAuto = SpanHas(rows, placement.Row, placement.RowSpan, definition => GetMeasureUnitType(definition, finiteHeight) == GridUnitType.Auto);
        bool columnHasStar = SpanHas(columns, placement.Column, placement.ColumnSpan, definition => GetMeasureUnitType(definition, finiteWidth) == GridUnitType.Star);
        bool columnHasAuto = SpanHas(columns, placement.Column, placement.ColumnSpan, definition => GetMeasureUnitType(definition, finiteWidth) == GridUnitType.Auto);

        if (!rowHasStar && !columnHasStar)
        {
            return CellGroup.Group1;
        }

        if (rowHasStar && columnHasAuto)
        {
            return CellGroup.Group2;
        }

        if (rowHasAuto && columnHasStar)
        {
            return CellGroup.Group3;
        }

        return CellGroup.Group4;
    }

    private static bool SpanHas<T>(IReadOnlyList<T> definitions, int start, int span, Func<T, bool> predicate)
    {
        for (int i = start; i < start + span; i++)
        {
            if (predicate(definitions[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static GridUnitType GetMeasureUnitType(GridDefinitionBase definition, bool finiteConstraint)
    {
        if (definition.UserSize.IsAbsolute)
        {
            return GridUnitType.Pixel;
        }

        if (definition.UserSize.IsStar && finiteConstraint)
        {
            return GridUnitType.Star;
        }

        return GridUnitType.Auto;
    }

    private Size CreateChildConstraint(
        Placement placement,
        IReadOnlyList<RowDefinition> rows,
        IReadOnlyList<ColumnDefinition> columns,
        double spacing,
        MeasureMode mode,
        double[]? columnConstraints,
        double[]? rowConstraints,
        bool finiteWidth,
        bool finiteHeight)
    {
        double width = CreateAxisConstraint(placement.Column, placement.ColumnSpan, columns, spacing, mode, columnConstraints, finiteWidth);
        double height = CreateAxisConstraint(placement.Row, placement.RowSpan, rows, spacing, mode, rowConstraints, finiteHeight);
        return new Size(width, height);
    }

    private double CreateAxisConstraint<T>(
        int start,
        int span,
        IReadOnlyList<T> definitions,
        double spacing,
        MeasureMode mode,
        double[]? constraints,
        bool finiteConstraint)
        where T : GridDefinitionBase
    {
        // Use effective unit type (Star becomes Auto when constraint is infinite).
        bool hasAuto = false;
        bool hasStar = false;
        for (int i = start; i < start + span; i++)
        {
            var unitType = GetMeasureUnitType(definitions[i], finiteConstraint);
            if (unitType == GridUnitType.Auto) hasAuto = true;
            else if (unitType == GridUnitType.Star) hasStar = true;
        }

        if (hasAuto && !hasStar)
        {
            return double.PositiveInfinity;
        }

        // WPF GetMeasureSizeForRange: Auto → MinSize, others → MeasureSize.
        if (mode == MeasureMode.Constrained && constraints != null)
        {
            if (hasAuto)
            {
                double total = 0;
                for (int i = start; i < start + span; i++)
                {
                    total += definitions[i].UserSize.IsAuto
                        ? ClampDefinitionSize(definitions[i], definitions[i].UserMinSize)
                        : constraints[i];
                }
                if (span > 1) total += (span - 1) * spacing;
                return total;
            }

            return GetRangeSize(constraints, start, span, spacing);
        }

        bool allPixel = true;
        for (int i = start; i < start + span; i++)
        {
            if (!definitions[i].UserSize.IsAbsolute)
            {
                allPixel = false;
                break;
            }
        }

        if (allPixel)
        {
            double total = 0;
            for (int i = start; i < start + span; i++)
            {
                total += definitions[i].UserSize.Value;
            }

            if (span > 1)
            {
                total += (span - 1) * spacing;
            }

            return total;
        }

        return double.PositiveInfinity;
    }

    private void ComputeMeasureSizes<T>(IReadOnlyList<T> definitions, IReadOnlyList<Placement> placements, bool isColumn)
        where T : GridDefinitionBase
    {
        InitializeMeasureSizes(definitions);
        var spanRequests = new Dictionary<SpanKey, double>();

        foreach (var placement in placements)
        {
            int start = isColumn ? placement.Column : placement.Row;
            int span = isColumn ? placement.ColumnSpan : placement.RowSpan;
            double desired = GetDesiredSize(placement.Child, isColumn);
            if (span > 1)
            {
                desired = Math.Max(0, desired - (span - 1) * Math.Max(0, Spacing));
            }

            if (span == 1)
            {
                var definition = definitions[start];
                if (!definition.UserSize.IsAbsolute)
                {
                    definition.MeasureSize = Math.Max(definition.MeasureSize, desired);
                }
                continue;
            }

            RegisterSpanRequest(spanRequests, start, span, desired);
        }

        foreach (var spanRequest in spanRequests.OrderBy(item => item.Key.Span))
        {
            EnsureMinSizeInDefinitionRange(definitions, spanRequest.Key.Start, spanRequest.Key.Span, spanRequest.Value);
        }

        foreach (var definition in definitions)
        {
            definition.MeasureSize = ClampDefinitionSize(definition, definition.MeasureSize);
        }
    }

    private static void InitializeMeasureSizes<T>(IReadOnlyList<T> definitions) where T : GridDefinitionBase
    {
        foreach (var definition in definitions)
        {
            if (definition.UserSize.IsAbsolute)
            {
                definition.MeasureSize = ClampDefinitionSize(definition, definition.UserSize.Value);
            }
            else
            {
                definition.MeasureSize = ClampDefinitionSize(definition, definition.UserMinSize);
            }
        }
    }

    private static void RegisterSpanRequest(Dictionary<SpanKey, double> spanRequests, int start, int span, double desired)
    {
        var key = new SpanKey(start, span);
        if (spanRequests.TryGetValue(key, out double existing))
        {
            spanRequests[key] = Math.Max(existing, desired);
        }
        else
        {
            spanRequests.Add(key, desired);
        }
    }

    private static void EnsureMinSizeInDefinitionRange<T>(IReadOnlyList<T> definitions, int start, int span, double desired)
        where T : GridDefinitionBase
    {
        double current = 0;
        for (int i = start; i < start + span; i++)
        {
            current += definitions[i].MeasureSize;
        }

        double extra = Math.Max(0, desired - current);
        if (extra <= 0)
        {
            return;
        }

        var preferredDefinitions = new List<T>();
        var autoDefinitions = new List<T>();
        var starDefinitions = new List<T>();
        var otherDefinitions = new List<T>();
        for (int i = start; i < start + span; i++)
        {
            var definition = definitions[i];
            if (definition.UserSize.IsAuto)
            {
                autoDefinitions.Add(definition);
                preferredDefinitions.Add(definition);
            }
            else if (definition.UserSize.IsStar)
            {
                starDefinitions.Add(definition);
            }
            else
            {
                otherDefinitions.Add(definition);
            }
        }

        if (preferredDefinitions.Count > 0)
        {
            DistributeExtra(preferredDefinitions, extra, useStarWeights: false);
            return;
        }

        if (starDefinitions.Count > 0)
        {
            DistributeExtra(starDefinitions, extra, useStarWeights: true);
            return;
        }

        if (otherDefinitions.Count > 0)
        {
            DistributeExtra(otherDefinitions, extra, useStarWeights: false);
        }
    }

    private static void DistributeExtra<T>(IReadOnlyList<T> definitions, double extra, bool useStarWeights)
        where T : GridDefinitionBase
    {
        var remaining = extra;
        var pending = new List<T>(definitions);

        while (remaining > 0.01 && pending.Count > 0)
        {
            double totalWeight = pending.Sum(def => GetDefinitionWeight(def, useStarWeights));
            if (totalWeight <= 0)
            {
                break;
            }

            double applied = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var definition = pending[i];
                double before = definition.MeasureSize;
                double share = remaining * GetDefinitionWeight(definition, useStarWeights) / totalWeight;
                definition.MeasureSize = ClampDefinitionSize(definition, before + share);
                applied += definition.MeasureSize - before;

                if (definition.MeasureSize >= definition.UserMaxSize - 0.01)
                {
                    pending.RemoveAt(i);
                }
            }

            if (applied <= 0.01)
            {
                break;
            }

            remaining -= applied;
        }
    }

    private static double[] CreateMeasureConstraints<T>(IReadOnlyList<T> definitions, double available, double spacing)
        where T : GridDefinitionBase
    {
        if (double.IsPositiveInfinity(available))
        {
            return definitions.Select(def => def.MeasureSize).ToArray();
        }

        return CreateDistributedSizes(definitions, available, spacing, useMeasuredForStarWhenInfinite: false);
    }

    private static double[] CreateFinalSizes<T>(IReadOnlyList<T> definitions, double available, double spacing)
        where T : GridDefinitionBase
    {
        if (double.IsPositiveInfinity(available))
        {
            return definitions.Select(def => def.MeasureSize).ToArray();
        }

        return CreateDistributedSizes(definitions, available, spacing, useMeasuredForStarWhenInfinite: false);
    }

    private static double[] CreateDistributedSizes<T>(
        IReadOnlyList<T> definitions,
        double available,
        double spacing,
        bool useMeasuredForStarWhenInfinite)
        where T : GridDefinitionBase
    {
        var sizes = new double[definitions.Count];
        double gaps = definitions.Count > 1 ? (definitions.Count - 1) * spacing : 0;
        double usable = Math.Max(0, available - gaps);
        double occupied = 0;
        var starDefinitions = new List<T>();

        for (int i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (definition.UserSize.IsStar)
            {
                starDefinitions.Add(definition);
                sizes[i] = ClampDefinitionSize(definition, definition.UserMinSize);
            }
            else
            {
                sizes[i] = definition.MeasureSize;
                occupied += sizes[i];
            }
        }

        if (starDefinitions.Count == 0)
        {
            return sizes;
        }

        if (double.IsPositiveInfinity(available) && useMeasuredForStarWhenInfinite)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i].UserSize.IsStar)
                {
                    sizes[i] = definitions[i].MeasureSize;
                }
            }
            return sizes;
        }

        double remaining = Math.Max(0, usable - occupied);
        var unresolved = new List<T>(starDefinitions);
        double remainingWeight = unresolved.Sum(def => Math.Max(0, def.UserSize.Value));

        while (unresolved.Count > 0 && remainingWeight > 0)
        {
            bool constrained = false;
            for (int i = unresolved.Count - 1; i >= 0; i--)
            {
                var definition = unresolved[i];
                int index = IndexOfDefinition(definitions, definition);
                double proposed = remaining * definition.UserSize.Value / remainingWeight;
                double clamped = ClampDefinitionSize(definition, proposed);

                if (clamped <= definition.UserMinSize + 0.01 || clamped >= definition.UserMaxSize - 0.01)
                {
                    sizes[index] = clamped;
                    remaining -= clamped;
                    remainingWeight -= Math.Max(0, definition.UserSize.Value);
                    unresolved.RemoveAt(i);
                    constrained = true;
                }
            }

            if (!constrained)
            {
                break;
            }
        }

        if (remainingWeight > 0)
        {
            foreach (var definition in unresolved)
            {
                int index = IndexOfDefinition(definitions, definition);
                double proposed = remaining * definition.UserSize.Value / remainingWeight;
                sizes[index] = ClampDefinitionSize(definition, proposed);
            }
        }

        return sizes;
    }

    private static int IndexOfDefinition<T>(IReadOnlyList<T> definitions, T definition)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            if (ReferenceEquals(definitions[i], definition))
            {
                return i;
            }
        }

        return -1;
    }

    private static void ApplyFinalSizes<T>(IReadOnlyList<T> definitions, double[] sizes) where T : GridDefinitionBase
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            definitions[i].FinalSize = sizes[i];
        }
    }

    private static void CommitActualSizes(
        IReadOnlyList<ColumnDefinition> columns,
        IReadOnlyList<RowDefinition> rows,
        bool useFinal)
    {
        foreach (var column in columns)
        {
            column.ActualWidth = useFinal ? column.FinalSize : column.MeasureSize;
        }

        foreach (var row in rows)
        {
            row.ActualHeight = useFinal ? row.FinalSize : row.MeasureSize;
        }
    }

    private static void CalculateOffsets<T>(IReadOnlyList<T> definitions, double spacing) where T : GridDefinitionBase
    {
        double offset = 0;
        foreach (var definition in definitions)
        {
            definition.Offset = offset;
            offset += definition.FinalSize + spacing;
        }
    }

    private static double GetRangeSize<T>(IReadOnlyList<T> definitions, int start, int span, double spacing, bool useFinal)
        where T : GridDefinitionBase
    {
        double total = 0;
        for (int i = start; i < start + span; i++)
        {
            total += useFinal ? definitions[i].FinalSize : definitions[i].MeasureSize;
        }

        if (span > 1)
        {
            total += (span - 1) * spacing;
        }

        return total;
    }

    private static double GetRangeSize(double[] sizes, int start, int span, double spacing)
    {
        double total = 0;
        for (int i = start; i < start + span; i++)
        {
            total += sizes[i];
        }

        if (span > 1)
        {
            total += (span - 1) * spacing;
        }

        return total;
    }

    private static double SumMeasureSizes<T>(IReadOnlyList<T> definitions, double spacing) where T : GridDefinitionBase
    {
        double total = definitions.Sum(def => def.MeasureSize);
        if (definitions.Count > 1)
        {
            total += (definitions.Count - 1) * spacing;
        }
        return total;
    }

    private IEnumerable<Element> EnumerateVisibleChildren()
    {
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            yield return child;
        }
    }

    private static double GetDesiredSize(Element child, bool isColumn)
        => isColumn ? child.DesiredSize.Width : child.DesiredSize.Height;

    private static double ClampDefinitionSize(GridDefinitionBase definition, double size)
        => Math.Clamp(size, definition.UserMinSize, definition.UserMaxSize);

    private static double GetDefinitionWeight(GridDefinitionBase definition, bool useStarWeights)
    {
        if (useStarWeights && definition.UserSize.IsStar)
        {
            return Math.Max(0.0001, definition.UserSize.Value);
        }

        return 1;
    }

    private static void MarkOccupied(bool[,] occupied, Placement placement)
    {
        for (int row = placement.Row; row < placement.Row + placement.RowSpan; row++)
        {
            for (int column = placement.Column; column < placement.Column + placement.ColumnSpan; column++)
            {
                occupied[row, column] = true;
            }
        }
    }

    private static bool TryFindFirstFit(bool[,] occupied, int rowSpan, int columnSpan, out int row, out int column)
    {
        row = 0;
        column = 0;

        for (int r = 0; r < occupied.GetLength(0); r++)
        {
            for (int c = 0; c < occupied.GetLength(1); c++)
            {
                if (CanPlace(occupied, r, c, rowSpan, columnSpan))
                {
                    row = r;
                    column = c;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindInRow(bool[,] occupied, int row, int rowSpan, int columnSpan, out int column)
    {
        column = 0;
        for (int c = 0; c < occupied.GetLength(1); c++)
        {
            if (CanPlace(occupied, row, c, rowSpan, columnSpan))
            {
                column = c;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindInColumn(bool[,] occupied, int column, int rowSpan, int columnSpan, out int row)
    {
        row = 0;
        for (int r = 0; r < occupied.GetLength(0); r++)
        {
            if (CanPlace(occupied, r, column, rowSpan, columnSpan))
            {
                row = r;
                return true;
            }
        }

        return false;
    }

    private static bool CanPlace(bool[,] occupied, int row, int column, int rowSpan, int columnSpan)
    {
        if (row < 0 || column < 0)
        {
            return false;
        }

        if (row + rowSpan > occupied.GetLength(0) || column + columnSpan > occupied.GetLength(1))
        {
            return false;
        }

        for (int r = row; r < row + rowSpan; r++)
        {
            for (int c = column; c < column + columnSpan; c++)
            {
                if (occupied[r, c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private sealed class DefinitionCollection<T> : Collection<T> where T : GridDefinitionBase
    {
        private readonly Grid _owner;

        public DefinitionCollection(Grid owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.InsertItem(index, item);
            item.Attach(_owner);
            _owner.InvalidateMeasure();
        }

        protected override void RemoveItem(int index)
        {
            this[index].Detach(_owner);
            base.RemoveItem(index);
            _owner.InvalidateMeasure();
        }

        protected override void SetItem(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            this[index].Detach(_owner);
            base.SetItem(index, item);
            item.Attach(_owner);
            _owner.InvalidateMeasure();
        }

        protected override void ClearItems()
        {
            foreach (var item in this)
            {
                item.Detach(_owner);
            }

            base.ClearItems();
            _owner.InvalidateMeasure();
        }
    }

    private readonly record struct Placement(
        Element Child,
        int Row,
        int Column,
        int RowSpan,
        int ColumnSpan);

    private enum CellGroup
    {
        Group1,
        Group2,
        Group3,
        Group4,
    }

    private enum MeasureMode
    {
        Initial,
        Constrained,
    }

    private readonly record struct SpanKey(int Start, int Span);
}
