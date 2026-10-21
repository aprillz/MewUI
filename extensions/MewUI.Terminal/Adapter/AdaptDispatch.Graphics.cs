// Ported from Windows Terminal src/terminal/adapter/adaptDispatchGraphics.cpp and adaptDispatch.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Adapter;

internal sealed partial class AdaptDispatch
{
    // OSC 10 to 19 resources by number: the color table entry, and the alias that points at it, if any.
    private static readonly (int ColorTableIndex, int AliasIndex)[] _xtermResourceColorTableMappings =
    [
        (TextColor.DEFAULT_FOREGROUND, (int)ColorAlias.DefaultForeground),
        (TextColor.DEFAULT_BACKGROUND, (int)ColorAlias.DefaultBackground),
        (TextColor.CURSOR_COLOR, -1),
        (-1, -1),
        (-1, -1),
        (-1, -1),
        (-1, -1),
        (TextColor.SELECTION_BACKGROUND, -1),
        (-1, -1),
        (-1, -1),
    ];

    private static void SetUnderlineStyleHelper(VTParameter option, ref TextAttribute attr)
    {
        int style = option.ValueOr(0);
        if (style >= 0 && style <= (int)UnderlineStyle.Max)
        {
            attr.SetUnderlineStyle((UnderlineStyle)style);
        }
    }

    /// <summary>Applies the colon-less 38/48 forms (<c>38;2;r;g;b</c>, <c>38;5;n</c>); returns the options used.</summary>
    private static int SetRgbColorsHelper(VTParameters options, ref TextAttribute attr, bool isForeground)
    {
        int optionsConsumed = 1;
        var typeOpt = (GraphicsOptions)options.At(0).ValueOr(0);
        if (typeOpt == GraphicsOptions.RGBColorOrFaint)
        {
            optionsConsumed = 4;
            int red = options.At(1).ValueOr(0);
            int green = options.At(2).ValueOr(0);
            int blue = options.At(3).ValueOr(0);
            if (red <= 255 && green <= 255 && blue <= 255)
            {
                attr.SetColor(TextColor.Rgb((byte)red, (byte)green, (byte)blue), isForeground);
            }
        }
        else if (typeOpt == GraphicsOptions.BlinkOrXterm256Index)
        {
            optionsConsumed = 2;
            int tableIndex = options.At(1).ValueOr(0);
            if (tableIndex <= 255)
            {
                if (isForeground)
                {
                    attr.SetIndexedForeground256((byte)tableIndex);
                }
                else
                {
                    attr.SetIndexedBackground256((byte)tableIndex);
                }
            }
        }

        return optionsConsumed;
    }

    /// <summary>Applies the colon forms of 38, 48 and 58 (<c>38:2:cs:r:g:b</c>, <c>38:5:n</c>).</summary>
    private static void SetRgbColorsHelperFromSubParams(VTParameter colorItem, VTSubParameters options, ref TextAttribute attr)
    {
        TextColor? color = null;
        var typeOpt = (GraphicsOptions)options.At(0).ValueOr(0);
        if (typeOpt == GraphicsOptions.RGBColorOrFaint)
        {
            // Only the default color space (0 or omitted) is supported.
            bool validColorSpace = options.At(1).ValueOr(0) == 0;
            int red = options.At(2).ValueOr(0);
            int green = options.At(3).ValueOr(0);
            int blue = options.At(4).ValueOr(0);
            if (validColorSpace && red <= 255 && green <= 255 && blue <= 255)
            {
                color = new TextColor(TextColor.Rgb((byte)red, (byte)green, (byte)blue));
            }
        }
        else if (typeOpt == GraphicsOptions.BlinkOrXterm256Index)
        {
            int tableIndex = options.At(1).ValueOr(0);
            if (tableIndex <= 255)
            {
                color = new TextColor((byte)tableIndex, true);
            }
        }

        if (color is TextColor newColor)
        {
            switch ((GraphicsOptions)colorItem.ValueOr(0))
            {
                case GraphicsOptions.ForegroundExtended:
                    attr.SetForeground(newColor);
                    break;
                case GraphicsOptions.BackgroundExtended:
                    attr.SetBackground(newColor);
                    break;
                case GraphicsOptions.UnderlineColor:
                    attr.SetUnderlineColor(newColor);
                    break;
            }
        }
    }

