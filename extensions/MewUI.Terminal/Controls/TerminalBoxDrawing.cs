// Taken from Mewtty TerminalBoxDrawing, extended with the heavy and double lines and the block elements of
// Windows Terminal src/renderer/atlas/BuiltinGlyphs.cpp (MIT).

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Terminal;

/// <summary>Draws line-drawing and block characters as shapes that meet the cell edges, so lines never break between cells.</summary>
internal static class TerminalBoxDrawing
{
    // Each direction takes two bits: 0 none, 1 light, 2 heavy, 3 double.
    private const int UP_SHIFT = 0;
    private const int DOWN_SHIFT = 2;
    private const int LEFT_SHIFT = 4;
    private const int RIGHT_SHIFT = 6;

    private const int NONE = 0;
    private const int LIGHT = 1;
    private const int HEAVY = 2;
    private const int DOUBLE = 3;

    private static readonly byte[] _lineTable = BuildLineTable();

    public static bool Handles(char character) =>
        (character >= '─' && character <= '╿' && _lineTable[character - '─'] != 0)
        || (character >= '▀' && character <= '▟')
        || (character >= '⎺' && character <= '⎽');

    public static void Draw(IGraphicsContext context, char character, Rect cell, Color color, double scale)
    {
        cell = LayoutRounding.SnapRectEdgesToPixels(cell, scale);
        double thin = Math.Max(1, Math.Round(scale)) / scale;

        if (character >= '▀' && character <= '▟')
        {
            DrawBlock(context, character, cell, color, scale);
            return;
        }

        if (character >= '⎺' && character <= '⎽')
        {
            // Horizontal scan lines 1, 3, 7 and 9 of nine.
            ReadOnlySpan<double> positions = [0.0, 0.25, 0.75, 1.0];
            double y = cell.Y + ((cell.Height - thin) * positions[character - '⎺']);
            context.FillRectangle(Snap(new Rect(cell.X, y, cell.Width, thin), scale), color);
            return;
        }

        byte lines = _lineTable[character - '─'];
        double heavy = Math.Max(2 * thin, Math.Round(cell.Width / 4 * scale) / scale);
        double centerX = Snap(cell.X + ((cell.Width - thin) / 2), scale);
        double centerY = Snap(cell.Y + ((cell.Height - thin) / 2), scale);

        DrawArm(context, (lines >> UP_SHIFT) & 3, vertical: true, from: cell.Y, to: centerY + thin, centerX, centerY, thin, heavy, color, scale);
        DrawArm(context, (lines >> DOWN_SHIFT) & 3, vertical: true, from: centerY, to: cell.Bottom, centerX, centerY, thin, heavy, color, scale);
        DrawArm(context, (lines >> LEFT_SHIFT) & 3, vertical: false, from: cell.X, to: centerX + thin, centerX, centerY, thin, heavy, color, scale);
        DrawArm(context, (lines >> RIGHT_SHIFT) & 3, vertical: false, from: centerX, to: cell.Right, centerX, centerY, thin, heavy, color, scale);
    }

    /// <summary>Draws one arm from the cell edge to the center: a thin, thick or doubled line.</summary>
    private static void DrawArm(IGraphicsContext context, int weight, bool vertical, double from, double to, double centerX, double centerY,
        double thin, double heavy, Color color, double scale)
    {
        if (weight == NONE)
        {
            return;
        }

        if (weight == DOUBLE)
        {
            // Two thin lines one line width either side of the center line; arms overlap at the center.
            double offset = 2 * thin;
            if (vertical)
            {
                context.FillRectangle(new Rect(centerX - offset, from - offset, thin, to - from + (2 * offset)), color);
                context.FillRectangle(new Rect(centerX + offset, from - offset, thin, to - from + (2 * offset)), color);
            }
            else
            {
                context.FillRectangle(new Rect(from - offset, centerY - offset, to - from + (2 * offset), thin), color);
                context.FillRectangle(new Rect(from - offset, centerY + offset, to - from + (2 * offset), thin), color);
            }

            return;
        }

        double width = weight == HEAVY ? heavy : thin;
        double shift = Snap((width - thin) / 2, scale);
        if (vertical)
        {
            context.FillRectangle(new Rect(centerX - shift, from, width, to - from), color);
        }
        else
        {
            context.FillRectangle(new Rect(from, centerY - shift, to - from, width), color);
        }
    }

