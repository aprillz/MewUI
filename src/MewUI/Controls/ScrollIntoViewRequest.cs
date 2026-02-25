namespace Aprillz.MewUI.Controls;

internal enum ScrollIntoViewRequestKind
{
    None = 0,
    Selected = 1,
    Index = 2,
}

internal struct ScrollIntoViewRequest
{
    public ScrollIntoViewRequestKind Kind;
    public int Index;

    public static ScrollIntoViewRequest None => default;

    public static ScrollIntoViewRequest Selected() => new() { Kind = ScrollIntoViewRequestKind.Selected };

    public static ScrollIntoViewRequest IndexRequest(int index) => new() { Kind = ScrollIntoViewRequestKind.Index, Index = index };

    public bool IsNone => Kind == ScrollIntoViewRequestKind.None;

    public void Clear() => this = default;
}
