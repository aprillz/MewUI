namespace Aprillz.MewUI.Resources;

/// <summary>
/// Backend-agnostic abstraction for external GPU textures that require Acquire/Release
/// brackets around each use. Used for cross-API interop where the backend's draw pipeline
/// is not the sole owner of the underlying texture (e.g., WGL_NV_DX_interop locking,
/// PBO fence waits, IOSurface borrowing).
/// </summary>
/// <remarks>
/// <para>
/// External code (sample, library) implements this and passes it through an
/// <see cref="Rendering.IExternalSampleSource"/>. The backend invokes
/// <see cref="Acquire"/> before the first draw of each frame that samples the texture
/// and <see cref="Release"/> after the frame's draws have been flushed/committed.
/// </para>
/// <para>
/// <b>Acquire/Release timing</b> (per backend):
/// <list type="bullet">
///   <item>MewVG GL: <c>Acquire</c> at first draw use of the frame; <c>Release</c> after
///         <c>SwapBuffers</c>/<c>glFlush</c> of the frame containing those draws.</item>
///   <item>MewVG Metal: <c>Acquire</c> before encoding; <c>Release</c> from the
///         command buffer's <c>addCompletedHandler</c> (callback runs on a Metal-managed
///         private queue — implementer must handle that).</item>
///   <item>D2D: this contract is NOT used by the D2D backend — D3D11 → D2D interop
///         relies on COM ref-chain lifetime instead (see
///         <c>Direct2DGraphicsFactory.CreateImageFromNativeBitmap</c>).</item>
/// </list>
/// </para>
/// <para>
/// <b>Lifecycle</b>: backend wrapper IImage releases its own bookkeeping on Dispose;
/// the implementer's own <see cref="System.IDisposable.Dispose"/> is invoked separately
/// by the external owner once all consuming IImages are gone. Disposing while
/// <c>Acquire</c>'d is allowed — implementer should release first, then clean up.
/// </para>
/// </remarks>
public interface IExternalLockedTexture : IDisposable
{
    /// <summary>
    /// Native GPU handle. Valid only between <see cref="Acquire"/> and the matching
    /// <see cref="Release"/>. Interpretation depends on the consuming backend the
    /// IImage was created for:
    /// <list type="bullet">
    ///   <item>GL backends → cast to <c>uint</c> texture id</item>
    ///   <item>Metal backend → <c>MTLTexture*</c> pointer</item>
    ///   <item>D2D backend → <c>ID2D1Bitmap*</c> pointer (rare — D2D usually uses
    ///         CreateImageFromNativeBitmap directly)</item>
    /// </list>
    /// </summary>
    nint NativeHandle { get; }

    /// <summary>Texture width in pixels.</summary>
    int PixelWidth { get; }

    /// <summary>Texture height in pixels.</summary>
    int PixelHeight { get; }

    /// <summary>How the texture's alpha channel is interpreted.</summary>
    BitmapAlphaMode AlphaMode { get; }

    /// <summary>
    /// <see langword="true"/> when texel row 0 corresponds to the bottom of the image
    /// (GL FBO convention). <see langword="false"/> for top-down storage (D3D / Metal).
    /// Backends use this to flip the V coordinate at sample time when needed.
    /// </summary>
    bool YFlipped { get; }

    /// <summary>
    /// Called by the backend before the first draw command in a frame that samples this
    /// texture. The implementer performs whatever lock/sync is needed to make the
    /// underlying GPU resource valid for sampling — for example,
    /// <c>wglDXLockObjectsNV</c>, <c>glClientWaitSync</c> on an upload fence, or
    /// <c>CVPixelBufferLockBaseAddress</c>. After this returns, <see cref="NativeHandle"/>
    /// MUST be valid for GPU read.
    /// </summary>
    /// <remarks>
    /// May be called once per frame even when the texture is sampled multiple times in
    /// that frame — the backend deduplicates Acquire calls within a frame.
    /// </remarks>
    void Acquire();

    /// <summary>
    /// Called by the backend after the frame's draw commands referencing this texture
    /// have been flushed (or committed for async backends). Implementer performs the
    /// matching unlock/release.
    /// </summary>
    /// <remarks>
    /// Acquire/Release calls are paired and never nested for the same texture.
    /// </remarks>
    void Release();
}
