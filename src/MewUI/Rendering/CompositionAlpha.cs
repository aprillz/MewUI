namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Applies the alpha the framework draws a visual under, kept apart from the visual's own
/// <see cref="IGraphicsContext.GlobalAlpha"/> so the visual can neither read it nor set it away.
/// </summary>
internal static class CompositionAlpha
{
    /// <summary>Multiplies <paramref name="alpha"/> into what is drawn next, until the enclosing Restore.</summary>
    internal static void Multiply(IGraphicsContext context, double alpha)
    {
        float value = (float)alpha;
        if (context is GraphicsContextBase backend)
        {
            backend.MultiplyCompositionAlpha(value);
        }
        else if (context is Retained.RenderDataRecorder recorder)
        {
            recorder.MultiplyCompositionAlpha(value);
        }
        else
        {
            // A context outside the framework has no composition alpha of its own.
            context.GlobalAlpha *= value;
        }
    }

    /// <summary>Opens a state scope whose drawing alpha starts over, as an opacity scope without a layer does.</summary>
    internal static void BeginScope(IGraphicsContext context, double opacity)
    {
        if (context is GraphicsContextBase backend)
        {
            backend.BeginAlphaScope(opacity);
        }
        else
        {
            context.Save();
            context.GlobalAlpha *= (float)Math.Clamp(opacity, 0.0, 1.0);
        }
    }

    internal static void EndScope(IGraphicsContext context) => context.Restore();
}
