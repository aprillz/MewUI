namespace Aprillz.MewUI.Resources;

/// <summary>
/// Tells consumers how a call to <see cref="IPixelBufferSource.Lock"/> reaches the pixel
/// buffer. Consumers that have a deferred path can avoid invoking <c>Lock</c> when it
/// would trigger a GPU sync barrier.
/// </summary>
public enum LockMode
{
    /// <summary>
    /// Buffer is already in CPU memory and is exposed without intermediate copy or
    /// conversion. Sources backed by a managed byte[] (<c>WriteableBitmap</c>, decoded
    /// image data, DIB-backed render targets) report this.
    /// </summary>
    Direct,

    /// <summary>
    /// Buffer lives on the GPU; <c>Lock</c> performs a staging copy / <c>glReadPixels</c>
    /// / <c>[texture getBytes:]</c> preceded by a fence wait. CPU stalls on a GPU sync
    /// barrier. Avoid in hot paths if the consumer has a GPU-side option.
    /// </summary>
    Readback,
}