    private static void DrawBlock(IGraphicsContext context, char character, Rect cell, Color color, double scale)
    {
        double eighthWidth = cell.Width / 8;
        double eighthHeight = cell.Height / 8;
        switch (character)
        {
            case '▀':
                // Upper half.
                Fill(context, cell.X, cell.Y, cell.Width, cell.Height / 2, color, scale);
                break;
            case >= '▁' and <= '█':
            {
                // Lower one eighth to full block.
                double height = eighthHeight * (character - '▀');
                Fill(context, cell.X, cell.Bottom - height, cell.Width, height, color, scale);
                break;
            }

            case >= '▉' and <= '▏':
            {
                // Left seven eighths to one eighth.
                double width = eighthWidth * ('▐' - character);
                Fill(context, cell.X, cell.Y, width, cell.Height, color, scale);
                break;
            }

            case '▐':
                Fill(context, cell.X + (cell.Width / 2), cell.Y, cell.Width / 2, cell.Height, color, scale);
                break;
            case '░':
            case '▒':
            case '▓':
            {
                // Shades are the color at a quarter, a half and three quarters strength.
                byte alpha = (byte)(color.A * (character - '▐') / 4);
                context.FillRectangle(cell, Color.FromArgb(alpha, color.R, color.G, color.B));
                break;
            }

            case '▔':
                Fill(context, cell.X, cell.Y, cell.Width, eighthHeight, color, scale);
                break;
            case '▕':
                Fill(context, cell.Right - eighthWidth, cell.Y, eighthWidth, cell.Height, color, scale);
                break;
            default:
                DrawQuadrants(context, character, cell, color, scale);
                break;
        }
    }

    /// <summary>The quadrant blocks U+2596 to U+259F, as a bit set of upper-left, upper-right, lower-left, lower-right.</summary>
    private static void DrawQuadrants(IGraphicsContext context, char character, Rect cell, Color color, double scale)
    {
        ReadOnlySpan<byte> quadrants = [0b0100, 0b1000, 0b0001, 0b1101, 0b1001, 0b0111, 0b1011, 0b0010, 0b0110, 0b1110];
        int index = character - '▖';
        if (index < 0 || index >= quadrants.Length)
        {
            return;
        }

        int mask = quadrants[index];
        double halfWidth = cell.Width / 2;
        double halfHeight = cell.Height / 2;
        if ((mask & 0b0001) != 0)
        {
            Fill(context, cell.X, cell.Y, halfWidth, halfHeight, color, scale);
        }

        if ((mask & 0b0010) != 0)
        {
            Fill(context, cell.X + halfWidth, cell.Y, halfWidth, halfHeight, color, scale);
        }

        if ((mask & 0b0100) != 0)
        {
            Fill(context, cell.X, cell.Y + halfHeight, halfWidth, halfHeight, color, scale);
        }

        if ((mask & 0b1000) != 0)
        {
            Fill(context, cell.X + halfWidth, cell.Y + halfHeight, halfWidth, halfHeight, color, scale);
        }
    }

    private static void Fill(IGraphicsContext context, double left, double top, double width, double height, Color color, double scale) =>
        context.FillRectangle(Snap(new Rect(left, top, width, height), scale), color);

    private static Rect Snap(Rect rect, double scale) => LayoutRounding.SnapRectEdgesToPixels(rect, scale);

    private static double Snap(double value, double scale) => Math.Round(value * scale) / scale;

    private static byte Line(int up, int down, int left, int right) =>
        (byte)((up << UP_SHIFT) | (down << DOWN_SHIFT) | (left << LEFT_SHIFT) | (right << RIGHT_SHIFT));

