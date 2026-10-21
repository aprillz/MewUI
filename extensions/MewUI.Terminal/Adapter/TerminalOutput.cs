// Ported from Windows Terminal src/terminal/adapter/terminalOutput.hpp and terminalOutput.cpp (MIT).

using Aprillz.MewUI.Terminal.Parser;

namespace Aprillz.MewUI.Terminal.Adapter;

/// <summary>
/// The G0 to G3 character set designations and the GL, GR and single shifts, which translate printed characters
/// (DEC Special Graphics line drawing, national replacement sets, ISO Latin supplements).
/// </summary>
internal sealed class TerminalOutput
{
    private static readonly VTID _idAscii = VTID.FromString("B");
    private static readonly VTID _idUserPreference = VTID.FromString("<");
    private static readonly VTID _idLatin1 = VTID.FromString("A");

    private readonly string[] _gsetTranslationTables = new string[4];
    private readonly VTID[] _gsetIds = new VTID[4];
    private VTID _upssId;
    private string _upssTranslationTable;
    private int _glSetNumber;
    private int _grSetNumber = 2;
    private string _glTranslationTable = string.Empty;
    private string _grTranslationTable = string.Empty;
    private int _ssSetNumber;
    private bool _grTranslationEnabled;
    private VTID _drcsId;
    private string _drcsTranslationTable = string.Empty;

    public TerminalOutput(bool grEnabled = false)
    {
        _upssId = _idLatin1;
        _upssTranslationTable = Charsets.Latin1;
        _grTranslationEnabled = grEnabled;

        string grTranslationTable = grEnabled ? _upssTranslationTable : Charsets.Ascii;
        var grId = grEnabled ? _idUserPreference : _idAscii;
        _gsetTranslationTables[0] = Charsets.Ascii;
        _gsetTranslationTables[1] = Charsets.Ascii;
        _gsetTranslationTables[2] = grTranslationTable;
        _gsetTranslationTables[3] = grTranslationTable;
        _gsetIds[0] = _idAscii;
        _gsetIds[1] = _idAscii;
        _gsetIds[2] = grId;
        _gsetIds[3] = grId;
    }

    public TerminalOutput Clone()
    {
        var clone = new TerminalOutput(_grTranslationEnabled);
        clone.CopyFrom(this);
        return clone;
    }

    private void CopyFrom(TerminalOutput other)
    {
        Array.Copy(other._gsetTranslationTables, _gsetTranslationTables, 4);
        Array.Copy(other._gsetIds, _gsetIds, 4);
        _upssId = other._upssId;
        _upssTranslationTable = other._upssTranslationTable;
        _glSetNumber = other._glSetNumber;
        _grSetNumber = other._grSetNumber;
        _glTranslationTable = other._glTranslationTable;
        _grTranslationTable = other._grTranslationTable;
        _ssSetNumber = other._ssSetNumber;
        _grTranslationEnabled = other._grTranslationEnabled;
        _drcsId = other._drcsId;
        _drcsTranslationTable = other._drcsTranslationTable;
    }

    public void SoftReset() => RestoreFrom(new TerminalOutput(_grTranslationEnabled));

    /// <summary>Restores saved designations, keeping GR translation and the current soft font.</summary>
    public void RestoreFrom(TerminalOutput savedState)
    {
        bool preserveGrTranslation = _grTranslationEnabled;
        var preserveDrcsId = _drcsId;
        int preserveDrcsSize = _drcsTranslationTable.Length;
        CopyFrom(savedState);
        _grTranslationEnabled = preserveGrTranslation;
        if (preserveDrcsId.Value != 0)
        {
            if (preserveDrcsSize == 96)
            {
                SetDrcs96Designation(preserveDrcsId);
            }
            else
            {
                SetDrcs94Designation(preserveDrcsId);
            }
        }
    }

