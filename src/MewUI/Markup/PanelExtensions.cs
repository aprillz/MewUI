using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extension methods for panels.
/// </summary>
public static class PanelExtensions
{
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
        panel.AddRange(children);
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
                var value = string.IsNullOrEmpty(valueStr) ? 1.0 : double.Parse(valueStr);
                yield return GridLength.Stars(value);
            }
            else
            {
                yield return GridLength.Pixels(double.Parse(trimmed));
            }
        }
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