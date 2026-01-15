namespace Aprillz.MewUI.Controls;

public sealed class TabItem
{
    public Element? Header { get; set; }
    public Element? Content { get; set; }

    public bool IsEnabled { get; set; } = true;
}