    public void AssignUserPreferenceCharset(VTID charset, bool size96)
    {
        string translationTable = size96 ? LookupTranslationTable96(charset) : LookupTranslationTable94(charset);
        if (translationTable.Length == 0)
        {
            return;
        }

        _upssId = charset;
        _upssTranslationTable = translationTable;

        // Sets designated as the user preference set follow the new assignment.
        for (int gset = 0; gset < 4; gset++)
        {
            if (_gsetIds[gset] == _idUserPreference)
            {
                _gsetTranslationTables[gset] = _upssTranslationTable;
            }
        }

        LockingShift(_glSetNumber);
        LockingShiftRight(_grSetNumber);
    }

    public VTID UserPreferenceCharsetId => _upssId;

    public int UserPreferenceCharsetSize => _upssTranslationTable.Length == 96 ? 96 : 94;

    public void Designate94Charset(int gsetNumber, VTID charset)
    {
        string translationTable = LookupTranslationTable94(charset);
        if (translationTable.Length == 0)
        {
            return;
        }

        _gsetIds[gsetNumber] = charset;
        SetTranslationTable(gsetNumber, translationTable);
    }

    public void Designate96Charset(int gsetNumber, VTID charset)
    {
        string translationTable = LookupTranslationTable96(charset);
        if (translationTable.Length == 0)
        {
            return;
        }

        _gsetIds[gsetNumber] = charset;
        SetTranslationTable(gsetNumber, translationTable);
    }

    public void SetDrcs94Designation(VTID charset)
    {
        ReplaceDrcsTable(LookupTranslationTable94(charset), Charsets.Drcs94);
        _drcsId = charset;
        _drcsTranslationTable = Charsets.Drcs94;
    }

    public void SetDrcs96Designation(VTID charset)
    {
        ReplaceDrcsTable(LookupTranslationTable96(charset), Charsets.Drcs96);
        _drcsId = charset;
        _drcsTranslationTable = Charsets.Drcs96;
    }

    public VTID GetCharsetId(int gsetNumber) => _gsetIds[gsetNumber];

    public int GetCharsetSize(int gsetNumber) => _gsetTranslationTables[gsetNumber].Length == 96 ? 96 : 94;

    public void LockingShift(int gsetNumber)
    {
        _glSetNumber = gsetNumber;
        _glTranslationTable = _gsetTranslationTables[_glSetNumber];

        // ASCII in GL needs no translation.
        if (ReferenceEquals(_glTranslationTable, Charsets.Ascii))
        {
            _glTranslationTable = string.Empty;
        }
    }

    public void LockingShiftRight(int gsetNumber)
    {
        _grSetNumber = gsetNumber;
        _grTranslationTable = _gsetTranslationTables[_grSetNumber];

        // Latin-1 in GR needs no translation, and without GR translation nothing in GR is translated.
        if (ReferenceEquals(_grTranslationTable, Charsets.Latin1) || !_grTranslationEnabled)
        {
            _grTranslationTable = string.Empty;
        }
    }

    public void SingleShift(int gsetNumber) => _ssSetNumber = gsetNumber;

    public int LeftSetNumber => _glSetNumber;

    public int RightSetNumber => _grSetNumber;

    public bool IsSingleShiftPending(int gsetNumber) => _ssSetNumber == gsetNumber;

    public bool NeedToTranslate => _glTranslationTable.Length != 0 || _grTranslationTable.Length != 0 || _ssSetNumber != 0;

    public void EnableGrTranslation(bool enabled)
    {
        _grTranslationEnabled = enabled;
        string defaultTranslationTable = enabled ? _upssTranslationTable : Charsets.Ascii;
        var defaultId = enabled ? _idUserPreference : _idAscii;
        _gsetTranslationTables[2] = defaultTranslationTable;
        _gsetTranslationTables[3] = defaultTranslationTable;
        _gsetIds[2] = defaultId;
        _gsetIds[3] = defaultId;
        LockingShift(_glSetNumber);
        LockingShiftRight(_grSetNumber);
    }