    /// <summary>Applies the SGR option at <paramref name="optionIndex"/>; returns how many options it used.</summary>
    private static int ApplyGraphicsOption(VTParameters options, int optionIndex, ref TextAttribute attr)
    {
        var option = options.At(optionIndex);
        if (options.HasSubParamsFor(optionIndex))
        {
            ApplyGraphicsOptionWithSubParams(option, options.SubParamsFor(optionIndex), ref attr);
            return 1;
        }

        switch ((GraphicsOptions)option.ValueOr(0))
        {
            case GraphicsOptions.Off:
                attr.SetDefaultForeground();
                attr.SetDefaultBackground();
                attr.SetDefaultUnderlineColor();
                attr.SetDefaultRenditionAttributes();
                return 1;
            case GraphicsOptions.ForegroundDefault:
                attr.SetDefaultForeground();
                return 1;
            case GraphicsOptions.BackgroundDefault:
                attr.SetDefaultBackground();
                return 1;
            case GraphicsOptions.UnderlineColorDefault:
                attr.SetDefaultUnderlineColor();
                return 1;
            case GraphicsOptions.Intense:
                attr.SetIntense(true);
                return 1;
            case GraphicsOptions.RGBColorOrFaint:
                attr.SetFaint(true);
                return 1;
            case GraphicsOptions.NotIntenseOrFaint:
                attr.SetIntense(false);
                attr.SetFaint(false);
                return 1;
            case GraphicsOptions.Italics:
                attr.SetItalic(true);
                return 1;
            case GraphicsOptions.NotItalics:
                attr.SetItalic(false);
                return 1;
            case GraphicsOptions.BlinkOrXterm256Index:
            case GraphicsOptions.RapidBlink:
                attr.SetBlinking(true);
                return 1;
            case GraphicsOptions.Steady:
                attr.SetBlinking(false);
                return 1;
            case GraphicsOptions.Invisible:
                attr.SetInvisible(true);
                return 1;
            case GraphicsOptions.Visible:
                attr.SetInvisible(false);
                return 1;
            case GraphicsOptions.CrossedOut:
                attr.SetCrossedOut(true);
                return 1;
            case GraphicsOptions.NotCrossedOut:
                attr.SetCrossedOut(false);
                return 1;
            case GraphicsOptions.Negative:
                attr.SetReverseVideo(true);
                return 1;
            case GraphicsOptions.Positive:
                attr.SetReverseVideo(false);
                return 1;
            case GraphicsOptions.Underline:
                attr.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
                return 1;
            case GraphicsOptions.DoublyUnderlined:
                attr.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
                return 1;
            case GraphicsOptions.NoUnderline:
                attr.SetUnderlineStyle(UnderlineStyle.NoUnderline);
                return 1;
            case GraphicsOptions.Overline:
                attr.SetOverlined(true);
                return 1;
            case GraphicsOptions.NoOverline:
                attr.SetOverlined(false);
                return 1;
            case >= GraphicsOptions.ForegroundBlack and <= GraphicsOptions.ForegroundWhite:
                attr.SetIndexedForeground((byte)(option.Value - (int)GraphicsOptions.ForegroundBlack));
                return 1;
            case >= GraphicsOptions.BackgroundBlack and <= GraphicsOptions.BackgroundWhite:
                attr.SetIndexedBackground((byte)(option.Value - (int)GraphicsOptions.BackgroundBlack));
                return 1;
            case >= GraphicsOptions.BrightForegroundBlack and <= GraphicsOptions.BrightForegroundWhite:
                attr.SetIndexedForeground((byte)(option.Value - (int)GraphicsOptions.BrightForegroundBlack + TextColor.BRIGHT_BLACK));
                return 1;
            case >= GraphicsOptions.BrightBackgroundBlack and <= GraphicsOptions.BrightBackgroundWhite:
                attr.SetIndexedBackground((byte)(option.Value - (int)GraphicsOptions.BrightBackgroundBlack + TextColor.BRIGHT_BLACK));
                return 1;
            case GraphicsOptions.ForegroundExtended:
                return 1 + SetRgbColorsHelper(options.Subspan(optionIndex + 1), ref attr, true);
            case GraphicsOptions.BackgroundExtended:
                return 1 + SetRgbColorsHelper(options.Subspan(optionIndex + 1), ref attr, false);
            default:
                return 1;
        }
    }

