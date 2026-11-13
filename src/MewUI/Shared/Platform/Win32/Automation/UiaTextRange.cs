using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>
/// A UI Automation text range over a peer's text: two offsets a reader moves by unit. Like the node it belongs to,
/// it does its work on the UI thread.
/// </summary>
[GeneratedComClass]
internal sealed partial class UiaTextRange : IUiaTextRangeProvider
{
    // TextPatternRangeEndpoint and the TextUnit values UI Automation passes.
    private const int ENDPOINT_START = 0;
    private const int UNIT_CHARACTER = 0;
    private const int UNIT_WORD = 2;
    private const int UNIT_LINE = 3;
    private const int UNIT_PARAGRAPH = 4;

    private readonly UiaNode _node;
    private int _start;
    private int _end;

    internal UiaTextRange(UiaNode node, int start, int end)
    {
        _node = node;
        _start = start;
        _end = Math.Max(start, end);
    }

    private string Text => _node.Pattern<ITextProvider>().Text;

    private static TextUnit UnitOf(int unit) => unit switch
    {
        UNIT_CHARACTER => TextUnit.Character,
        UNIT_WORD => TextUnit.Word,
        UNIT_LINE => TextUnit.Line,
        UNIT_PARAGRAPH => TextUnit.Paragraph,
        // Format runs and pages: the text has one format and one page.
        _ => TextUnit.Document,
    };

    private int Act(Action action) => _node.Call(() =>
    {
        action();
        return UiaNative.S_OK;
    });

    /// <summary>Keeps both ends inside the text, which may have shrunk since the range was made.</summary>
    private void Clamp(int length)
    {
        _start = Math.Clamp(_start, 0, length);
        _end = Math.Clamp(_end, _start, length);
    }

    private static UiaTextRange? Other(IUiaTextRangeProvider? range) => range as UiaTextRange;

    int IUiaTextRangeProvider.Clone(out IUiaTextRangeProvider? clone)
    {
        clone = new UiaTextRange(_node, _start, _end);
        return UiaNative.S_OK;
    }

    int IUiaTextRangeProvider.Compare(IUiaTextRangeProvider? range, out int same)
    {
        var other = Other(range);
        same = other != null && ReferenceEquals(other._node, _node) && other._start == _start && other._end == _end ? 1 : 0;
        return UiaNative.S_OK;
    }

    int IUiaTextRangeProvider.CompareEndpoints(int endpoint, IUiaTextRangeProvider? targetRange, int targetEndpoint, out int comparison)
    {
        var other = Other(targetRange);
        if (other == null || !ReferenceEquals(other._node, _node))
        {
            comparison = 0;
            return UiaNative.E_INVALIDARG;
        }

        int mine = endpoint == ENDPOINT_START ? _start : _end;
        int theirs = targetEndpoint == ENDPOINT_START ? other._start : other._end;
        comparison = Math.Sign(mine - theirs);
        return UiaNative.S_OK;
    }

    int IUiaTextRangeProvider.ExpandToEnclosingUnit(int unit) => Act(() =>
    {
        var text = _node.Pattern<ITextProvider>();
        Clamp(text.Text.Length);
        var enclosing = text.GetUnitAt(_start, UnitOf(unit));
        _start = enclosing.Start;
        _end = enclosing.Start + enclosing.Length;
    });

    int IUiaTextRangeProvider.FindAttribute(int attributeId, UiaVariant value, int backward, out IUiaTextRangeProvider? found)
    {
        // The text carries no format attributes to find.
        found = null;
        return UiaNative.S_OK;
    }

    int IUiaTextRangeProvider.FindText(nint text, int backward, int ignoreCase, out IUiaTextRangeProvider? found)
    {
        string wanted = Marshal.PtrToStringBSTR(text);
        int hresult = _node.Read(() =>
        {
            string all = Text;
            Clamp(all.Length);
            var comparison = ignoreCase != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string scope = all[_start.._end];
            int index = backward != 0 ? scope.LastIndexOf(wanted, comparison) : scope.IndexOf(wanted, comparison);
            return index < 0 || wanted.Length == 0 ? null : new UiaTextRange(_node, _start + index, _start + index + wanted.Length);
        }, out var match);
        found = match;
        return hresult;
    }

    int IUiaTextRangeProvider.GetAttributeValue(int attributeId, out UiaVariant value)
    {
        value = UiaVariant.NotSupported();
        return UiaNative.S_OK;
    }

    int IUiaTextRangeProvider.GetBoundingRectangles(out nint rectangles)
    {
        int hresult = _node.Read(() =>
        {
            var text = _node.Pattern<ITextProvider>();
            Clamp(text.Text.Length);
            var values = new List<double>();
            foreach (var line in text.GetBounds(_start, _end - _start))
            {
                var screen = AutomationSurface.ClientToScreen(_node.Peer, line);
                values.Add(screen.X);
                values.Add(screen.Y);
                values.Add(screen.Width);
                values.Add(screen.Height);
            }

            return values.ToArray();
        }, out var bounds);
        rectangles = hresult == UiaNative.S_OK ? UiaNative.CreateDoubleArray(bounds) : 0;
        return hresult;
    }

