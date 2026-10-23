using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>Reads the text a reader would announce for an element's content.</summary>
internal static class AutomationText
{
    /// <summary>
    /// The text of <paramref name="element"/>: its shown text when it is a text element, its peer's name when it
    /// has one, otherwise the text of the first text element below it.
    /// </summary>
    internal static string Of(Element? element)
    {
        if (element == null)
        {
            return string.Empty;
        }

        if (element is TextBlockBase textBlock)
        {
            return textBlock.DisplayTextInternal;
        }

        if (element is UIElement uiElement && uiElement.GetAutomationPeer() is AutomationPeer peer)
        {
            string name = peer.GetName();
            if (name.Length > 0)
            {
                return name;
            }
        }

        return TextBelow(element);
    }

    /// <summary>The shown text of the first text element at or below <paramref name="element"/>; peers are not consulted.</summary>
    internal static string TextBelow(Element element)
    {
        var first = VisualTree.Find(element, static candidate => candidate is TextBlockBase);
        return first is TextBlockBase textBlock ? textBlock.DisplayTextInternal : string.Empty;
    }
}
