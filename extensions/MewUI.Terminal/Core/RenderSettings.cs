// Ported from Windows Terminal src/renderer/inc/RenderSettings.hpp and src/renderer/base/RenderSettings.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

internal enum ColorAlias
{
    DefaultForeground,
    DefaultBackground,
    FrameForeground,
    FrameBackground,
}

/// <summary>
/// The color table and render modes a cell's attributes resolve against: the 256 indexed colors, the default and
/// frame color aliases, reverse screen, intense-as-bright and synchronized output.
/// </summary>
internal sealed class RenderSettings
{
    [Flags]
    public enum Mode
    {
        None = 0,
        IndexedDistinguishableColors = 1 << 0,
        AlwaysDistinguishableColors = 1 << 1,
        IntenseIsBold = 1 << 2,
        IntenseIsBright = 1 << 3,
        ScreenReversed = 1 << 4,
        SynchronizedOutput = 1 << 5,
    }

    private const int ALIAS_COUNT = 4;

    private readonly uint[] _colorTable = new uint[TextColor.TABLE_SIZE];
    private readonly int[] _colorAliasIndices = new int[ALIAS_COUNT];
    private readonly uint[] _defaultColorTable = new uint[TextColor.TABLE_SIZE];
    private readonly int[] _defaultColorAliasIndices = new int[ALIAS_COUNT];
    private Mode _renderMode = Mode.IntenseIsBright;
    private bool _blinkShouldBeFaint;

    public RenderSettings()
    {
        Types.ColorTable.Initialize(_colorTable);
        SetColorTableEntry(TextColor.DEFAULT_FOREGROUND, TextAttribute.INVALID_COLOR);
        SetColorTableEntry(TextColor.DEFAULT_BACKGROUND, TextAttribute.INVALID_COLOR);
        SetColorTableEntry(TextColor.FRAME_FOREGROUND, TextAttribute.INVALID_COLOR);
        SetColorTableEntry(TextColor.FRAME_BACKGROUND, TextAttribute.INVALID_COLOR);
        SetColorTableEntry(TextColor.CURSOR_COLOR, TextAttribute.INVALID_COLOR);
        SetColorTableEntry(TextColor.SELECTION_BACKGROUND, TextAttribute.INVALID_COLOR);
        SetColorAliasIndex(ColorAlias.DefaultForeground, TextColor.DARK_WHITE);
        SetColorAliasIndex(ColorAlias.DefaultBackground, TextColor.DARK_BLACK);
        SetColorAliasIndex(ColorAlias.FrameForeground, TextColor.FRAME_FOREGROUND);
        SetColorAliasIndex(ColorAlias.FrameBackground, TextColor.FRAME_BACKGROUND);
        SaveDefaultSettings();
    }

    public ReadOnlySpan<uint> ColorTable => _colorTable;

    /// <summary>Keeps the current table and aliases as what a reset (RIS, OSC 104) returns to.</summary>
    public void SaveDefaultSettings()
    {
        _colorTable.CopyTo(_defaultColorTable, 0);
        _colorAliasIndices.CopyTo(_defaultColorAliasIndices, 0);
    }

    public void RestoreDefaultSettings()
    {
        _defaultColorTable.CopyTo(_colorTable, 0);
        _defaultColorAliasIndices.CopyTo(_colorAliasIndices, 0);
        _renderMode &= ~(Mode.ScreenReversed | Mode.SynchronizedOutput);
    }

    public void SetRenderMode(Mode mode, bool enabled)
    {
        if (enabled)
        {
            _renderMode |= mode;
        }
        else
        {
            _renderMode &= ~mode;
        }
    }

    public bool GetRenderMode(Mode mode) => (_renderMode & mode) != 0;

    /// <summary>Takes the colors and modes of <paramref name="other"/>, for a copy the view draws from outside the terminal lock.</summary>
    public void CopyFrom(RenderSettings other)
    {
        other._colorTable.CopyTo(_colorTable, 0);
        other._colorAliasIndices.CopyTo(_colorAliasIndices, 0);
        _renderMode = other._renderMode;
        _blinkShouldBeFaint = other._blinkShouldBeFaint;
    }