    int IUiaTextRangeProvider.GetEnclosingElement(out IRawElementProviderSimple? element)
    {
        element = _node;
        return UiaNative.S_OK;
    }

    int IUiaTextRangeProvider.GetText(int maxLength, out nint text)
    {
        int hresult = _node.Read(() =>
        {
            string all = Text;
            Clamp(all.Length);
            int length = maxLength < 0 ? _end - _start : Math.Min(maxLength, _end - _start);
            return all.Substring(_start, length);
        }, out string? found);
        text = hresult == UiaNative.S_OK ? Marshal.StringToBSTR(found ?? string.Empty) : 0;
        return hresult;
    }

    int IUiaTextRangeProvider.Move(int unit, int count, out int moved)
    {
        int hresult = _node.Read(() =>
        {
            var text = _node.Pattern<ITextProvider>();
            int length = text.Text.Length;
            Clamp(length);
            bool degenerate = _start == _end;
            var textUnit = UnitOf(unit);
            int position = degenerate ? _start : text.GetUnitAt(_start, textUnit).Start;
            int steps = Step(text, textUnit, ref position, count, length);
            if (degenerate)
            {
                _start = position;
                _end = position;
            }
            else
            {
                var target = text.GetUnitAt(position, textUnit);
                _start = target.Start;
                _end = target.Start + target.Length;
            }

            return steps;
        }, out moved);
        return hresult;
    }

    int IUiaTextRangeProvider.MoveEndpointByUnit(int endpoint, int unit, int count, out int moved)
    {
        int hresult = _node.Read(() =>
        {
            var text = _node.Pattern<ITextProvider>();
            int length = text.Text.Length;
            Clamp(length);
            int position = endpoint == ENDPOINT_START ? _start : _end;
            int steps = Step(text, UnitOf(unit), ref position, count, length);
            SetEndpoint(endpoint, position);
            return steps;
        }, out moved);
        return hresult;
    }

    int IUiaTextRangeProvider.MoveEndpointByRange(int endpoint, IUiaTextRangeProvider? targetRange, int targetEndpoint)
    {
        var other = Other(targetRange);
        if (other == null || !ReferenceEquals(other._node, _node))
        {
            return UiaNative.E_INVALIDARG;
        }

        SetEndpoint(endpoint, targetEndpoint == ENDPOINT_START ? other._start : other._end);
        return UiaNative.S_OK;
    }

    /// <summary>Moves one end; an end that passes the other drags it along, so the range never inverts.</summary>
    private void SetEndpoint(int endpoint, int position)
    {
        if (endpoint == ENDPOINT_START)
        {
            _start = position;
            _end = Math.Max(_end, position);
        }
        else
        {
            _end = position;
            _start = Math.Min(_start, position);
        }
    }

    /// <summary>Moves <paramref name="position"/> by up to <paramref name="count"/> unit boundaries; returns how many it moved.</summary>
    private static int Step(ITextProvider text, TextUnit unit, ref int position, int count, int length)
    {
        int moved = 0;
        while (moved < Math.Abs(count))
        {
            int next;
            if (count > 0)
            {
                if (position >= length)
                {
                    break;
                }

                var current = text.GetUnitAt(position, unit);
                next = Math.Max(current.Start + current.Length, position + 1);
            }
            else
            {
                if (position <= 0)
                {
                    break;
                }

                next = text.GetUnitAt(position - 1, unit).Start;
            }

            position = Math.Clamp(next, 0, length);
            moved++;
        }

        return count > 0 ? moved : -moved;
    }

    int IUiaTextRangeProvider.Select() => Act(() =>
    {
        Clamp(Text.Length);
        _node.Pattern<ITextProvider>().Select(_start, _end - _start);
    });

    int IUiaTextRangeProvider.AddToSelection() => ((IUiaTextRangeProvider)this).Select();

    int IUiaTextRangeProvider.RemoveFromSelection() => Act(() =>
    {
        var text = _node.Pattern<ITextProvider>();
        text.Select(Math.Max(text.CaretOffset, 0), 0);
    });

    int IUiaTextRangeProvider.ScrollIntoView(int alignToTop) => Act(() =>
    {
        if (_node.Peer is IScrollItemProvider scrollItem)
        {
            scrollItem.ScrollIntoView();
        }
    });

    int IUiaTextRangeProvider.GetChildren(out nint children)
    {
        children = UiaNative.CreateInterfaceArray(Array.Empty<IRawElementProviderSimple>());
        return UiaNative.S_OK;
    }
}
