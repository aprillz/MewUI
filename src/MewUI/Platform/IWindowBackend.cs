using Aprillz.MewUI.Elements;

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

    void SetClientSize(double widthDip, double heightDip);

    void CaptureMouse(UIElement element);

    void ReleaseMouseCapture();
}
