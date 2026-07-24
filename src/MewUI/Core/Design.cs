using System.Runtime.CompilerServices;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Design-time services for editor previews.
/// </summary>
public static class Design
{
    /// <summary>
    /// Whether this process is running as an editor preview session. Set by the preview session
    /// assembly before <c>Main</c> (module initializer), so entry-point code can guard side
    /// effects (network, database) before the UI runtime exists; false everywhere else.
    /// </summary>
    public static bool IsPreviewMode { get; internal set; }

    // Preview-only sizing hints keyed per element; unused (and never allocated) outside preview
    // sessions so production cost stays at one flag check in the fluent setters.
    private static ConditionalWeakTable<FrameworkElement, DesignHintEntry>? _hints;

    private sealed class DesignHintEntry
    {
        public double Width = double.NaN;
        public double Height = double.NaN;
    }

    internal static void SetDesignSize(FrameworkElement element, double? width, double? height)
    {
        if (!IsPreviewMode)
        {
            return;
        }

        var hints = _hints ??= new ConditionalWeakTable<FrameworkElement, DesignHintEntry>();
        var entry = hints.GetOrCreateValue(element);
        if (width is double widthValue)
        {
            entry.Width = widthValue;
        }
        if (height is double heightValue)
        {
            entry.Height = heightValue;
        }
    }

    /// <summary>
    /// Reads the element's design size hints; unset dimensions come back as NaN. Returns false
    /// when the element has no hints (or the process is not a preview session).
    /// </summary>
    internal static bool TryGetDesignSize(FrameworkElement element, out double width, out double height)
    {
        if (_hints != null && _hints.TryGetValue(element, out var entry))
        {
            width = entry.Width;
            height = entry.Height;
            return true;
        }

        width = double.NaN;
        height = double.NaN;
        return false;
    }
}
