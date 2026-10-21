// Ported from Windows Terminal src/types/inc/sgrStack.hpp and sgrStack.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Parser;

namespace Aprillz.MewUI.Terminal.Adapter;

/// <summary>The XTPUSHSGR/XTPOPSGR stack: a ring of saved attributes, each with the parts that were asked to be saved.</summary>
internal sealed class SgrStack
{
    public const int MAX_STORED_SGR_PUSHES = 10;

    private readonly (TextAttribute Attributes, uint ValidParts)[] _storedSgrAttributes = new (TextAttribute, uint)[MAX_STORED_SGR_PUSHES];
    private int _nextPushIndex;
    private int _numSavedAttrs;

    public void Push(TextAttribute currentAttributes, VTParameters options)
    {
        uint validParts = 0;
        if (options.IsEmpty)
        {
            validParts = PartBit(SgrSaveRestoreStackOptions.All);
        }
        else
        {
            for (int index = 0; index < options.Size; index++)
            {
                var option = (SgrSaveRestoreStackOptions)options.At(index).ValueOr(0);
                if (option > SgrSaveRestoreStackOptions.All && option <= SgrSaveRestoreStackOptions.Max)
                {
                    validParts |= PartBit(option);
                }
            }
        }

        // A full stack drops its oldest entry.
        if (_numSavedAttrs < MAX_STORED_SGR_PUSHES)
        {
            _numSavedAttrs++;
        }

        _storedSgrAttributes[_nextPushIndex] = (currentAttributes, validParts);
        _nextPushIndex = (_nextPushIndex + 1) % MAX_STORED_SGR_PUSHES;
    }

    public TextAttribute Pop(TextAttribute currentAttributes)
    {
        if (_numSavedAttrs > 0)
        {
            _numSavedAttrs--;
            _nextPushIndex = _nextPushIndex == 0 ? MAX_STORED_SGR_PUSHES - 1 : _nextPushIndex - 1;

            var (savedAttributes, validParts) = _storedSgrAttributes[_nextPushIndex];
            if (Has(validParts, SgrSaveRestoreStackOptions.All))
            {
                return savedAttributes;
            }

            return CombineWithCurrentAttributes(currentAttributes, savedAttributes, validParts);
        }

        return currentAttributes;
    }

    private static uint PartBit(SgrSaveRestoreStackOptions option) => 1u << (int)option;

    private static bool Has(uint validParts, SgrSaveRestoreStackOptions option) => (validParts & PartBit(option)) != 0;

    private static TextAttribute CombineWithCurrentAttributes(TextAttribute currentAttributes, TextAttribute savedAttribute, uint validParts)
    {
        var result = currentAttributes;
        if (Has(validParts, SgrSaveRestoreStackOptions.Intense))
        {
            result.SetIntense(savedAttribute.IsIntense());
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.Faintness))
        {
            result.SetFaint(savedAttribute.IsFaint());
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.Italics))
        {
            result.SetItalic(savedAttribute.IsItalic());
        }

        // Single and double underline are saved separately but share one style field.
        bool isUnderlinedValid = Has(validParts, SgrSaveRestoreStackOptions.Underline);
        bool isDoublyUnderlinedValid = Has(validParts, SgrSaveRestoreStackOptions.DoublyUnderlined);
        if (isUnderlinedValid && isDoublyUnderlinedValid)
        {
            result.SetUnderlineStyle(savedAttribute.UnderlineStyle);
        }
        else if (isUnderlinedValid)
        {
            var savedStyle = savedAttribute.UnderlineStyle;
            if (savedStyle != UnderlineStyle.NoUnderline && savedStyle != UnderlineStyle.DoublyUnderlined)
            {
                result.SetUnderlineStyle(savedStyle);
            }
            else if (result.UnderlineStyle != UnderlineStyle.DoublyUnderlined)
            {
                result.SetUnderlineStyle(UnderlineStyle.NoUnderline);
            }
        }
        else if (isDoublyUnderlinedValid)
        {
            if (savedAttribute.UnderlineStyle == UnderlineStyle.DoublyUnderlined)
            {
                result.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
            }
            else if (result.UnderlineStyle == UnderlineStyle.DoublyUnderlined)
            {
                result.SetUnderlineStyle(UnderlineStyle.NoUnderline);
            }
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.Blink))
        {
            result.SetBlinking(savedAttribute.IsBlinking());
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.Negative))
        {
            result.SetReverseVideo(savedAttribute.IsReverseVideo());
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.Invisible))
        {
            result.SetInvisible(savedAttribute.IsInvisible());
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.CrossedOut))
        {
            result.SetCrossedOut(savedAttribute.IsCrossedOut());
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.SaveForegroundColor))
        {
            result.SetForeground(savedAttribute.Foreground);
        }

        if (Has(validParts, SgrSaveRestoreStackOptions.SaveBackgroundColor))
        {
            result.SetBackground(savedAttribute.Background);
        }

        return result;
    }
}