    private static void ApplyGraphicsOptionWithSubParams(VTParameter option, VTSubParameters subParams, ref TextAttribute attr)
    {
        switch ((GraphicsOptions)option.ValueOr(0))
        {
            case GraphicsOptions.Underline:
                SetUnderlineStyleHelper(subParams.At(0), ref attr);
                break;
            case GraphicsOptions.ForegroundExtended:
            case GraphicsOptions.BackgroundExtended:
            case GraphicsOptions.UnderlineColor:
                SetRgbColorsHelperFromSubParams(option, subParams, ref attr);
                break;
        }
    }

    private static void ApplyGraphicsOptions(VTParameters options, ref TextAttribute attr)
    {
        for (int index = 0; index < options.Size;)
        {
            index += ApplyGraphicsOption(options, index, ref attr);
        }
    }

    public override void SetGraphicsRendition(VTParameters options)
    {
        var page = _pages.ActivePage();
        var attr = page.Attributes;
        ApplyGraphicsOptions(options, ref attr);
        page.SetAttributes(attr);
    }

    public override void SetCharacterProtectionAttribute(VTParameters options)
    {
        var page = _pages.ActivePage();
        var attr = page.Attributes;
        for (int index = 0; index < options.Size; index++)
        {
            switch ((LogicalAttributeOptions)options.At(index).ValueOr(0))
            {
                case LogicalAttributeOptions.Default:
                    attr.SetProtected(false);
                    break;
                case LogicalAttributeOptions.Protected:
                    attr.SetProtected(true);
                    break;
                case LogicalAttributeOptions.Unprotected:
                    attr.SetProtected(false);
                    break;
            }
        }

        page.SetAttributes(attr);
    }

    public override void PushGraphicsRendition(VTParameters options) => _sgrStack.Push(_pages.ActivePage().Attributes, options);

    public override void PopGraphicsRendition()
    {
        var page = _pages.ActivePage();
        page.SetAttributes(_sgrStack.Pop(page.Attributes));
    }

    public override void SetClipboard(string content) => _api.CopyToClipboard(content);

    public override void SetColorTableEntry(int tableIndex, uint color)
    {
        _renderSettings.SetColorTableEntry(tableIndex, color);

        // The window background follows the default background and frame background colors.
        if (_renderer != null)
        {
            bool backgroundChanged = tableIndex == _renderSettings.GetColorAliasIndex(ColorAlias.DefaultBackground);
            bool frameChanged = tableIndex == _renderSettings.GetColorAliasIndex(ColorAlias.FrameBackground);
            _renderer.TriggerRedrawAll(backgroundChanged, frameChanged);
        }
    }

    public override void RequestColorTableEntry(int tableIndex)
    {
        uint color = _renderSettings.GetColorTableEntry(tableIndex);
        if (color != TextAttribute.INVALID_COLOR)
        {
            ReturnOscResponse($"4;{tableIndex};{FormatXColor(color)}");
        }
    }

    /// <summary>A color as XParseColor <c>rgb:rrrr/gggg/bbbb</c>, with each 8-bit component repeated.</summary>
    private static string FormatXColor(uint color)
    {
        int red = (int)(color & 0xFF) * 0x0101;
        int green = (int)((color >> 8) & 0xFF) * 0x0101;
        int blue = (int)((color >> 16) & 0xFF) * 0x0101;
        return $"rgb:{red:x4}/{green:x4}/{blue:x4}";
    }

    public override void ResetColorTable()
    {
        _renderSettings.RestoreDefaultIndexed256ColorTable();
        _renderer?.TriggerRedrawAll(true, true);
    }

    public override void ResetColorTableEntry(int tableIndex)
    {
        _renderSettings.RestoreDefaultColorTableEntry(tableIndex);
        if (_renderer != null)
        {
            bool backgroundChanged = tableIndex == _renderSettings.GetColorAliasIndex(ColorAlias.DefaultBackground);
            bool frameChanged = tableIndex == _renderSettings.GetColorAliasIndex(ColorAlias.FrameBackground);
            _renderer.TriggerRedrawAll(backgroundChanged, frameChanged);
        }
    }

