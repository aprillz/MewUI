namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// A leaf tab hosting application content via the layout factory (port of FlexLayout model/TabNode.ts).
/// View-only DOM/scroll plumbing from the original is omitted; per-node attribute overrides arrive with JSON
/// (flags resolve to the model globals until then). Sub-layout rules are simplified pending Phase 5.
/// </summary>
internal sealed class TabNode : Node
{
    private string? _name;

    internal TabNode(Model model, bool addToModel = true) : base(model)
    {
        if (addToModel)
        {
            model.AddNode(this);
        }
    }

    public override string Type => "tab";

    public override string? Name => _name;

    public string? Component { get; internal set; }

    /// <summary>Document pane (top tabs, maximize - the default) vs plain pane (edge dock / auto-hide). Owned by
    /// the tab itself so it travels across splits / moves / popouts; serialized only when false.</summary>
    public bool IsDocument { get; internal set; } = true;

    public string? SubLayoutId { get; internal set; }

    public string? Icon => Model.TabIcon;

    public string? HelpText { get; internal set; }

    public string? AltName { get; internal set; }

    /// <summary>Arbitrary per-tab config persisted to JSON (changed via UpdateNodeAttributes).</summary>
    public object? Config { get; internal set; }

    /// <summary>Transient per-tab data that is NOT serialized.</summary>
    public Dictionary<string, object?> Extra { get; } = new();

    // Tab button / content rect set by the view during layout, plus transient render state.
    public Rect TabRect { get; internal set; } = Rect.Empty;

    public bool Visible { get; internal set; }

    public bool Rendered { get; internal set; }

    internal double? ScrollTop { get; set; }

    internal double? ScrollLeft { get; set; }

    internal double BorderWidth { get; set; } = -1;

    internal double BorderHeight { get; set; } = -1;

    // Per-node enable overrides (null = fall back to the model global). Set from JSON / UpdateNodeAttributes.
    internal bool? EnableCloseOverride { get; set; }

    internal bool? EnableDragOverride { get; set; }

    internal bool? EnableRenameOverride { get; set; }

    internal bool? EnablePopoutOverride { get; set; }

    public override bool IsEnableClose => EnableCloseOverride ?? Model.TabEnableClose;

    public override bool IsEnableDrag => EnableDragOverride ?? Model.TabEnableDrag;

    public bool IsEnableRename => EnableRenameOverride ?? Model.TabEnableRename;

    public bool IsEnablePopout => EnablePopoutOverride ?? Model.TabEnablePopout;

    public bool IsEnablePopoutIcon => Model.TabEnablePopoutIcon;

    public bool IsEnableRenderOnDemand => Model.TabEnableRenderOnDemand;

    public CloseType CloseType => Model.TabCloseType;

    public double GetMinWidth() => Model.TabMinWidth;

    public double GetMinHeight() => Model.TabMinHeight;

    public double GetMaxWidth() => Model.TabMaxWidth;

    public double GetMaxHeight() => Model.TabMaxHeight;

    public bool IsPoppedOut() => LayoutId != Model.MainLayoutId;

    public bool IsSelected()
    {
        var selected = Parent switch
        {
            TabSetNode ts => ts.GetSelectedNode(),
            BorderNode bn => bn.GetSelectedNode(),
            _ => null,
        };
        return ReferenceEquals(selected, this);
    }

    // TODO Phase 5: factor in sub-layout root closeable/allowed when SubLayoutId is set.
    public override bool IsCloseable() => IsEnableClose;

    public override bool IsAllowedInWindow() => IsEnablePopout;

    // Empty normalizes to null so "no name" has one representation: display falls back, serialization omits it.
    internal void SetName(string? name) => _name = string.IsNullOrEmpty(name) ? null : name;

    internal void SetBorderWidth(double width) => BorderWidth = width;

    internal void SetBorderHeight(double height) => BorderHeight = height;

    internal void SetVisible(bool visible)
    {
        if (visible != Visible)
        {
            Visible = visible;
            FireEvent(NodeEventType.Visibility, visible);
        }
    }

    internal void SetTabRect(Rect rect)
    {
        if (!rect.Equals(Rect))
        {
            FireEvent(NodeEventType.Resize, rect);
            Rect = rect;
        }
    }

    internal void Delete()
    {
        switch (Parent)
        {
            case TabSetNode tabSet:
                tabSet.Remove(this);
                break;
            case BorderNode border:
                border.Remove(this);
                break;
        }
        if (SubLayoutId is not null)
        {
            Model.Layouts.Remove(SubLayoutId);
        }
        FireEvent(NodeEventType.Close, null);
    }
}
