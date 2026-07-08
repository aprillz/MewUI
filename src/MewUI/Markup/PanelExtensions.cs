using System.Globalization;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extension methods for panels.
/// </summary>
public static class PanelExtensions
{
    /// <summary>
    /// Sets whether child content is clipped to the panel bounds.
    /// </summary>
    /// <typeparam name="T">Panel type.</typeparam>
    /// <param name="panel">Target panel.</param>
    /// <param name="value">Whether child content is clipped.</param>
    /// <returns>The panel for chaining.</returns>
    public static T ClipToBounds<T>(this T panel, bool value = true) where T : Panel
    {
        panel.ClipToBounds = value;
        return panel;
    }

    #region Panel Base

    /// <summary>
    /// Adds children to the panel.
    /// </summary>
    /// <typeparam name="T">Panel type.</typeparam>
    /// <param name="panel">Target panel.</param>
    /// <param name="children">Child elements.</param>
    /// <returns>The panel for chaining.</returns>
    public static T Children<T>(this T panel, params Element[] children) where T : Panel
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(children);
        panel.AddRange(children);
        return panel;
    }

    /// <summary>
    /// Sets the padding.
    /// </summary>
    /// <typeparam name="T">Panel type.</typeparam>
    /// <param name="panel">Target panel.</param>
    /// <param name="padding">Padding thickness.</param>
    /// <returns>The panel for chaining.</returns>
    public static T Padding<T>(this T panel, Thickness padding) where T : Panel
    {
        panel.Padding = padding;
        return panel;
    }

    /// <summary>
    /// Sets uniform padding on all sides.
    /// </summary>
    /// <typeparam name="T">Panel type.</typeparam>
    /// <param name="panel">Target panel.</param>
    /// <param name="uniform">Uniform padding value.</param>
    /// <returns>The panel for chaining.</returns>
    public static T Padding<T>(this T panel, double uniform) where T : Panel
    {
        panel.Padding = new Thickness(uniform);
        return panel;
    }

    /// <summary>
    /// Sets horizontal and vertical padding.
    /// </summary>
    /// <typeparam name="T">Panel type.</typeparam>
    /// <param name="panel">Target panel.</param>
    /// <param name="horizontal">Left and right padding.</param>
    /// <param name="vertical">Top and bottom padding.</param>
    /// <returns>The panel for chaining.</returns>
    public static T Padding<T>(this T panel, double horizontal, double vertical) where T : Panel
    {
        panel.Padding = new Thickness(horizontal, vertical, horizontal, vertical);
        return panel;
    }

    /// <summary>
    /// Sets padding for each side individually.
    /// </summary>
    /// <typeparam name="T">Panel type.</typeparam>
    /// <param name="panel">Target panel.</param>
    /// <param name="left">Left padding.</param>
    /// <param name="top">Top padding.</param>
    /// <param name="right">Right padding.</param>
    /// <param name="bottom">Bottom padding.</param>
    /// <returns>The panel for chaining.</returns>
    public static T Padding<T>(this T panel, double left, double top, double right, double bottom) where T : Panel
    {
        panel.Padding = new Thickness(left, top, right, bottom);
        return panel;
    }

    #endregion

    #region StackPanel

    /// <summary>
    /// Sets the orientation.
    /// </summary>
    /// <param name="panel">Target stack panel.</param>
    /// <param name="orientation">Orientation value.</param>
    /// <returns>The stack panel for chaining.</returns>
    public static StackPanel Orientation(this StackPanel panel, Orientation orientation)
    {
        panel.Orientation = orientation;
        return panel;
    }

    /// <summary>
    /// Sets horizontal orientation.
    /// </summary>
    /// <param name="panel">Target stack panel.</param>
    /// <returns>The stack panel for chaining.</returns>
    public static StackPanel Horizontal(this StackPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Horizontal;
        return panel;
    }

    /// <summary>
    /// Sets vertical orientation.
    /// </summary>
    /// <param name="panel">Target stack panel.</param>
    /// <returns>The stack panel for chaining.</returns>
    public static StackPanel Vertical(this StackPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
        return panel;
    }

    /// <summary>
    /// Sets the spacing between items.
    /// </summary>
    /// <param name="panel">Target stack panel.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The stack panel for chaining.</returns>
    public static StackPanel Spacing(this StackPanel panel, double spacing)
    {
        panel.Spacing = spacing;
        return panel;
    }

    #endregion

    #region Grid

    /// <summary>
    /// Defines rows for the grid.
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="rows">Row definitions.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid Rows(this Grid grid, params GridLength[] rows)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(rows);
        grid.RowDefinitions.Clear();
        foreach (var row in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = row });
        }

        return grid;
    }

    /// <summary>
    /// Defines columns for the grid.
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="columns">Column definitions.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid Columns(this Grid grid, params GridLength[] columns)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(columns);
        grid.ColumnDefinitions.Clear();
        foreach (var col in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = col });
        }

        return grid;
    }

    /// <summary>
    /// Defines rows using string syntax: "Auto,*,2*,100"
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="definition">Row definition string.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid Rows(this Grid grid, string definition)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(definition);
        grid.RowDefinitions.Clear();
        foreach (var length in ParseGridLengths(definition))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = length });
        }

        return grid;
    }

    /// <summary>
    /// Defines columns using string syntax: "Auto,*,2*,100"
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="definition">Column definition string.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid Columns(this Grid grid, string definition)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(definition);
        grid.ColumnDefinitions.Clear();
        foreach (var length in ParseGridLengths(definition))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        }

        return grid;
    }

    /// <summary>
    /// Sets the spacing between cells.
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid Spacing(this Grid grid, double spacing)
    {
        grid.Spacing = spacing;
        return grid;
    }

    /// <summary>
    /// Sets auto indexing for child elements.
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="autoIndexing">Auto indexing enabled.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid AutoIndexing(this Grid grid, bool autoIndexing = true)
    {
        grid.AutoIndexing = autoIndexing;
        return grid;
    }

    /// <summary>
    /// Sets whether grid lines are shown.
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="showGridLine">Whether grid lines are shown.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid ShowGridLine(this Grid grid, bool showGridLine = true)
    {
        grid.ShowGridLine = showGridLine;
        return grid;
    }

    /// <summary>
    /// Sets whether star-sized rows and columns share available space.
    /// </summary>
    /// <param name="grid">Target grid.</param>
    /// <param name="shareStarSize">Whether star sizes are shared.</param>
    /// <returns>The grid for chaining.</returns>
    public static Grid ShareStarSize(this Grid grid, bool shareStarSize = true)
    {
        grid.ShareStarSize = shareStarSize;
        return grid;
    }

    private static IEnumerable<GridLength> ParseGridLengths(string definition)
    {
        var parts = definition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                yield return GridLength.Auto;
            }
            else if (trimmed.EndsWith('*'))
            {
                var valueStr = trimmed[..^1];
                if (valueStr.Length == 0)
                {
                    yield return GridLength.Stars(1.0);
                }
                else if (double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var stars))
                {
                    yield return GridLength.Stars(stars);
                }
                else
                {
                    throw new FormatException($"Invalid grid length '{trimmed}'.");
                }
            }
            else if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
            {
                yield return GridLength.Pixels(pixels);
            }
            else
            {
                throw new FormatException($"Invalid grid length '{trimmed}'.");
            }
        }
    }

    #endregion

    #region SplitPanel

    /// <summary>
    /// Sets the orientation.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="orientation">Orientation value.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel Orientation(this SplitPanel panel, Orientation orientation)
    {
        panel.Orientation = orientation;
        return panel;
    }

    /// <summary>
    /// Sets horizontal orientation.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel Horizontal(this SplitPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Horizontal;
        return panel;
    }

    /// <summary>
    /// Sets vertical orientation.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel Vertical(this SplitPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
        return panel;
    }

    /// <summary>
    /// Sets the splitter thickness.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="thickness">Splitter thickness in DIPs.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel SplitterThickness(this SplitPanel panel, double thickness)
    {
        panel.SplitterThickness = thickness;
        return panel;
    }

    /// <summary>
    /// Sets the first pane length.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="length">Grid length for the first pane.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel FirstLength(this SplitPanel panel, GridLength length)
    {
        panel.FirstLength = length;
        return panel;
    }

    /// <summary>
    /// Sets the second pane length.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="length">Grid length for the second pane.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel SecondLength(this SplitPanel panel, GridLength length)
    {
        panel.SecondLength = length;
        return panel;
    }

    /// <summary>
    /// Sets the minimum size for the first pane.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="value">Minimum size in DIPs.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel MinFirst(this SplitPanel panel, double value)
    {
        panel.MinFirst = value;
        return panel;
    }

    /// <summary>
    /// Sets the minimum size for the second pane.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="value">Minimum size in DIPs.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel MinSecond(this SplitPanel panel, double value)
    {
        panel.MinSecond = value;
        return panel;
    }

    /// <summary>
    /// Sets the maximum size for the first pane.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="value">Maximum size in DIPs.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel MaxFirst(this SplitPanel panel, double value)
    {
        panel.MaxFirst = value;
        return panel;
    }

    /// <summary>
    /// Sets the maximum size for the second pane.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="value">Maximum size in DIPs.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel MaxSecond(this SplitPanel panel, double value)
    {
        panel.MaxSecond = value;
        return panel;
    }

    /// <summary>
    /// Sets the first pane content.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="element">Content element.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel First(this SplitPanel panel, UIElement? element)
    {
        panel.First = element;
        return panel;
    }

    /// <summary>
    /// Sets the second pane content.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <param name="element">Content element.</param>
    /// <returns>The split panel for chaining.</returns>
    public static SplitPanel Second(this SplitPanel panel, UIElement? element)
    {
        panel.Second = element;
        return panel;
    }

    #endregion

    #region UniformGrid

    /// <summary>
    /// Sets the number of rows.
    /// </summary>
    /// <param name="grid">Target uniform grid.</param>
    /// <param name="rows">Number of rows.</param>
    /// <returns>The uniform grid for chaining.</returns>
    public static UniformGrid Rows(this UniformGrid grid, int rows)
    {
        grid.Rows = rows;
        return grid;
    }

    /// <summary>
    /// Sets the number of columns.
    /// </summary>
    /// <param name="grid">Target uniform grid.</param>
    /// <param name="columns">Number of columns.</param>
    /// <returns>The uniform grid for chaining.</returns>
    public static UniformGrid Columns(this UniformGrid grid, int columns)
    {
        grid.Columns = columns;
        return grid;
    }

    /// <summary>
    /// Sets the spacing between cells.
    /// </summary>
    /// <param name="grid">Target uniform grid.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The uniform grid for chaining.</returns>
    public static UniformGrid Spacing(this UniformGrid grid, double spacing)
    {
        grid.Spacing = spacing;
        return grid;
    }

    #endregion

    #region WrapPanel

    /// <summary>
    /// Sets the orientation.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <param name="orientation">Orientation value.</param>
    /// <returns>The wrap panel for chaining.</returns>
    public static WrapPanel Orientation(this WrapPanel panel, Orientation orientation)
    {
        panel.Orientation = orientation;
        return panel;
    }

    /// <summary>
    /// Sets horizontal orientation.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <returns>The wrap panel for chaining.</returns>
    public static WrapPanel Horizontal(this WrapPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Horizontal;
        return panel;
    }

    /// <summary>
    /// Sets vertical orientation.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <returns>The wrap panel for chaining.</returns>
    public static WrapPanel Vertical(this WrapPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
        return panel;
    }

    /// <summary>
    /// Sets the spacing between items.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The wrap panel for chaining.</returns>
    public static WrapPanel Spacing(this WrapPanel panel, double spacing)
    {
        panel.Spacing = spacing;
        return panel;
    }

    /// <summary>
    /// Sets the item width.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <param name="width">Item width.</param>
    /// <returns>The wrap panel for chaining.</returns>
    public static WrapPanel ItemWidth(this WrapPanel panel, double width)
    {
        panel.ItemWidth = width;
        return panel;
    }

    /// <summary>
    /// Sets the item height.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <param name="height">Item height.</param>
    /// <returns>The wrap panel for chaining.</returns>
    public static WrapPanel ItemHeight(this WrapPanel panel, double height)
    {
        panel.ItemHeight = height;
        return panel;
    }

    #endregion

    #region DockPanel

    /// <summary>
    /// Sets whether the last child fills remaining space.
    /// </summary>
    /// <param name="panel">Target dock panel.</param>
    /// <param name="lastChildFill">Last child fill enabled.</param>
    /// <returns>The dock panel for chaining.</returns>
    public static DockPanel LastChildFill(this DockPanel panel, bool lastChildFill = true)
    {
        panel.LastChildFill = lastChildFill;
        return panel;
    }

    /// <summary>
    /// Sets the spacing between items.
    /// </summary>
    /// <param name="panel">Target dock panel.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The dock panel for chaining.</returns>
    public static DockPanel Spacing(this DockPanel panel, double spacing)
    {
        panel.Spacing = spacing;
        return panel;
    }

    #endregion
}
