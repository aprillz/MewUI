using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Rendering;

public static class RenderDeviceCompatibilityExtensions
{
    private static readonly ConditionalWeakTable<IGraphicsFactory, IRenderDevice> Devices = new();

    public static IRenderDevice AsRenderDevice(this IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (factory is IRenderDevice renderDevice)
        {
            return renderDevice;
        }

        return Devices.GetValue(factory, static f => new GraphicsFactoryRenderDeviceAdapter(f));
    }
}
