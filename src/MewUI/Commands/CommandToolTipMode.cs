namespace Aprillz.MewUI;

/// <summary>
/// Selects which parts of a command a control builds its tooltip from when it has no tooltip of its own.
/// The parts a surface already shows are the ones to leave out.
/// </summary>
[Flags]
public enum CommandToolTipMode
{
    /// <summary>Build no tooltip.</summary>
    None = 0,

    /// <summary>The command's presentation text.</summary>
    Text = 1 << 0,

    /// <summary>The gesture the command currently answers to, in brackets after the text.</summary>
    Shortcut = 1 << 1,

    /// <summary>The command's description, on a line of its own.</summary>
    Description = 1 << 2,
}
