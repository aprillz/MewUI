using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.MacOS.Automation;

/// <summary>
/// The accessibility element of one automation peer: a native element object the node owns, whose methods read and
/// operate the peer. AppKit calls them on the main thread, which is the UI thread.
/// </summary>
internal sealed class AxNode
{
    internal AxNode(AutomationPeer peer, nint handle)
    {
        Peer = peer;
        Handle = handle;
    }

    ~AxNode() => AxProvider.ReleaseElement(Handle);

    internal AutomationPeer Peer { get; }

    /// <summary>The native element object, retained by this node.</summary>
    internal nint Handle { get; }

    /// <summary>The window whose native view shows the peer, while that view exists.</summary>
    internal Window? Surface => AutomationSurface.Of(Peer) is Window window && window.Handle != 0 ? window : null;

    internal nint Role() => AxObjects.String(AxMapping.Role(Peer));

    internal nint Subrole() => AxMapping.Subrole(Peer) is string subrole ? AxObjects.String(subrole) : 0;

    internal nint Label() => AxObjects.String(Peer.GetName());

    internal nint Identifier() => AxObjects.String(Peer.GetAutomationId());

    internal nint Help() => AxObjects.String(Peer.GetHelpText());

    internal nint Value()
    {
        switch (Peer)
        {
            case IValueProvider value:
                return AxObjects.String(value.Value);
            case IRangeValueProvider range:
                return AxObjects.Number(range.Value);
            case IToggleProvider toggle:
                return AxObjects.Integer(AxMapping.ToggleValue(toggle.ToggleState));
            case ISelectionItemProvider item when AxMapping.SelectionIsValue(Peer):
                return AxObjects.Integer(item.IsSelected ? 1 : 0);
            default:
                return 0;
        }
    }

    internal nint MinValue() => Peer is IRangeValueProvider range ? AxObjects.Number(range.Minimum) : 0;

    internal nint MaxValue() => Peer is IRangeValueProvider range ? AxObjects.Number(range.Maximum) : 0;

    internal void SetValue(nint value)
    {
        if (!Peer.IsEnabled())
        {
            return;
        }

        if (Peer is IValueProvider text && !text.IsReadOnly && AxObjects.TryReadString(value, out string? newText))
        {
            text.SetValue(newText);
        }
        else if (Peer is IRangeValueProvider range && !range.IsReadOnly && AxObjects.TryReadNumber(value, out double number))
        {
            range.SetValue(Math.Clamp(number, range.Minimum, range.Maximum));
        }
    }

    /// <summary>The parent element: the peer's parent in its window's tree, or the window for a top-level peer.</summary>
    internal nint Parent()
    {
        var parent = AutomationSurface.ParentOf(Peer);
        if (parent == null || parent is WindowAutomationPeer)
        {
            return AxObjects.WindowOfView(Surface?.Handle ?? 0);
        }
        else
        {
            return AxProvider.NodeOf(parent).Handle;
        }
    }

    internal nint Children() => AxProvider.ElementArray(AutomationSurface.ChildrenOf(Peer));

    internal nint TopLevel() => AxObjects.WindowOfView(Surface?.Handle ?? 0);

    /// <summary>The bounds in Cocoa screen coordinates: points, with the origin at the bottom-left of the main screen.</summary>
    internal NSRect Frame()
    {
        var bounds = AutomationSurface.ScreenBoundsOf(Peer);
        if (bounds.IsEmpty)
        {
            return default;
        }

        var topLeft = MacOSInterop.ScreenPixelsToCocoa(bounds.TopLeft);
        var bottomRight = MacOSInterop.ScreenPixelsToCocoa(bounds.BottomRight);
        return new NSRect(topLeft.x, bottomRight.y, bottomRight.x - topLeft.x, topLeft.y - bottomRight.y);
    }

    internal bool IsFocused() => Peer.HasKeyboardFocus();

    internal void SetFocused(bool focused)
    {
        if (focused)
        {
            Peer.SetFocus();
        }
    }

    internal bool IsSelected() => Peer is ISelectionItemProvider item && item.IsSelected;

    internal void SetSelected(bool selected)
    {
        if (Peer is not ISelectionItemProvider item || !Peer.IsEnabled())
        {
            return;
        }

        if (selected)
        {
            item.Select();
        }
        else
        {
            item.RemoveFromSelection();
        }
    }

    internal bool IsExpanded() => Peer is IExpandCollapseProvider expander && expander.ExpandCollapseState == ExpandCollapseState.Expanded;

    internal void SetExpanded(bool expanded)
    {
        if (Peer is not IExpandCollapseProvider expander || !Peer.IsEnabled())
        {
            return;
        }

        if (expanded)
        {
            expander.Expand();
        }
        else
        {
            expander.Collapse();
        }
    }

    internal nint SelectedChildren()
    {
        if (Peer is ISelectionProvider selection)
        {
            return AxProvider.ElementArray(selection.GetSelection());
        }
        else
        {
            return 0;
        }
    }

    /// <summary>Performs the element's primary action as a click would; false when it has none or is disabled.</summary>
    internal bool Press()
    {
        if (!Peer.IsEnabled())
        {
            return false;
        }

        switch (Peer)
        {
            case IInvokeProvider invoke:
                // Pressing must not wait for what the action starts, such as a dialog with its own loop.
                AxProvider.Post(invoke.Invoke);
                return true;
            case IToggleProvider toggle:
                toggle.Toggle();
                return true;
            case ISelectionItemProvider item:
                item.Select();
                return true;
            case IExpandCollapseProvider expander:
                SetExpanded(expander.ExpandCollapseState != ExpandCollapseState.Expanded);
                return true;
            default:
                return false;
        }
    }

