using System.Runtime.CompilerServices;

using Aprillz.MewVG;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

internal static class MewVGMetalExternalImageBridge
{
    private static readonly NVGimageFlags s_placeholderFlags = NVGimageFlags.Premultiplied;
    private static readonly NVGimageFlags s_externalImageFlags = NVGimageFlags.Premultiplied | NVGimageFlags.NoDelete;

    public static int CreateExternalImage(
        NanoVGMetal vg,
        nint textureHandle,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(vg);

        if (textureHandle == 0)
        {
            return 0;
        }

        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);

        Span<byte> placeholderPixel = stackalloc byte[4];
        placeholderPixel.Clear();
        int imageId = vg.CreateImageRGBA(1, 1, s_placeholderFlags, placeholderPixel);
        if (imageId == 0)
        {
            return 0;
        }

        try
        {
            BindExternalTexture(vg.Context, imageId, textureHandle, pixelWidth, pixelHeight);
            return imageId;
        }
        catch
        {
            vg.DeleteImage(imageId);
            throw;
        }
    }

    private static void BindExternalTexture(
        MNVGcontext context,
        int imageId,
        nint textureHandle,
        int pixelWidth,
        int pixelHeight)
    {
        ref MNVGtexture texture = ref FindTexture(context, imageId);
        if (texture.id == 0)
        {
            throw new InvalidOperationException("MewVG could not resolve the Metal image slot for the external texture.");
        }

        nint previousTexture = texture.tex;
        texture.tex = textureHandle;
        texture.width = pixelWidth;
        texture.height = pixelHeight;
        texture.flags = (int)s_externalImageFlags;

        if (previousTexture != 0 && previousTexture != textureHandle)
        {
            ObjCRuntime.Release(previousTexture);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "FindTexture")]
    private static extern ref MNVGtexture FindTexture(MNVGcontext context, int id);
}
