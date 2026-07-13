namespace Aprillz.MewUI.Controls;

/// <summary>
/// The mutually exclusive surface role of a top-level window, resolved from the role flags.
/// Dialog (a modal show mode) and alert (a panel-animation hint) overlap the other roles, so they
/// remain separate cumulative traits rather than kinds.
/// </summary>
internal enum WindowKind
{
    Normal,
    Tool,
    Popup,
    Overlay,
}
