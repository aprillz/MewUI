using System.Numerics;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Resolves live window resource objects owned by a MewVG graphics factory.
/// </summary>
public interface IMewVGWindowResourceResolver
{
    bool TryGetWindowResources(nint windowHandle, out IDisposable? resources);
}

/// <summary>
/// Window-resource interop required to create external OpenGL images and bind
/// the current GL context for interop rendering.
/// </summary>
public interface IMewVGGlWindowInterop
{
    int CreateExternalImage(int textureId, int pixelWidth, int pixelHeight);

    void DeleteExternalImage(int imageId);

    void RunWithCurrentContext(Action action);
}

/// <summary>
/// Window-resource interop required to create Metal textures compatible with the
/// active MewVG window device and command queue.
/// </summary>
public interface IMewVGMetalWindowInterop
{
    nint DeviceHandle { get; }

    nint CommandQueueHandle { get; }

    nint CreateSharedTexture(int widthPx, int heightPx);

    void ReleaseSharedTexture(ref nint texture);
}

/// <summary>
/// Active OpenGL graphics context capable of compositing an external image handle
/// into the current MewVG frame.
/// </summary>
public interface IMewVGGlExternalImageContext
{
    void DrawExternalImage(int imageId, Rect destRect, int imageWidthPx, int imageHeightPx);
}

/// <summary>
/// Captures the drawable and state required to composite into the current Metal
/// frame from an external renderer.
/// </summary>
public readonly record struct MewVGMetalExternalCompositeState(
    nint DrawableTexture,
    int ViewportWidthPx,
    int ViewportHeightPx,
    double DpiScale,
    Rect? ClipBoundsWorld,
    float GlobalAlpha,
    Matrix3x2 Transform);

/// <summary>
/// Active Metal graphics context capable of suspending the current frame,
/// exposing the drawable for external composition, and restoring the frame.
/// </summary>
public interface IMewVGMetalExternalCompositeContext
{
    bool TryBeginExternalComposite(out MewVGMetalExternalCompositeState state);

    void EndExternalComposite();
}
