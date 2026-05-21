namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Per-evaluation environment passed to <see cref="IImageFilterExecutor.Execute"/>. Provides
/// access to the source layer (resolved by <see cref="SourceFilter"/> / null inputs), a
/// scratch render-target pool, and graph factory for backend resource creation.
/// </summary>
/// <remarks>
/// <see cref="WithSource"/> creates a sub-context whose <see cref="Source"/> is replaced —
/// used by <see cref="ComposeFilter"/> evaluation: the inner filter runs against the original
/// source, and the outer filter runs against the inner's result. The sub-context shares the
/// same scratch pool / factory.
/// </remarks>
public interface IImageFilterContext
{
    /// <summary>
    /// Image the executor treats as the layer being filtered. Returned for
    /// <see cref="SourceFilter"/> nodes and any <see langword="null"/> input slot.
    /// Wrapped as a <see cref="BorrowedFilterResult"/> so callers can <c>using</c>-dispose
    /// uniformly without releasing the underlying source.
    /// </summary>
    FilterResult Source { get; }

    /// <summary>
    /// Bounds of the source image in user coordinates. Used by nodes that compute output
    /// extent from input bounds (Blur halo inflation, Offset translation).
    /// </summary>
    Rect SourceBounds { get; }

    IGraphicsFactory Factory { get; }

    /// <summary>
    /// Conversion factor from filter input coordinates (logical/DIP) to source-layer
    /// pixel coordinates along X. Matches the effective scale at which the input was
    /// rasterized into <see cref="Source"/> (= user transform x DPI for image filters).
    /// Executors multiply per-axis filter parameters specified in input units (Blur sigma,
    /// Offset dx) by this factor before applying GPU/CPU passes so that filter radii track
    /// the current transform like Skia/WPF effects.
    /// </summary>
    double LogicalToPixelScaleX { get; }

    /// <summary>Same as <see cref="LogicalToPixelScaleX"/> for the Y axis.</summary>
    double LogicalToPixelScaleY { get; }

    /// <summary>
    /// Rents a scratch render surface sized at least (<paramref name="pixelWidth"/>,
    /// <paramref name="pixelHeight"/>) from the pool. The returned
    /// <see cref="ScratchFilterResult"/> automatically releases the surface on
    /// <see cref="FilterResult.Dispose"/>. Callers must render INTO the scratch before
    /// returning it as the node's output.
    /// </summary>
    /// <param name="pixelWidth">Minimum required pixel width.</param>
    /// <param name="pixelHeight">Minimum required pixel height.</param>
    /// <param name="bounds">Bounds in source coordinates this scratch represents.</param>
    ScratchFilterResult AcquireScratch(int pixelWidth, int pixelHeight, Rect bounds);

    /// <summary>
    /// Returns a sub-context whose <see cref="Source"/> is replaced with
    /// <paramref name="newSource"/>. Used by <see cref="ComposeFilter"/> to chain Inner→Outer
    /// without aliasing or recursive bookkeeping.
    /// </summary>
    IImageFilterContext WithSource(FilterResult newSource);
}

/// <summary>
/// Backend-agnostic graph evaluator. Each backend implements its own concrete executor
/// that handles the node types it can render natively. Unsupported nodes delegate to a
/// fallback executor (typically the CPU one) — see plan.md for the chain-of-responsibility
/// model.
/// </summary>
public interface IImageFilterExecutor
{
    /// <summary>
    /// Evaluates <paramref name="filter"/> against <paramref name="context"/> and returns
    /// the result. Caller owns the returned <see cref="FilterResult"/> and must
    /// <c>using</c>-dispose it (no-op for borrowed source results).
    /// </summary>
    FilterResult Execute(ImageFilter filter, IImageFilterContext context);

    /// <summary>
    /// Upper bound on the LogicalToPixel scale at which the executor wants the source layer
    /// rasterized. GPU executors return <see cref="double.PositiveInfinity"/> (render at full
    /// requested scale for crisp source). CPU executors should return <c>1.0</c> — running
    /// Gaussian blur on a 12× upscaled buffer is orders of magnitude slower than rendering
    /// the source at 1× and letting the backend's hardware bilinear stretch the small filter
    /// result up to the final display rect during <c>DrawImage</c>. The visual difference is
    /// minor (blur output is low-frequency) and the perf win is large.
    /// </summary>
    double MaxInputScale => double.PositiveInfinity;
}