    /// <summary>The arms of U+2500 to U+257F that are straight lines; dashed, rounded and diagonal ones are drawn by the font.</summary>
    private static byte[] BuildLineTable()
    {
        var table = new byte[0x80];
        table[0x00] = Line(NONE, NONE, LIGHT, LIGHT);
        table[0x01] = Line(NONE, NONE, HEAVY, HEAVY);
        table[0x02] = Line(LIGHT, LIGHT, NONE, NONE);
        table[0x03] = Line(HEAVY, HEAVY, NONE, NONE);
        table[0x0C] = Line(NONE, LIGHT, NONE, LIGHT);
        table[0x0D] = Line(NONE, LIGHT, NONE, HEAVY);
        table[0x0E] = Line(NONE, HEAVY, NONE, LIGHT);
        table[0x0F] = Line(NONE, HEAVY, NONE, HEAVY);
        table[0x10] = Line(NONE, LIGHT, LIGHT, NONE);
        table[0x11] = Line(NONE, LIGHT, HEAVY, NONE);
        table[0x12] = Line(NONE, HEAVY, LIGHT, NONE);
        table[0x13] = Line(NONE, HEAVY, HEAVY, NONE);
        table[0x14] = Line(LIGHT, NONE, NONE, LIGHT);
        table[0x15] = Line(LIGHT, NONE, NONE, HEAVY);
        table[0x16] = Line(HEAVY, NONE, NONE, LIGHT);
        table[0x17] = Line(HEAVY, NONE, NONE, HEAVY);
        table[0x18] = Line(LIGHT, NONE, LIGHT, NONE);
        table[0x19] = Line(LIGHT, NONE, HEAVY, NONE);
        table[0x1A] = Line(HEAVY, NONE, LIGHT, NONE);
        table[0x1B] = Line(HEAVY, NONE, HEAVY, NONE);
        table[0x1C] = Line(LIGHT, LIGHT, NONE, LIGHT);
        table[0x1D] = Line(LIGHT, LIGHT, NONE, HEAVY);
        table[0x1E] = Line(HEAVY, LIGHT, NONE, LIGHT);
        table[0x1F] = Line(LIGHT, HEAVY, NONE, LIGHT);
        table[0x20] = Line(HEAVY, HEAVY, NONE, LIGHT);
        table[0x21] = Line(HEAVY, LIGHT, NONE, HEAVY);
        table[0x22] = Line(LIGHT, HEAVY, NONE, HEAVY);
        table[0x23] = Line(HEAVY, HEAVY, NONE, HEAVY);
        table[0x24] = Line(LIGHT, LIGHT, LIGHT, NONE);
        table[0x25] = Line(LIGHT, LIGHT, HEAVY, NONE);
        table[0x26] = Line(HEAVY, LIGHT, LIGHT, NONE);
        table[0x27] = Line(LIGHT, HEAVY, LIGHT, NONE);
        table[0x28] = Line(HEAVY, HEAVY, LIGHT, NONE);
        table[0x29] = Line(HEAVY, LIGHT, HEAVY, NONE);
        table[0x2A] = Line(LIGHT, HEAVY, HEAVY, NONE);
        table[0x2B] = Line(HEAVY, HEAVY, HEAVY, NONE);
        table[0x2C] = Line(NONE, LIGHT, LIGHT, LIGHT);
        table[0x2D] = Line(NONE, LIGHT, HEAVY, LIGHT);
        table[0x2E] = Line(NONE, LIGHT, LIGHT, HEAVY);
        table[0x2F] = Line(NONE, LIGHT, HEAVY, HEAVY);
        table[0x30] = Line(NONE, HEAVY, LIGHT, LIGHT);
        table[0x31] = Line(NONE, HEAVY, HEAVY, LIGHT);
        table[0x32] = Line(NONE, HEAVY, LIGHT, HEAVY);
        table[0x33] = Line(NONE, HEAVY, HEAVY, HEAVY);
        table[0x34] = Line(LIGHT, NONE, LIGHT, LIGHT);
        table[0x35] = Line(LIGHT, NONE, HEAVY, LIGHT);
        table[0x36] = Line(LIGHT, NONE, LIGHT, HEAVY);
        table[0x37] = Line(LIGHT, NONE, HEAVY, HEAVY);
        table[0x38] = Line(HEAVY, NONE, LIGHT, LIGHT);
        table[0x39] = Line(HEAVY, NONE, HEAVY, LIGHT);
        table[0x3A] = Line(HEAVY, NONE, LIGHT, HEAVY);
        table[0x3B] = Line(HEAVY, NONE, HEAVY, HEAVY);
        table[0x3C] = Line(LIGHT, LIGHT, LIGHT, LIGHT);
        table[0x3D] = Line(LIGHT, LIGHT, HEAVY, LIGHT);
        table[0x3E] = Line(LIGHT, LIGHT, LIGHT, HEAVY);
        table[0x3F] = Line(LIGHT, LIGHT, HEAVY, HEAVY);
        table[0x40] = Line(HEAVY, LIGHT, LIGHT, LIGHT);
        table[0x41] = Line(LIGHT, HEAVY, LIGHT, LIGHT);
        table[0x42] = Line(HEAVY, HEAVY, LIGHT, LIGHT);
        table[0x43] = Line(HEAVY, LIGHT, HEAVY, LIGHT);
        table[0x44] = Line(HEAVY, LIGHT, LIGHT, HEAVY);
        table[0x45] = Line(LIGHT, HEAVY, HEAVY, LIGHT);
        table[0x46] = Line(LIGHT, HEAVY, LIGHT, HEAVY);
        table[0x47] = Line(HEAVY, LIGHT, HEAVY, HEAVY);
        table[0x48] = Line(LIGHT, HEAVY, HEAVY, HEAVY);
        table[0x49] = Line(HEAVY, HEAVY, HEAVY, LIGHT);
        table[0x4A] = Line(HEAVY, HEAVY, LIGHT, HEAVY);
        table[0x4B] = Line(HEAVY, HEAVY, HEAVY, HEAVY);
        table[0x50] = Line(NONE, NONE, DOUBLE, DOUBLE);
        table[0x51] = Line(DOUBLE, DOUBLE, NONE, NONE);
        table[0x52] = Line(NONE, LIGHT, NONE, DOUBLE);
        table[0x53] = Line(NONE, DOUBLE, NONE, LIGHT);
        table[0x54] = Line(NONE, DOUBLE, NONE, DOUBLE);
        table[0x55] = Line(NONE, LIGHT, DOUBLE, NONE);
        table[0x56] = Line(NONE, DOUBLE, LIGHT, NONE);
        table[0x57] = Line(NONE, DOUBLE, DOUBLE, NONE);
        table[0x58] = Line(LIGHT, NONE, NONE, DOUBLE);
        table[0x59] = Line(DOUBLE, NONE, NONE, LIGHT);
        table[0x5A] = Line(DOUBLE, NONE, NONE, DOUBLE);
        table[0x5B] = Line(LIGHT, NONE, DOUBLE, NONE);
        table[0x5C] = Line(DOUBLE, NONE, LIGHT, NONE);
        table[0x5D] = Line(DOUBLE, NONE, DOUBLE, NONE);
        table[0x5E] = Line(LIGHT, LIGHT, NONE, DOUBLE);
        table[0x5F] = Line(DOUBLE, DOUBLE, NONE, LIGHT);
        table[0x60] = Line(DOUBLE, DOUBLE, NONE, DOUBLE);
        table[0x61] = Line(LIGHT, LIGHT, DOUBLE, NONE);
        table[0x62] = Line(DOUBLE, DOUBLE, LIGHT, NONE);
        table[0x63] = Line(DOUBLE, DOUBLE, DOUBLE, NONE);
        table[0x64] = Line(NONE, LIGHT, DOUBLE, DOUBLE);
        table[0x65] = Line(NONE, DOUBLE, LIGHT, LIGHT);
        table[0x66] = Line(NONE, DOUBLE, DOUBLE, DOUBLE);
        table[0x67] = Line(LIGHT, NONE, DOUBLE, DOUBLE);
        table[0x68] = Line(DOUBLE, NONE, LIGHT, LIGHT);
        table[0x69] = Line(DOUBLE, NONE, DOUBLE, DOUBLE);
        table[0x6A] = Line(LIGHT, LIGHT, DOUBLE, DOUBLE);
        table[0x6B] = Line(DOUBLE, DOUBLE, LIGHT, LIGHT);
        table[0x6C] = Line(DOUBLE, DOUBLE, DOUBLE, DOUBLE);
        table[0x74] = Line(NONE, NONE, LIGHT, NONE);
        table[0x75] = Line(LIGHT, NONE, NONE, NONE);
        table[0x76] = Line(NONE, NONE, NONE, LIGHT);
        table[0x77] = Line(NONE, LIGHT, NONE, NONE);
        table[0x78] = Line(NONE, NONE, HEAVY, NONE);
        table[0x79] = Line(HEAVY, NONE, NONE, NONE);
        table[0x7A] = Line(NONE, NONE, NONE, HEAVY);
        table[0x7B] = Line(NONE, HEAVY, NONE, NONE);
        table[0x7C] = Line(NONE, NONE, LIGHT, HEAVY);
        table[0x7D] = Line(LIGHT, HEAVY, NONE, NONE);
        table[0x7E] = Line(NONE, NONE, HEAVY, LIGHT);
        table[0x7F] = Line(HEAVY, LIGHT, NONE, NONE);
        return table;
    }
}
