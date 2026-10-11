using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Infrastructure;

/// <summary>Restores the previous default factory without taking ownership of either factory.</summary>
internal sealed class DefaultGraphicsFactoryScope : IDisposable
{
    private readonly IGraphicsFactory _previousFactory;
    private bool _disposed;

    internal DefaultGraphicsFactoryScope(IGraphicsFactory factory)
    {
        _previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Application.DefaultGraphicsFactory = _previousFactory;
        _disposed = true;
    }
}
