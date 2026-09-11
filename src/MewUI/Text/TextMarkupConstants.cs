namespace Aprillz.MewUI.Text;

/// <summary>Tag limits and size ratios the inline markup parsers apply.</summary>
internal static class TextMarkupConstants
{
    public const int MAX_NESTING = 128;
    public const int MAX_TAG_LENGTH = 4096;

    // Size factor of one big or small step.
    public const double RELATIVE_SIZE_STEP = 1.2;

    // Synthetic scripts: size relative to the parent, and baseline shift as a fraction of the parent's size.
    public const double SCRIPT_SIZE_SCALE = 0.75;
    public const double SUPERSCRIPT_SHIFT = 0.35;
    public const double SUBSCRIPT_SHIFT = -0.20;
}
