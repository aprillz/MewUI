using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Platform;

public interface IWindowBackend : IDisposable
{
    nint Handle { get; }

    void SetResizable(bool resizable);

    void Show();

    void Hide();

    void Close();

    void Invalidate(bool erase);

    void SetTitle(string title);

    void SetIcon(IconSource? icon);

    void SetClientSize(double widthDip, double heightDip);

    void CaptureMouse(UIElement element);

    void ReleaseMouseCapture();

    /// <summary>
    /// Converts a point from window client coordinates (DIPs) to screen coordinates (device pixels).
    /// </summary>
    Point ClientToScreen(Point clientPointDip);

    /// <summary>
    /// Converts a point from screen coordinates (device pixels) to window client coordinates (DIPs).
    /// </summary>
    Point ScreenToClient(Point screenPointPx);
}
