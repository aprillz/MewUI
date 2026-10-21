// Ported from Windows Terminal src/buffer/out/TextAttribute.hpp, TextAttribute.cpp and src/inc/conattrs.hpp (MIT).

namespace Aprillz.MewUI.Terminal.Buffer;

[Flags]
internal enum CharacterAttributes : ushort
{
    Normal = 0x00,
    Intense = 0x01,
    Italics = 0x02,
    Blinking = 0x04,
    Invisible = 0x08,
    CrossedOut = 0x10,
    Faint = 0x20,

    // Bits 7 to 9 hold the underline style.
    UnderlineStyle = 0x1C0,

    Unused1 = 0x200,
    TopGridline = 0x400,
    LeftGridline = 0x800,
    RightGridline = 0x1000,
    Protected = 0x2000,
    ReverseVideo = 0x4000,
    BottomGridline = 0x8000,

    All = 0xFFFF,

    // Every attribute that affects how a cell looks, which excludes Protected.
    Rendition = All & ~Protected,
}

internal enum UnderlineStyle
{
    NoUnderline = 0,
    SinglyUnderlined = 1,
    DoublyUnderlined = 2,
    CurlyUnderlined = 3,
    DottedUnderlined = 4,
    DashedUnderlined = 5,
    Max = DashedUnderlined,
}

internal enum MarkKind : ushort
{
    None = 0,
    Prompt = 1,
    Command = 2,
    Output = 3,
}

/// <summary>
/// Everything that styles a cell besides its text: rendition flags, foreground, background and underline colors, the
/// hyperlink it belongs to, and the shell-integration mark it is part of.
/// </summary>
internal struct TextAttribute : IEquatable<TextAttribute>
{
    public const int UNDERLINE_STYLE_SHIFT = 6;

    // Valid colors are 0x00BBGGRR, so a set top byte can never be one.
    public const uint INVALID_COLOR = 0xFFFFFFFF;

    private CharacterAttributes _attrs;
    private ushort _hyperlinkId;
    private TextColor _foreground;
    private TextColor _background;
    private TextColor _underlineColor;
    private MarkKind _markKind;

    public TextAttribute(uint rgbForeground, uint rgbBackground, uint rgbUnderline = INVALID_COLOR)
    {
        _attrs = CharacterAttributes.Normal;
        _foreground = new TextColor(rgbForeground);
        _background = new TextColor(rgbBackground);
        _hyperlinkId = 0;
        _underlineColor = new TextColor(rgbUnderline);
        _markKind = MarkKind.None;
    }

    public TextAttribute(CharacterAttributes attrs, TextColor foreground, TextColor background, ushort hyperlinkId, TextColor underlineColor)
    {
        _attrs = attrs;
        _foreground = foreground;
        _background = background;
        _hyperlinkId = hyperlinkId;
        _underlineColor = underlineColor;
        _markKind = MarkKind.None;
    }

    public CharacterAttributes CharacterAttributes
    {
        readonly get => _attrs;
        set => _attrs = value;
    }

    public MarkKind MarkAttributes
    {
        readonly get => _markKind;
        set => _markKind = value;
    }

    public readonly TextColor Foreground => _foreground;

    public readonly TextColor Background => _background;

    public readonly TextColor UnderlineColor => _underlineColor;

    public readonly ushort HyperlinkId => _hyperlinkId;

    public readonly UnderlineStyle UnderlineStyle
        => (UnderlineStyle)(((int)(_attrs & CharacterAttributes.UnderlineStyle)) >> UNDERLINE_STYLE_SHIFT);

    public readonly bool IsLegacy() => _foreground.IsLegacy() && _background.IsLegacy();

    public readonly bool IsHyperlink() => _hyperlinkId != 0;

    public readonly bool IsTopHorizontalDisplayed() => (_attrs & CharacterAttributes.TopGridline) != 0;

    public readonly bool IsBottomHorizontalDisplayed() => (_attrs & CharacterAttributes.BottomGridline) != 0;

    public readonly bool IsLeftVerticalDisplayed() => (_attrs & CharacterAttributes.LeftGridline) != 0;

    public readonly bool IsRightVerticalDisplayed() => (_attrs & CharacterAttributes.RightGridline) != 0;

    /// <summary>Whether the text is drawn bold: intense text is bold when asked to be, or when its color cannot brighten.</summary>
    public readonly bool IsBold(bool intenseIsBold) => IsIntense() && (intenseIsBold || !_foreground.CanBeBrightened());

    public readonly bool IsIntense() => (_attrs & CharacterAttributes.Intense) != 0;

    public readonly bool IsFaint() => (_attrs & CharacterAttributes.Faint) != 0;

    public readonly bool IsItalic() => (_attrs & CharacterAttributes.Italics) != 0;

    public readonly bool IsBlinking() => (_attrs & CharacterAttributes.Blinking) != 0;

    public readonly bool IsInvisible() => (_attrs & CharacterAttributes.Invisible) != 0;

    public readonly bool IsCrossedOut() => (_attrs & CharacterAttributes.CrossedOut) != 0;

    public readonly bool IsUnderlined() => UnderlineStyle != UnderlineStyle.NoUnderline;

    public readonly bool IsOverlined() => (_attrs & CharacterAttributes.TopGridline) != 0;

    public readonly bool IsReverseVideo() => (_attrs & CharacterAttributes.ReverseVideo) != 0;

    public readonly bool IsProtected() => (_attrs & CharacterAttributes.Protected) != 0;

    public readonly bool BackgroundIsDefault() => _background.IsDefault();

