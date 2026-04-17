using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Registers Skia GPU hosting services for a concrete MewVG backend assembly
/// without requiring that backend to reference SkiaSharp directly.
/// </summary>
public static class MewVGSkiaGraphicsServices
{
    public static void RegisterFactory<TFactory>()
        where TFactory : class, IMewVGWindowResourceResolver
        => GraphicsServiceRegistry.RegisterResolver(Resolver<TFactory>.Resolve);

    private static class Resolver<TFactory>
        where TFactory : class, IMewVGWindowResourceResolver
    {
        public static object? Resolve(object instance, Type serviceType)
        {
            if (serviceType != typeof(ISkiaGpuControlFactory) || instance is not TFactory factory)
            {
                return null;
            }

            return MewVGSkiaGraphicsFactoryAdapter.GetOrCreate(factory);
        }
    }
}

internal sealed class MewVGSkiaGraphicsFactoryAdapter : ISkiaGpuControlFactory
{
    private static readonly ConditionalWeakTable<IMewVGWindowResourceResolver, MewVGSkiaGraphicsFactoryAdapter> s_cache = new();

    private readonly IMewVGWindowResourceResolver _resolver;

    private MewVGSkiaGraphicsFactoryAdapter(IMewVGWindowResourceResolver resolver)
    {
        _resolver = resolver;
    }

    public static MewVGSkiaGraphicsFactoryAdapter GetOrCreate(IMewVGWindowResourceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return s_cache.GetValue(resolver, static key => new MewVGSkiaGraphicsFactoryAdapter(key));
    }

    public ISkiaGpuControlSurface CreateSkiaGpuControlSurface(
        nint windowHandle,
        int pixelWidth,
        int pixelHeight,
        double dpiScale)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A valid window handle is required for GPU-backed Skia surfaces.", nameof(windowHandle));
        }

        if (!_resolver.TryGetWindowResources(windowHandle, out var resources) || resources == null)
        {
            throw new InvalidOperationException(
                "Skia GPU surfaces require an active MewVG window render context. " +
                "Create the surface from an attached control during rendering.");
        }

        return MewVGSkiaGpuControlSurfaceFactory.Create(resources, pixelWidth, pixelHeight, dpiScale);
    }
}
