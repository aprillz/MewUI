using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

public interface IImageSource
{
    IImage CreateImage(IGraphicsFactory factory);
}

public interface INotifyImageChanged
{
    event Action? Changed;
}