    public readonly bool IsAnyGridLineEnabled()
        => (_attrs & (CharacterAttributes.TopGridline | CharacterAttributes.LeftGridline | CharacterAttributes.RightGridline | CharacterAttributes.BottomGridline)) != 0;

    public readonly bool HasAnyVisualAttributes() => _attrs != CharacterAttributes.Normal || _hyperlinkId != 0;

    /// <summary>
    /// Whether a blank cell drawn with this attribute looks the same as one drawn with <paramref name="other"/>; with
    /// reverse video in effect the foreground is what fills the cell.
    /// </summary>
    public readonly bool HasIdenticalVisualRepresentationForBlankSpace(in TextAttribute other, bool inverted = false)
    {
        bool checkForeground = inverted != IsReverseVideo();
        return !IsAnyGridLineEnabled() &&
            !IsUnderlined() && (_attrs & CharacterAttributes.CrossedOut) == 0 &&
            !IsHyperlink() &&
            _attrs == other._attrs &&
            ((checkForeground && _foreground == other._foreground) || (!checkForeground && _background == other._background)) &&
            IsHyperlink() == other.IsHyperlink();
    }

    public void SetLeftVerticalDisplayed(bool isDisplayed) => UpdateFlag(CharacterAttributes.LeftGridline, isDisplayed);

    public void SetRightVerticalDisplayed(bool isDisplayed) => UpdateFlag(CharacterAttributes.RightGridline, isDisplayed);

    public void SetIntense(bool isIntense) => UpdateFlag(CharacterAttributes.Intense, isIntense);

    public void SetFaint(bool isFaint) => UpdateFlag(CharacterAttributes.Faint, isFaint);

    public void SetItalic(bool isItalic) => UpdateFlag(CharacterAttributes.Italics, isItalic);

    public void SetBlinking(bool isBlinking) => UpdateFlag(CharacterAttributes.Blinking, isBlinking);

    public void SetInvisible(bool isInvisible) => UpdateFlag(CharacterAttributes.Invisible, isInvisible);

    public void SetCrossedOut(bool isCrossedOut) => UpdateFlag(CharacterAttributes.CrossedOut, isCrossedOut);

    public void SetOverlined(bool isOverlined) => UpdateFlag(CharacterAttributes.TopGridline, isOverlined);

    public void SetReverseVideo(bool isReversed) => UpdateFlag(CharacterAttributes.ReverseVideo, isReversed);

    public void SetProtected(bool isProtected) => UpdateFlag(CharacterAttributes.Protected, isProtected);

    public void SetUnderlineStyle(UnderlineStyle style)
    {
        var shiftedStyle = (CharacterAttributes)((int)style << UNDERLINE_STYLE_SHIFT);
        _attrs = (_attrs & ~CharacterAttributes.UnderlineStyle) | shiftedStyle;
    }

    public void Invert() => _attrs ^= CharacterAttributes.ReverseVideo;

    public void SetForeground(TextColor foreground) => _foreground = foreground;

    public void SetBackground(TextColor background) => _background = background;

    public void SetUnderlineColor(TextColor color) => _underlineColor = color;

    public void SetForeground(uint rgbForeground) => _foreground = new TextColor(rgbForeground);

    public void SetBackground(uint rgbBackground) => _background = new TextColor(rgbBackground);

    public void SetIndexedForeground(byte index) => _foreground = new TextColor(index, false);

    public void SetIndexedBackground(byte index) => _background = new TextColor(index, false);

    public void SetIndexedForeground256(byte index) => _foreground = new TextColor(index, true);

    public void SetIndexedBackground256(byte index) => _background = new TextColor(index, true);

    public void SetColor(uint rgbColor, bool isForeground)
    {
        if (isForeground)
        {
            SetForeground(rgbColor);
        }
        else
        {
            SetBackground(rgbColor);
        }
    }

    public void SetHyperlinkId(ushort id) => _hyperlinkId = id;

    public void SetDefaultForeground() => _foreground = default;

    public void SetDefaultBackground() => _background = default;

    public void SetDefaultUnderlineColor() => _underlineColor = default;

    public void SetDefaultRenditionAttributes() => _attrs &= ~CharacterAttributes.Rendition;

    /// <summary>Clears what an erase must not carry into the blanked cells: renditions, the hyperlink and the mark.</summary>
    public void SetStandardErase()
    {
        _attrs = CharacterAttributes.Normal;
        _hyperlinkId = 0;
        _markKind = MarkKind.None;
    }

    public readonly bool Equals(TextAttribute other)
        => _attrs == other._attrs &&
            _hyperlinkId == other._hyperlinkId &&
            _foreground == other._foreground &&
            _background == other._background &&
            _underlineColor == other._underlineColor &&
            _markKind == other._markKind;

    public override readonly bool Equals(object? obj) => obj is TextAttribute other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(_attrs, _hyperlinkId, _foreground, _background, _underlineColor, _markKind);

    public static bool operator ==(TextAttribute left, TextAttribute right) => left.Equals(right);

    public static bool operator !=(TextAttribute left, TextAttribute right) => !left.Equals(right);

    public override readonly string ToString()
        => $"{{FG:{_foreground},BG:{_background},intense:{(IsIntense() ? 1 : 0)},attrs:(0x{(int)_attrs:x2})}}";

    private void UpdateFlag(CharacterAttributes flag, bool isSet)
    {
        if (isSet)
        {
            _attrs |= flag;
        }
        else
        {
            _attrs &= ~flag;
        }
    }
}