    internal bool Step(bool increment)
    {
        if (Peer is not IRangeValueProvider range || range.IsReadOnly || !Peer.IsEnabled())
        {
            return false;
        }

        double step = range.SmallChange > 0 ? range.SmallChange : (range.Maximum - range.Minimum) / 10;
        double target = increment ? range.Value + step : range.Value - step;
        range.SetValue(Math.Clamp(target, range.Minimum, range.Maximum));
        return true;
    }

    /// <summary>Whether the element answers <paramref name="selector"/>: actions and settable state only where the peer supports them.</summary>
    internal bool AllowsSelector(string selector) => selector switch
    {
        "accessibilityPerformPress" => Peer is IInvokeProvider or IToggleProvider or ISelectionItemProvider or IExpandCollapseProvider,
        "accessibilityPerformIncrement" or "accessibilityPerformDecrement" or "accessibilityMinValue" or "accessibilityMaxValue"
            => Peer is IRangeValueProvider,
        "setAccessibilityValue:" => (Peer is IValueProvider text && !text.IsReadOnly) || (Peer is IRangeValueProvider range && !range.IsReadOnly),
        "accessibilityValue" => Peer is IValueProvider or IRangeValueProvider or IToggleProvider || AxMapping.SelectionIsValue(Peer),
        "isAccessibilitySelected" or "setAccessibilitySelected:" => Peer is ISelectionItemProvider,
        "isAccessibilityExpanded" or "setAccessibilityExpanded:" => Peer is IExpandCollapseProvider,
        "accessibilitySelectedChildren" => Peer is ISelectionProvider,
        "accessibilityNumberOfCharacters" or "accessibilitySelectedText" or "accessibilitySelectedTextRange"
            or "accessibilityVisibleCharacterRange" or "accessibilityInsertionPointLineNumber" or "accessibilityStringForRange:"
            or "accessibilityFrameForRange:" or "accessibilityRangeForPosition:" or "accessibilityLineForIndex:"
            or "accessibilityRangeForLine:" or "accessibilityRangeForIndex:" => Peer is ITextProvider,
        "setAccessibilitySelectedTextRange:" => Peer is ITextProvider text && text.CaretOffset >= 0,
        "setAccessibilityFocused:" => Peer.IsKeyboardFocusable(),
        _ => true,
    };

    private ITextProvider TextPattern => (ITextProvider)Peer;

    internal long NumberOfCharacters() => TextPattern.Text.Length;

    internal NSRange SelectedTextRange()
    {
        var text = TextPattern;
        var selection = text.GetSelection();
        if (selection.Count > 0)
        {
            return new NSRange((ulong)selection[0].Start, (ulong)selection[0].Length);
        }
        else
        {
            return new NSRange((ulong)Math.Max(text.CaretOffset, 0), 0);
        }
    }

    internal nint SelectedText()
    {
        var range = SelectedTextRange();
        return AxObjects.String(Substring(range));
    }

    internal void SetSelectedTextRange(NSRange range)
    {
        if (Peer.IsEnabled())
        {
            TextPattern.Select((int)range.location, (int)range.length);
        }
    }

    internal NSRange VisibleCharacterRange() => new(0, (ulong)TextPattern.Text.Length);

    internal long InsertionPointLineNumber() => LineForIndex(Math.Max(TextPattern.CaretOffset, 0));

    internal nint StringForRange(NSRange range) => AxObjects.String(Substring(range));

    internal long LineForIndex(long index)
    {
        string text = TextPattern.Text;
        int end = (int)Math.Clamp(index, 0, text.Length);
        int line = 0;
        for (int offset = 0; offset < end; offset++)
        {
            if (text[offset] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    internal NSRange RangeForLine(long line)
    {
        string text = TextPattern.Text;
        int start = 0;
        for (long current = 0; current < line; current++)
        {
            int next = text.IndexOf('\n', start);
            if (next < 0)
            {
                return new NSRange((ulong)text.Length, 0);
            }

            start = next + 1;
        }

        var unit = TextPattern.GetUnitAt(start, TextUnit.Line);
        return new NSRange((ulong)unit.Start, (ulong)unit.Length);
    }

    internal NSRange RangeForIndex(long index)
    {
        var unit = TextPattern.GetUnitAt((int)index, TextUnit.Character);
        return new NSRange((ulong)unit.Start, (ulong)unit.Length);
    }

    internal NSRange RangeForPosition(NSPoint point)
    {
        var client = AutomationSurface.ScreenToClient(Peer, MacOSInterop.CocoaToScreenPixels(point));
        int offset = TextPattern.GetOffsetAt(client);
        return new NSRange((ulong)offset, offset < TextPattern.Text.Length ? 1UL : 0UL);
    }

    /// <summary>The bounds of the characters in Cocoa screen coordinates.</summary>
    internal NSRect FrameForRange(NSRange range)
    {
        var union = Rect.Empty;
        foreach (var line in TextPattern.GetBounds((int)range.location, (int)range.length))
        {
            union = union.IsEmpty ? line : union.Union(line);
        }

        var screen = AutomationSurface.ClientToScreen(Peer, union);
        if (screen.IsEmpty)
        {
            return default;
        }

        var topLeft = MacOSInterop.ScreenPixelsToCocoa(screen.TopLeft);
        var bottomRight = MacOSInterop.ScreenPixelsToCocoa(screen.BottomRight);
        return new NSRect(topLeft.x, bottomRight.y, bottomRight.x - topLeft.x, topLeft.y - bottomRight.y);
    }

    private string Substring(NSRange range)
    {
        string text = TextPattern.Text;
        int start = (int)Math.Min(range.location, (ulong)text.Length);
        int length = (int)Math.Min(range.length, (ulong)(text.Length - start));
        return text.Substring(start, length);
    }
}
