namespace Aprillz.MewUI.Terminal;

/// <summary>The cursor shape a terminal shows until an application chooses one.</summary>
public enum TerminalCursorShape
{
    /// <summary>A small box at the bottom of the cell, as the console host draws it.</summary>
    Vintage,

    /// <summary>A vertical bar at the left of the cell.</summary>
    Bar,

    /// <summary>A line under the cell.</summary>
    Underscore,

    /// <summary>Two lines under the cell.</summary>
    DoubleUnderscore,

    /// <summary>The whole cell, with the character in the background color.</summary>
    FilledBox,

    /// <summary>The outline of the cell.</summary>
    EmptyBox,
}
