using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Markdown;

/// <summary>Tab traversal between links across blocks, including blocks the viewer has not realized yet.</summary>
public partial class MarkdownPresenter
{
    private (int Unit, bool Forward)? _pendingLinkFocus;

    // Tab at the edge of a paragraph's links: the paragraph leaves it unhandled and it bubbles here.
    private bool TryMoveLinkFocus(bool forward)
    {
        if (_document == null || FindVisualRoot() is not Window window ||
            window.FocusManager.FocusedElement is not MarkdownParagraph current || current.TextUnit < 0 || !IsAttached(current))
        {
            return false;
        }
        int target = NextLinkUnit(current.TextUnit, forward);
        if (target < 0)
        {
            return false;
        }
        var realized = RealizedSelectableElements().OfType<MarkdownParagraph>().FirstOrDefault(paragraph => paragraph.TextUnit == target);
        if (realized != null)
        {
            realized.FocusLink(forward ? 0 : realized.LinkCount - 1);
            return true;
        }
        if (_host == null)
        {
            return false;
        }
        // The block is not realized: bring it in and focus its link once the element exists.
        _pendingLinkFocus = (target, forward);
        _host.RequestScrollToBlock(_document.TextUnits[target].TopIndex);
        return true;
    }

    private int NextLinkUnit(int fromUnit, bool forward)
    {
        var units = _document!.TextUnits;
        for (int unit = forward ? fromUnit + 1 : fromUnit - 1; unit >= 0 && unit < units.Count; unit += forward ? 1 : -1)
        {
            if (units[unit].HasLinks)
            {
                return unit;
            }
        }
        return -1;
    }

    // Runs after arrange: once the Tab target is realized and placed, its link takes focus. Focus
    // changes can scroll, so in a running app they wait for the layout pass to finish.
    private void FlushPendingLinkFocus()
    {
        if (_pendingLinkFocus is not (int unit, bool forward))
        {
            return;
        }
        var paragraph = RealizedSelectableElements().OfType<MarkdownParagraph>().FirstOrDefault(candidate => candidate.TextUnit == unit);
        if (paragraph == null || paragraph.LinkCount == 0)
        {
            return;
        }
        _pendingLinkFocus = null;
        int link = forward ? 0 : paragraph.LinkCount - 1;
        IDispatcher? dispatcher = Application.IsRunning ? Application.Current?.Dispatcher : null;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (!_disposed && IsAttached(paragraph))
                {
                    paragraph.FocusLink(link);
                }
            });
        }
        else
        {
            paragraph.FocusLink(link);
        }
    }
}
