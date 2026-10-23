namespace Aprillz.MewUI.Controls;

public sealed partial class TabControl
{
    /// <summary>Selects a tab the way a header click does, without moving focus.</summary>
    internal void SelectFromAutomation(int index) => CommitSelection(index);
}
