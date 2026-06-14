namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// Common base of the two row-child node types <see cref="RowNode"/> and <see cref="TabSetNode"/> (the
/// original's <c>RowNode | TabSetNode</c> union). Carries the weight and the computed min/max constraints used
/// by the splitter and layout math. Not a separate type in FlexLayout; an idiomatic C# way to express the union
/// without putting weight/sizing on the shared <see cref="Node"/> base.
/// </summary>
internal abstract class SizedNode : Node
{
    private protected SizedNode(Model model) : base(model)
    {
    }

    /// <summary>Relative size weight within the parent row (default 100, normalized across siblings).</summary>
    public double Weight { get; internal set; } = 100;

    public double MinWidth { get; internal set; } = ModelDefaults.Min;

    public double MinHeight { get; internal set; } = ModelDefaults.Min;

    public double MaxWidth { get; internal set; } = ModelDefaults.Max;

    public double MaxHeight { get; internal set; } = ModelDefaults.Max;

    internal double GetMinSize(Orientation orientation) =>
        orientation == Orientation.Horizontal ? MinWidth : MinHeight;

    internal double GetMaxSize(Orientation orientation) =>
        orientation == Orientation.Horizontal ? MaxWidth : MaxHeight;

    /// <summary>Recomputes this node's min/max constraints from its children (port of calcMinMaxSize).</summary>
    internal abstract void CalcMinMaxSize();
}
