namespace Aprillz.MewUI.Resources;

/// <summary>
/// Tells consumers how a call to <see cref="IPixelBufferSource.Lock"/> reaches the pixel buffer.
/// </summary>
/// <remarks>
/// Consumers that have a deferred path can avoid invoking <see cref="IPixelBufferSource.Lock"/> when it
/// would trigger a GPU sync barrier.
/// </remarks>
public enum LockMode
{
    /// <summary>
    /// The buffer is already in CPU memory and is exposed without an intermediate copy or conversion.
    /// </summary>
    /// <remarks>
    /// Sources backed by a managed byte[] (<see cref="WriteableBitmap"/>, decoded image data, CPU render
    /// targets) report this.
    /// </remarks>
    Direct,

    /// <summary>
    /// The buffer lives on the GPU, so <see cref="IPixelBufferSource.Lock"/> copies it back after waiting for
    /// the GPU, stalling the CPU on a sync barrier.
    /// </summary>
    /// <remarks>
    /// Avoid in hot paths if the consumer has a GPU-side option.
    /// </remarks>
    Readback,
}
