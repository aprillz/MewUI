namespace Aprillz.MewUI.Controls;

public sealed class GridViewColumn<TItem>
{
    public string Header { get; set; } = string.Empty;

    public double Width { get; set; }

    public IDataTemplate<TItem>? CellTemplate { get; set; }
}
