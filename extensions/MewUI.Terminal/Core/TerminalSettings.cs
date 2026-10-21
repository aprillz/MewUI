// Ported from Windows Terminal src/cascadia/inc/ControlProperties.h and src/inc/DefaultSettings.h (MIT): the settings
// the terminal core reads.

using Aprillz.MewUI.Terminal.Buffer;

namespace Aprillz.MewUI.Terminal.Core;

/// <summary>The settings of a terminal: its size, scrollback, behavior and colors.</summary>
internal sealed class TerminalSettings
{
    public const int DEFAULT_HISTORY_SIZE = 9001;
    public const int DEFAULT_ROWS = 30;
    public const int DEFAULT_COLS = 120;
    public const int DEFAULT_CURSOR_HEIGHT = 25;
    public const string DEFAULT_WORD_DELIMITERS = " ./\\()\"'-:,.;<>~!@#$%^&*|+=[]{}~?│";

    public int HistorySize { get; set; } = DEFAULT_HISTORY_SIZE;

    public int InitialRows { get; set; } = DEFAULT_ROWS;

    public int InitialCols { get; set; } = DEFAULT_COLS;

    public bool SnapOnInput { get; set; } = true;

    public bool AltGrAliasing { get; set; } = true;

    public string AnswerbackMessage { get; set; } = string.Empty;

    public string WordDelimiters { get; set; } = DEFAULT_WORD_DELIMITERS;

    public bool SuppressApplicationTitle { get; set; }

    public bool TrimBlockSelection { get; set; } = true;

    public bool AutoMarkPrompts { get; set; }

    public bool AllowVtClipboardWrite { get; set; } = true;

    public bool AllowVtChecksumReport { get; set; }

    public bool AllowOscNotifications { get; set; }

    public bool DetectUrls { get; set; } = true;

    public uint? TabColor { get; set; }

    public uint? StartingTabColor { get; set; }

    public string? StartingTitle { get; set; }

    public bool IntenseIsBold { get; set; }

    public bool IntenseIsBright { get; set; } = true;

    public TerminalCursorShape CursorShape { get; set; } = TerminalCursorShape.Bar;

    public int CursorHeight { get; set; } = DEFAULT_CURSOR_HEIGHT;

    public uint DefaultForeground { get; set; } = TextColor.Rgb(0xFF, 0xFF, 0xFF);

    public uint DefaultBackground { get; set; } = TextColor.Rgb(0, 0, 0);

    public uint CursorColor { get; set; } = TextColor.Rgb(0xFF, 0xFF, 0xFF);

    public uint SelectionBackground { get; set; } = TextColor.Rgb(0xFF, 0xFF, 0xFF);

    /// <summary>The 16 ANSI colors; the Campbell scheme unless replaced.</summary>
    public uint[] ColorTable { get; set; } = CreateDefaultColorTable();

    private static uint[] CreateDefaultColorTable()
    {
        var table = new uint[16];
        Types.ColorTable.InitializeAnsi(table);
        return table;
    }
}