    public void ResetColorTable() => Types.ColorTable.Initialize(_colorTable.AsSpan(0, 16));

    public void SetColorTableEntry(int tableIndex, uint color) => _colorTable[tableIndex] = color;

    public uint GetColorTableEntry(int tableIndex) => _colorTable[tableIndex];

    public void RestoreDefaultIndexed256ColorTable() => Array.Copy(_defaultColorTable, _colorTable, 256);

    public void RestoreDefaultColorTableEntry(int tableIndex) => _colorTable[tableIndex] = _defaultColorTable[tableIndex];

    public void SetColorAlias(ColorAlias alias, int tableIndex, uint color)
    {
        SetColorAliasIndex(alias, tableIndex);
        SetColorTableEntry(tableIndex, color);
    }

    public uint GetColorAlias(ColorAlias alias) => GetColorTableEntry(GetColorAliasIndex(alias));

    public void SetColorAliasIndex(ColorAlias alias, int tableIndex)
    {
        if (tableIndex < TextColor.TABLE_SIZE)
        {
            _colorAliasIndices[(int)alias] = tableIndex;
        }
    }

    public int GetColorAliasIndex(ColorAlias alias) => _colorAliasIndices[(int)alias];

    public void RestoreDefaultColorAliasIndex(ColorAlias alias) => _colorAliasIndices[(int)alias] = _defaultColorAliasIndices[(int)alias];

    /// <summary>
    /// The foreground and background a cell is drawn with, after intense-as-bright, faint (and faint blink), reverse
    /// video against the screen mode, and invisible text.
    /// </summary>
    public (uint Foreground, uint Background) GetAttributeColors(in TextAttribute attr)
    {
        var foregroundColor = attr.Foreground;
        var backgroundColor = attr.Background;
        int defaultForegroundIndex = GetColorAliasIndex(ColorAlias.DefaultForeground);
        int defaultBackgroundIndex = GetColorAliasIndex(ColorAlias.DefaultBackground);
        bool brightenForeground = attr.IsIntense() && GetRenderMode(Mode.IntenseIsBright);
        bool dimForeground = attr.IsFaint() || (_blinkShouldBeFaint && attr.IsBlinking());
        bool swapForegroundAndBackground = attr.IsReverseVideo() ^ GetRenderMode(Mode.ScreenReversed);

        uint foreground = foregroundColor.GetColor(_colorTable, defaultForegroundIndex, brightenForeground);
        uint background = backgroundColor.GetColor(_colorTable, defaultBackgroundIndex);

        if (dimForeground)
        {
            foreground = (foreground >> 1) & 0x7F7F7F;
        }

        if (swapForegroundAndBackground)
        {
            (foreground, background) = (background, foreground);
        }

        if (attr.IsInvisible())
        {
            foreground = background;
        }

        return (foreground, background);
    }

    /// <summary>
    /// As <see cref="GetAttributeColors"/>, with an opaque alpha on the foreground, and on the background unless the
    /// cell shows the default background.
    /// </summary>
    public (uint Foreground, uint Background) GetAttributeColorsWithAlpha(in TextAttribute attr)
    {
        var (foreground, background) = GetAttributeColors(attr);
        foreground |= 0xFF000000;
        if (!attr.BackgroundIsDefault() || (attr.IsReverseVideo() ^ GetRenderMode(Mode.ScreenReversed)) || attr.IsInvisible())
        {
            background |= 0xFF000000;
        }

        return (foreground, background);
    }

    public uint GetAttributeUnderlineColor(in TextAttribute attr)
    {
        var (foreground, background) = GetAttributeColors(attr);
        var underlineColor = attr.UnderlineColor;
        if (underlineColor.IsDefault())
        {
            return foreground;
        }

        int defaultUnderlineIndex = GetColorAliasIndex(ColorAlias.DefaultForeground);
        uint underline = underlineColor.GetColor(_colorTable, defaultUnderlineIndex, true);
        if (attr.IsInvisible())
        {
            underline = background;
        }

        return underline;
    }

    public void ToggleBlinkRendition() => _blinkShouldBeFaint = !_blinkShouldBeFaint;
}
