using System.Globalization;
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
    [FluentReplacesMember(nameof(Panel.Add))]
    [FluentReplacesMember(nameof(Panel.AddRange))]
    public static T Children<T>(this T panel, params Element[] children) where T : Panel
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(children);
        panel.AddRange(children);
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
    /// Sets horizontal orientation.
    /// </summary>
    /// <param name="panel">Target stack panel.</param>
    /// <returns>The stack panel for chaining.</returns>
    [FluentSetsConstant(nameof(StackPanel.Orientation), MewUI.Orientation.Horizontal)]
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
    [FluentSetsConstant(nameof(StackPanel.Orientation), MewUI.Orientation.Vertical)]
    public static StackPanel Vertical(this StackPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
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
    /// Sets horizontal orientation.
    /// </summary>
    /// <param name="panel">Target split panel.</param>
    /// <returns>The split panel for chaining.</returns>
    [FluentSetsConstant(nameof(SplitPanel.Orientation), MewUI.Orientation.Horizontal)]
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
    [FluentSetsConstant(nameof(SplitPanel.Orientation), MewUI.Orientation.Vertical)]
    public static SplitPanel Vertical(this SplitPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
        return panel;
    }

    #endregion

    #region WrapPanel

    /// <summary>
    /// Sets horizontal orientation.
    /// </summary>
    /// <param name="panel">Target wrap panel.</param>
    /// <returns>The wrap panel for chaining.</returns>
    [FluentSetsConstant(nameof(WrapPanel.Orientation), MewUI.Orientation.Horizontal)]
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
    [FluentSetsConstant(nameof(WrapPanel.Orientation), MewUI.Orientation.Vertical)]
    public static WrapPanel Vertical(this WrapPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
        return panel;
    }

    #endregion
}