    /// <summary>The character to print for <paramref name="character"/> under the current shifts; a single shift is used up.</summary>
    public char TranslateKey(char character)
    {
        char found = character;
        if (_ssSetNumber == 2 || _ssSetNumber == 3)
        {
            string ssTranslationTable = _gsetTranslationTables[_ssSetNumber];
            if ((uint)(character - 0x20) < (uint)ssTranslationTable.Length)
            {
                found = ssTranslationTable[character - 0x20];
            }
            else if ((uint)(character - 0xA0) < (uint)ssTranslationTable.Length)
            {
                found = ssTranslationTable[character - 0xA0];
            }

            _ssSetNumber = 0;
        }
        else if ((uint)(character - 0x20) < (uint)_glTranslationTable.Length)
        {
            found = _glTranslationTable[character - 0x20];
        }
        else if ((uint)(character - 0xA0) < (uint)_grTranslationTable.Length)
        {
            found = _grTranslationTable[character - 0xA0];
        }

        return found;
    }

    private string LookupTranslationTable94(VTID charset)
    {
        if (charset == _drcsId && _drcsId.Value != 0)
        {
            return _drcsTranslationTable;
        }

        return charset.ToString() switch
        {
            "B" or "1" => Charsets.Ascii,
            "0" or "2" => Charsets.DecSpecialGraphics,
            "<" => _upssTranslationTable,
            "A" => Charsets.BritishNrcs,
            "4" => Charsets.DutchNrcs,
            "5" or "C" => Charsets.FinnishNrcs,
            "R" => Charsets.FrenchNrcs,
            "f" => Charsets.FrenchNrcsIso,
            "9" or "Q" => Charsets.FrenchCanadianNrcs,
            "K" => Charsets.GermanNrcs,
            "Y" => Charsets.ItalianNrcs,
            "6" or "E" => Charsets.NorwegianDanishNrcs,
            "`" => Charsets.NorwegianDanishNrcsIso,
            "Z" => Charsets.SpanishNrcs,
            "7" or "H" => Charsets.SwedishNrcs,
            "=" => Charsets.SwissNrcs,
            "&4" => Charsets.DecCyrillic,
            "&5" => Charsets.RussianNrcs,
            "\"?" => Charsets.DecGreek,
            "\">" => Charsets.GreekNrcs,
            "\"4" => Charsets.DecHebrew,
            "%=" => Charsets.HebrewNrcs,
            "%0" => Charsets.DecTurkish,
            "%2" => Charsets.TurkishNrcs,
            "%5" => Charsets.DecSupplemental,
            "%6" => Charsets.PortugueseNrcs,
            _ => string.Empty,
        };
    }

    private string LookupTranslationTable96(VTID charset)
    {
        if (charset == _drcsId && _drcsId.Value != 0)
        {
            return _drcsTranslationTable;
        }

        return charset.ToString() switch
        {
            "A" => Charsets.Latin1,
            "<" => _upssTranslationTable,
            "B" => Charsets.Latin2,
            "L" => Charsets.LatinCyrillic,
            "F" => Charsets.LatinGreek,
            "H" => Charsets.LatinHebrew,
            "M" => Charsets.Latin5,
            _ => string.Empty,
        };
    }

    private void SetTranslationTable(int gsetNumber, string translationTable)
    {
        _gsetTranslationTables[gsetNumber] = translationTable;
        LockingShift(_glSetNumber);
        LockingShiftRight(_grSetNumber);
    }

    private void ReplaceDrcsTable(string oldTable, string newTable)
    {
        if (ReferenceEquals(newTable, oldTable))
        {
            return;
        }

        for (int gsetNumber = 0; gsetNumber < 4; gsetNumber++)
        {
            string gsetTable = _gsetTranslationTables[gsetNumber];

            // A previous soft font designation reverts to the default set first.
            if (ReferenceEquals(gsetTable, Charsets.Drcs94) || ReferenceEquals(gsetTable, Charsets.Drcs96))
            {
                gsetTable = gsetNumber < 2 ? Charsets.Ascii : Charsets.Latin1;
            }

            if (ReferenceEquals(gsetTable, oldTable))
            {
                gsetTable = newTable;
            }

            _gsetTranslationTables[gsetNumber] = gsetTable;
        }

        LockingShift(_glSetNumber);
        LockingShiftRight(_grSetNumber);
    }
}
