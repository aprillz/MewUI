namespace Aprillz.MewUI.Rendering;

public static class RenderSurfaceAdapterExtensions
{
    public static IRenderSurface AsRenderSurface(
        this IBitmapRenderTarget target,
        RenderSurfaceDescriptor? descriptor = null,
        bool ownsTarget = false)
        => new BitmapRenderTargetSurfaceAdapter(target, descriptor, ownsTarget);
}