    public override void SetXtermColorResource(int resource, uint color)
    {
        var (colorTableIndex, aliasIndex) = _xtermResourceColorTableMappings[resource - 10];
        if (colorTableIndex > 0)
        {
            // Setting the default colors also points their aliases back at the default entries.
            if (aliasIndex >= 0)
            {
                _renderSettings.SetColorAliasIndex((ColorAlias)aliasIndex, colorTableIndex);
            }

            SetColorTableEntry(colorTableIndex, color);
        }
    }

    public override void RequestXtermColorResource(int resource)
    {
        var (colorTableIndex, aliasIndex) = _xtermResourceColorTableMappings[resource - 10];
        if (colorTableIndex > 0)
        {
            int finalColorIndex = colorTableIndex;

            // An aliased resource reports the entry its alias points at.
            if (aliasIndex >= 0)
            {
                finalColorIndex = _renderSettings.GetColorAliasIndex((ColorAlias)aliasIndex);
            }

            uint color = _renderSettings.GetColorTableEntry(finalColorIndex);
            if (color != TextAttribute.INVALID_COLOR)
            {
                ReturnOscResponse($"{resource};{FormatXColor(color)}");
            }
        }
    }

    public override void ResetXtermColorResource(int resource)
    {
        var (colorTableIndex, aliasIndex) = _xtermResourceColorTableMappings[resource - 10];
        if (colorTableIndex > 0)
        {
            if (aliasIndex >= 0)
            {
                _renderSettings.RestoreDefaultColorAliasIndex((ColorAlias)aliasIndex);
            }

            ResetColorTableEntry(colorTableIndex);
        }
    }

    public override void AssignColor(ColorItem item, int foregroundIndex, int backgroundIndex)
    {
        switch (item)
        {
            case ColorItem.NormalText:
                _renderSettings.SetColorAliasIndex(ColorAlias.DefaultForeground, foregroundIndex);
                _renderSettings.SetColorAliasIndex(ColorAlias.DefaultBackground, backgroundIndex);
                break;
            case ColorItem.WindowFrame:
                _renderSettings.SetColorAliasIndex(ColorAlias.FrameForeground, foregroundIndex);
                _renderSettings.SetColorAliasIndex(ColorAlias.FrameBackground, backgroundIndex);
                break;
            default:
                return;
        }

        _renderer?.TriggerRedrawAll(item == ColorItem.NormalText, item == ColorItem.WindowFrame);
    }

    public override void AddHyperlink(ReadOnlySpan<char> uri, ReadOnlySpan<char> parameters)
    {
        var page = _pages.ActivePage();
        var attr = page.Attributes;
        string uriString = uri.ToString();
        ushort id = page.Buffer.GetHyperlinkId(uriString, parameters.ToString());
        attr.SetHyperlinkId(id);
        page.SetAttributes(attr);
        page.Buffer.AddHyperlinkToMap(uriString, id);
    }

    public override void EndHyperlink()
    {
        var page = _pages.ActivePage();
        var attr = page.Attributes;
        attr.SetHyperlinkId(0);
        page.SetAttributes(attr);
    }

    public override void DoConEmuAction(ReadOnlySpan<char> text)
    {
        const int TASKBAR_MAX_STATE = 4;
        const int TASKBAR_MAX_PROGRESS = 100;

        uint state = 0;
        uint progress = 0;

        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 1 || !VTUtils.TryStringToUint(parts[0], out uint subParam))
        {
            return;
        }

