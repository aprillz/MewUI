namespace Aprillz.MewUI.Controls;

internal readonly record struct GridViewColumnWidthRequest(
    GridLength Width,
    double AutoDesiredWidth,
    double MinWidth,
    double MaxWidth);

/// <summary>
/// Resolves GridView column declarations into concrete widths without depending on UI layout state.
/// </summary>
internal static class GridViewColumnWidthResolver
{
    public static double Resolve(
        IReadOnlyList<GridViewColumnWidthRequest> columns,
        double availableWidth,
        Span<double> resolvedWidths)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (resolvedWidths.Length < columns.Count)
        {
            throw new ArgumentException("The destination span is shorter than the column list.", nameof(resolvedWidths));
        }

        bool finite = !double.IsPositiveInfinity(availableWidth);
        if (finite && (double.IsNaN(availableWidth) || availableWidth < 0))
        {
            availableWidth = 0;
        }

        var starIndices = new List<int>();
        double occupied = 0;

        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            double width;

            if (column.Width.IsStar)
            {
                starIndices.Add(i);
                width = column.MinWidth;
            }
            else if (column.Width.IsAuto)
            {
                width = Clamp(column.AutoDesiredWidth, column.MinWidth, column.MaxWidth);
                occupied += width;
            }
            else
            {
                width = Clamp(column.Width.Value, column.MinWidth, column.MaxWidth);
                occupied += width;
            }

            resolvedWidths[i] = width;
        }

        if (starIndices.Count == 0 || !finite)
        {
            return Sum(resolvedWidths, columns.Count);
        }

        double remaining = Math.Max(0, availableWidth - occupied);
        var unresolved = new List<int>(starIndices);
        double remainingWeight = 0;
        for (int i = 0; i < unresolved.Count; i++)
        {
            remainingWeight += columns[unresolved[i]].Width.Value;
        }

        while (unresolved.Count > 0 && remainingWeight > 0)
        {
            bool constrained = false;

            for (int i = unresolved.Count - 1; i >= 0; i--)
            {
                int index = unresolved[i];
                var column = columns[index];
                double proposed = remaining * column.Width.Value / remainingWeight;
                double clamped = Clamp(proposed, column.MinWidth, column.MaxWidth);

                if (!clamped.Equals(proposed))
                {
                    resolvedWidths[index] = clamped;
                    remaining = Math.Max(0, remaining - clamped);
                    remainingWeight -= column.Width.Value;
                    unresolved.RemoveAt(i);
                    constrained = true;
                }
            }

            if (constrained)
            {
                continue;
            }

            for (int i = 0; i < unresolved.Count; i++)
            {
                int index = unresolved[i];
                var column = columns[index];
                resolvedWidths[index] = remaining * column.Width.Value / remainingWeight;
            }
            unresolved.Clear();
        }

        // A positive Star weight is validated when columns are registered. Keep a defensive
        // fallback so malformed internal requests still resolve to their minimums.
        for (int i = 0; i < unresolved.Count; i++)
        {
            int index = unresolved[i];
            resolvedWidths[index] = columns[index].MinWidth;
        }

        return Sum(resolvedWidths, columns.Count);
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(Math.Max(value, min), max);

    private static double Sum(Span<double> widths, int count)
    {
        double total = 0;
        for (int i = 0; i < count; i++)
        {
            total += Math.Max(0, widths[i]);
        }
        return total;
    }
}
