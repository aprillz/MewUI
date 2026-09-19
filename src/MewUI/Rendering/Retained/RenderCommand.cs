namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Preservation requirement of the resource a <see cref="RenderCommand"/> references.
/// </summary>
[Flags]
internal enum RenderResourcePolicy : byte
{
    Value = 0,
    ImmutableDescriptor = 1,
    FrozenGeometry = 2,
    ImageLeaseRequired = 4,
    TextLayoutLeaseRequired = 8,
}

internal enum RenderCommandKind : byte
{
    Save,
    Restore,
    SetClip,
    SetClipRoundedRect,
    SetClipPath,
    ResetClip,
    IntersectClip,
    Translate,
    Rotate,
    Scale,
    SetTransform,
    ResetTransform,
    BeginOpaqueBackdrop,
    EndOpaqueBackdrop,
    BeginOpacity,
    EndOpacity,
    SetGlobalAlpha,
    SetTextPixelSnap,
    SetImageScaleQuality,
    SetAlphaTextHint,
    DrawLine,
    DrawRectangle,
    FillRectangle,
    DrawRoundedRectangle,
    FillRoundedRectangle,
    DrawEllipse,
    FillEllipse,
    DrawPath,
    FillPath,
    DrawBoxShadow,
    DrawImage,
    DrawText,
    DrawTextBackground,
    DrawTextForeground,
}

internal enum RenderImageVariant : byte
{
    Location,
    Destination,
    DestinationAndSource,
}

/// <summary>
/// Which overload a recorded call used, folded into one byte alongside its small arguments.
/// </summary>
[Flags]
internal enum RenderCommandFlags : byte
{
    None = 0,
    BooleanValue = 1,
    UsesBooleanOverload = 2,
    UsesFillRule = 4,
    EvenOddFillRule = 8,
    ImageVariantLow = 16,
    ImageVariantHigh = 32,
}

/// <summary>
/// One recorded drawing call of a single visual, with its bounds in that visual's local space. The
/// numeric arguments live in the owning <see cref="RenderData"/>'s value buffer and the objects it
/// draws with live in that data's resource table, so this record stays small enough to keep dense.
/// </summary>
internal readonly struct RenderCommand
{
    private const int IMAGE_VARIANT_SHIFT = 4;
    private const int IMAGE_VARIANT_MASK = 0b0011_0000;

    internal RenderCommand(
        RenderCommandKind kind,
        Rect bounds,
        RenderResourcePolicy resourcePolicy,
        RenderCommandFlags flags,
        Color color,
        int valueOffset,
        byte valueCount,
        int resourceIndex,
        int paintIndex)
    {
        Bounds = bounds;
        Color = color;
        ValueOffset = valueOffset;
        ResourceIndex = resourceIndex;
        PaintIndex = paintIndex;
        Kind = kind;
        Flags = flags;
        ResourcePolicy = resourcePolicy;
        ValueCount = valueCount;
    }

    internal Rect Bounds { get; }

    internal Color Color { get; }

    /// <summary>Index of this command's first argument in the owning data's value buffer.</summary>
    internal int ValueOffset { get; }

    /// <summary>Index into the owning data's resource table, or -1 when the command draws no object.</summary>
    internal int ResourceIndex { get; }

    /// <summary>Index of the pen, brush or text options in the owning data's resource table, or -1.</summary>
    internal int PaintIndex { get; }

    internal RenderCommandKind Kind { get; }

    internal RenderCommandFlags Flags { get; }

    internal RenderResourcePolicy ResourcePolicy { get; }

    internal byte ValueCount { get; }

    internal bool BooleanValue => (Flags & RenderCommandFlags.BooleanValue) != 0;

    internal bool UsesBooleanOverload => (Flags & RenderCommandFlags.UsesBooleanOverload) != 0;

    internal bool UsesFillRule => (Flags & RenderCommandFlags.UsesFillRule) != 0;

    internal FillRule FillRule
        => (Flags & RenderCommandFlags.EvenOddFillRule) != 0 ? FillRule.EvenOdd : FillRule.NonZero;

    internal RenderImageVariant ImageVariant
        => (RenderImageVariant)(((int)Flags & IMAGE_VARIANT_MASK) >> IMAGE_VARIANT_SHIFT);

    internal static RenderCommandFlags Encode(RenderImageVariant variant)
        => (RenderCommandFlags)((int)variant << IMAGE_VARIANT_SHIFT);

    internal static RenderCommandFlags Encode(FillRule fillRule)
        => RenderCommandFlags.UsesFillRule |
            (fillRule == FillRule.EvenOdd ? RenderCommandFlags.EvenOddFillRule : RenderCommandFlags.None);
}
