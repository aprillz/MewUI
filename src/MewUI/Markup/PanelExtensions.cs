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
    public static T Children<T>(this T panel, params Element[] children) where T : Panel
    {
        panel.AddRange(children);
        return panel;
    }

    #endregion

    #region StackPanel

    public static StackPanel Orientation(this StackPanel panel, Orientation orientation)
    {
        panel.Orientation = orientation;
        return panel;
    }

    public static StackPanel Horizontal(this StackPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Horizontal;
        return panel;
    }

    public static StackPanel Vertical(this StackPanel panel)
    {
        panel.Orientation = MewUI.Orientation.Vertical;
        return panel;
    }

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
    public static Grid Columns(this Grid grid, string definition)
    {
        grid.ColumnDefinitions.Clear();
        foreach (var length in ParseGridLengths(definition))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        }

        return grid;
    }

    public static Grid Spacing(this Grid grid, double spacing)
    {
        grid.Spacing = spacing;
        return grid;
    }

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

    public static UniformGrid Rows(this UniformGrid grid, int rows)
    {
        grid.Rows = rows;
        return grid;
    }

    public static UniformGrid Columns(this UniformGrid grid, int columns)
    {
        grid.Columns = columns;
        return grid;
    }

    public static UniformGrid Spacing(this UniformGrid grid, double spacing)
    {
        grid.Spacing = spacing;
        return grid;
    }

    #endregion

    #region WrapPanel

    public static WrapPanel Orientation(this WrapPanel panel, Orientation orientation)
    {
        panel.Orientation = orientation;
        return panel;
    }

    public static WrapPanel Spacing(this WrapPanel panel, double spacing)
    {
        panel.Spacing = spacing;
        return panel;
    }

    public static WrapPanel ItemWidth(this WrapPanel panel, double width)
    {
        panel.ItemWidth = width;
        return panel;
    }

    public static WrapPanel ItemHeight(this WrapPanel panel, double height)
    {
        panel.ItemHeight = height;
        return panel;
    }

    #endregion

    #region DockPanel

    public static DockPanel LastChildFill(this DockPanel panel, bool lastChildFill = true)
    {
        panel.LastChildFill = lastChildFill;
        return panel;
    }

    public static DockPanel Spacing(this DockPanel panel, double spacing)
    {
        panel.Spacing = spacing;
        return panel;
    }

    #endregion
}