        if (subParam == 4)
        {
            // OSC 9;4;state;progress sets the taskbar progress; empty fields count as 0.
            if (parts.Count >= 2)
            {
                if (!VTUtils.TryStringToUint(parts[1], out state) && parts[1].Length != 0)
                {
                    return;
                }

                if (parts.Count >= 3 && !VTUtils.TryStringToUint(parts[2], out progress) && parts[2].Length != 0)
                {
                    return;
                }
            }

            if (state > TASKBAR_MAX_STATE)
            {
                return;
            }

            progress = Math.Min(progress, TASKBAR_MAX_PROGRESS);
            _api.SetTaskbarProgress((TaskbarState)state, (int)progress);
        }
        else if (subParam == 9)
        {
            // OSC 9;9;path sets the working directory; the path may be quoted.
            if (parts.Count >= 2)
            {
                string path = parts[1];
                if (path.Length >= 3 && path[0] == '"' && path[^1] == '"')
                {
                    path = path[1..^1];
                }

                if (!IsLegalPath(path))
                {
                    return;
                }

                _api.SetWorkingDirectory(path);
            }
        }
        else if (subParam == 12)
        {
            // OSC 9;12 marks a prompt.
            _pages.ActivePage().Buffer.StartCommand();
            _api.NotifyShellIntegrationMark();
        }
    }

    public override void DoITerm2Action(ReadOnlySpan<char> text)
    {
        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 1)
        {
            return;
        }

        if (parts[0] == "SetMark")
        {
            _pages.ActivePage().Buffer.StartPrompt();
            _api.NotifyShellIntegrationMark();
        }
    }

    public override void DoFinalTermAction(ReadOnlySpan<char> text)
    {
        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 1 || parts[0].Length != 1)
        {
            return;
        }

        var buffer = _pages.ActivePage().Buffer;
        switch (parts[0][0])
        {
            case 'A':
                // FTCS_PROMPT
                buffer.StartPrompt();
                _api.NotifyShellIntegrationMark();
                break;
            case 'B':
                // FTCS_COMMAND_START
                buffer.StartCommand();
                _api.NotifyShellIntegrationMark();
                break;
            case 'C':
                // FTCS_COMMAND_EXECUTED
                buffer.StartOutput();
                _api.NotifyShellIntegrationMark();
                break;
            case 'D':
                // FTCS_COMMAND_FINISHED; an exit code that does not parse counts as an error.
                uint? error = null;
                if (parts.Count >= 2)
                {
                    error = VTUtils.TryStringToUint(parts[1], out uint parsedError) ? parsedError : uint.MaxValue;
                }

                buffer.EndCurrentCommand(error);
                _api.NotifyShellIntegrationMark();
                break;
        }
    }

    public override void DoVsCodeAction(ReadOnlySpan<char> text)
    {
        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 1 || parts[0] != "Completions")
        {
            return;
        }

        // OSC 633;Completions;replacementIndex;replacementLength;cursorIndex;json
        bool succeeded = parts.Count >= 2 && VTUtils.TryStringToUint(parts[1], out _);
        uint replacementLength = 0;
        succeeded &= parts.Count >= 3 && VTUtils.TryStringToUint(parts[2], out replacementLength);
        succeeded &= parts.Count >= 4 && VTUtils.TryStringToUint(parts[3], out _);
        if (succeeded)
        {
            // The JSON may contain semicolons, so it is everything after the fourth one.
            int prefixLength = parts[0].Length + 1 + parts[1].Length + 1 + parts[2].Length + 1 + parts[3].Length + 1;
            if (prefixLength > text.Length)
            {
                return;
            }

            var remainder = text[prefixLength..];
            _api.InvokeCompletions(parts.Count < 5 ? ReadOnlySpan<char>.Empty : remainder, (int)replacementLength);
        }
    }

    public override void DoWTAction(ReadOnlySpan<char> text)
    {
        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 1)
        {
            return;
        }

        if (parts[0] == "CmdNotFound" && parts.Count >= 2)
        {
            _api.SearchMissingCommand(parts[1]);
        }
    }

    public override void DoUrxvtAction(ReadOnlySpan<char> text)
    {
        if ((_optionalFeatures & OptionalFeature.DesktopNotification) == 0)
        {
            return;
        }

        // OSC 777;notify;title;body, where the body may contain semicolons.
        var rest = text;
        var action = NextSplit(ref rest);
        if (action.SequenceEqual("notify"))
        {
            var title = NextSplit(ref rest);
            _api.ShowNotification(title, rest);
        }
    }

    /// <summary>The text up to the next semicolon, leaving <paramref name="text"/> after it (or empty).</summary>
    private static ReadOnlySpan<char> NextSplit(ref ReadOnlySpan<char> text)
    {
        int separator = text.IndexOf(';');
        if (separator < 0)
        {
            var whole = text;
            text = default;
            return whole;
        }
        else
        {
            var part = text[..separator];
            text = text[(separator + 1)..];
            return part;
        }
    }
}
