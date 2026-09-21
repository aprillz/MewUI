using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Win32;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// A lightweight graphics context for text measurement only.
/// </summary>
internal sealed class GdiMeasurementContext : MeasureGraphicsContextBase, ITextAdvanceSource
{
    private readonly nint _hdc;
    private bool _disposed;

    public override double DpiScale { get; }

    double[] ITextAdvanceSource.GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font)
    {
        if (font is not IWin32TextFace face)
        {
            throw new ArgumentException("Font must be a Win32 text face.", nameof(font));
        }

        if (text.IsEmpty)
        {
            return [];
        }

        var advances = GC.AllocateUninitializedArray<double>(text.Length);
        if (!face.TryGetPrefixAdvances(text, DpiScale, advances))
        {
            throw new InvalidOperationException("The font could not report prefix advances.");
        }

        return advances;
    }

    bool ITextAdvanceSource.TryGetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, Span<double> destination)
        => font is IWin32TextFace face && face.TryGetPrefixAdvances(text, DpiScale, destination);

    public GdiMeasurementContext(nint hdc, uint dpi)
    {
        _hdc = hdc;
        DpiScale = dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            User32.ReleaseDC(0, _hdc);
            _disposed = true;
            base.Dispose();
        }
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => font is IWin32TextFace face
            ? face.Measure(text, double.PositiveInfinity, TextWrapping.NoWrap, DpiScale)
            : Size.Empty;

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => font is IWin32TextFace face
            ? face.Measure(text, maxWidth, TextWrapping.Wrap, DpiScale)
            : Size.Empty;
}
