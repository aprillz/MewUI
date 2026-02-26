using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// GDI solid-color brush resource.  Owns a single <c>HBRUSH</c> created by
/// <see cref="Gdi32.CreateSolidBrush"/>.
/// </summary>
internal sealed class GdiSolidColorBrush : ISolidColorBrush
{
    private bool _disposed;

    /// <inheritdoc/>
    public Color Color { get; }

    /// <summary>Gets the underlying GDI brush handle.</summary>
    internal nint HBrush { get; }

    public GdiSolidColorBrush(Color color)
    {
        Color = color;
        HBrush = Gdi32.CreateSolidBrush(color.ToCOLORREF());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (HBrush != 0)
                Gdi32.DeleteObject(HBrush);
            _disposed = true;
        }
    }
}
