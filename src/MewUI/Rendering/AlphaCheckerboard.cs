namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Fills a rectangle with a two-tone checkerboard pattern used to preview
/// transparency behind alpha-aware color samples.
/// </summary>
public static class AlphaCheckerboard
{
    private static readonly Color LightCellA = Color.FromRgb(255, 255, 255);
    private static readonly Color LightCellB = Color.FromRgb(204, 204, 204);
    private static readonly Color DarkCellA = Color.FromRgb(68, 68, 68);
    private static readonly Color DarkCellB = Color.FromRgb(100, 100, 100);

    /// <summary>
    /// Fills <paramref name="rect"/> with an alpha-preview checkerboard.
    /// Pass <paramref name="isDark"/> = <see langword="true"/> to use dark-theme cells.
    /// </summary>
    public static void Fill(IGraphicsContext context, Rect rect, bool isDark, double cellSize = 8)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || cellSize <= 0)
            return;

        var (cellA, cellB) = isDark
            ? (DarkCellA, DarkCellB)
            : (LightCellA, LightCellB);

        context.FillRectangle(rect, cellA);

        int cols = (int)Math.Ceiling(rect.Width / cellSize);
        int rows = (int)Math.Ceiling(rect.Height / cellSize);

        for (int row = 0; row < rows; row++)
        {
            double y = rect.Y + row * cellSize;
            double h = Math.Min(cellSize, rect.Bottom - y);
            for (int col = (row & 1); col < cols; col += 2)
            {
                double x = rect.X + col * cellSize;
                double w = Math.Min(cellSize, rect.Right - x);
                context.FillRectangle(new Rect(x, y, w, h), cellB);
            }
        }
    }
}
