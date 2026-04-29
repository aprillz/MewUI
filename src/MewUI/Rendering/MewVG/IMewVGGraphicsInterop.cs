namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Resolves live window resource objects owned by a MewVG graphics factory.
/// </summary>
public interface IMewVGWindowResourceResolver
{
    bool TryGetWindowResources(nint windowHandle, out IDisposable? resources);
}

/// <summary>
/// Window-resource interop required to create backend-native images from
/// externally managed GPU resources.
/// </summary>
public interface IMewVGExternalImageInterop
{
    int CreateExternalImage(nint handle, int pixelWidth, int pixelHeight);

    void DeleteExternalImage(int imageId);
}

/// <summary>
/// Window-resource interop required to create external OpenGL images and bind
/// the current GL context for interop rendering.
/// </summary>
public interface IMewVGGlWindowInterop : IMewVGExternalImageInterop
{
    void RunWithCurrentContext(Action action);
}

/// <summary>
/// Window-resource interop required to create Metal textures compatible with the
/// active MewVG window device and command queue.
/// </summary>
public interface IMewVGMetalWindowInterop : IMewVGExternalImageInterop
{
    nint DeviceHandle { get; }

    nint CommandQueueHandle { get; }

    nint CreateSharedTexture(int widthPx, int heightPx);

    void ReleaseSharedTexture(ref nint texture);
}

/// <summary>
/// Active MewVG graphics context capable of compositing an external image handle
/// into the current frame.
/// </summary>
public interface IMewVGExternalImageContext
{
    void DrawExternalImage(int imageId, Rect destRect, int imageWidthPx, int imageHeightPx);
}
