namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Optional backend capability for consumers that must produce D3D11 resources compatible
/// with a specific render target.
/// </summary>
public interface ID3D11RenderTargetDeviceProvider
{
    /// <summary>
    /// Returns an AddRef'ed <c>ID3D11Device*</c> compatible with the render target identified
    /// by <paramref name="renderTargetHandle"/>. The caller must release the returned pointer.
    /// </summary>
    nint RetainD3D11DeviceForRenderTarget(nint renderTargetHandle);
}
