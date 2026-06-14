using System.Text.Json;

using Aprillz.MewUI.MewDock.Model.Json;

namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// JSON round-trip for <see cref="Model"/> via the source-generated DTOs (port of Model.fromJson / toJson and
/// the per-node fromJson/toJson). Per-node attribute overrides beyond the core fields arrive in a later step.
/// </summary>
internal partial class Model
{
    public static Model FromJson(JsonModel json)
    {
        var model = new Model();
        model.LoadFrom(json);
        return model;
    }

    public static Model FromJson(string json) =>
        FromJson(JsonSerializer.Deserialize(json, MewDockJsonContext.Default.JsonModel)!);

    /// <summary>Populates this (empty) model from the DTO. Subclasses build their own type then call this.</summary>
    protected void LoadFrom(JsonModel json)
    {
        if (json.Global is not null)
        {
            ApplyGlobalAttributes(json.Global);
        }

        if (json.Borders is not null)
        {
            foreach (var borderJson in json.Borders)
            {
                _borders.Add(BuildBorderNode(borderJson));
            }
        }

        _mainLayout.SetRootRow(BuildRowNode(json.Layout, _mainLayout));

        if (json.SubLayouts is not null)
        {
            foreach (var (id, subJson) in json.SubLayouts)
            {
                BuildSubLayout(id, subJson);
            }
        }

        Tidy();
        UpdateIdMap();
    }

    public JsonModel ToJson()
    {
        VisitNodes((node, level) => node.FireEvent(NodeEventType.Save, null));

        return new JsonModel
        {
            Global = GlobalToJson(),
            Borders = _borders.Borders.Count > 0 ? _borders.Borders.Select(BorderToJson).ToList() : null,
            Layout = RowToJson(_mainLayout.RootRow!),
            SubLayouts = SubLayoutsToJson(),
        };
    }

    private Dictionary<string, JsonSubLayout>? SubLayoutsToJson()
    {
        Dictionary<string, JsonSubLayout>? result = null;
        foreach (var (id, layout) in _layouts)
        {
            if (layout.IsMainLayout || layout.RootRow is not RowNode rootRow)
            {
                continue;
            }
            result ??= new Dictionary<string, JsonSubLayout>();
            result[id] = SubLayoutToJson(layout, rootRow);
        }
        return result;
    }

    // Serialization seam: the feature layer overrides this pair to round-trip its own Layout subclasses (the
    // Extended dock sub-layouts); the base handles the faithful window/float/tab spaces only.
    private protected virtual JsonSubLayout SubLayoutToJson(Layout layout, RowNode rootRow) => new()
    {
        Type = LayoutTypeName(layout.Type),
        Rect = layout.Type is LayoutType.Float or LayoutType.Window
            ? new JsonRect { X = layout.Rect.X, Y = layout.Rect.Y, Width = layout.Rect.Width, Height = layout.Rect.Height }
            : null,
        Layout = RowToJson(rootRow),
    };

    private static string LayoutTypeName(LayoutType type) => type switch
    {
        LayoutType.Float => "float",
        LayoutType.Tab => "tab",
        _ => "window",
    };

    public string ToJsonString() =>
        JsonSerializer.Serialize(ToJson(), MewDockJsonContext.Default.JsonModel);

    private protected RowNode BuildRowNode(JsonRowNode json, Layout layout)
    {
        var row = new RowNode(this);
        if (json.Id is not null)
        {
            row.SetId(json.Id);
        }
        if (json.Weight is double weight)
        {
            row.Weight = weight;
        }
        if (json.Children is not null)
        {
            foreach (var child in json.Children)
            {
                switch (child)
                {
                    case JsonTabSetNode tabSetJson:
                        row.AddChild(BuildTabSetNode(tabSetJson, layout));
                        break;
                    case JsonRowNode rowJson:
                        row.AddChild(BuildRowNode(rowJson, layout));
                        break;
                }
            }
        }
        return row;
    }

    private TabSetNode BuildTabSetNode(JsonTabSetNode json, Layout layout)
    {
        var tabSet = new TabSetNode(this);
        if (json.Id is not null)
        {
            tabSet.SetId(json.Id);
        }
        if (json.Weight is double weight)
        {
            tabSet.Weight = weight;
        }
        if (json.Selected is int selected)
        {
            tabSet.SetSelected(selected);
        }
        if (json.Children is not null)
        {
            foreach (var tabJson in json.Children)
            {
                tabSet.AddChild(BuildTabNode(tabJson, addToModel: true));
            }
        }
        if (json.Active == true)
        {
            layout.ActiveTabSet = tabSet;
        }
        if (json.Maximized == true)
        {
            layout.MaximizedTabSet = tabSet;
        }
        return tabSet;
    }

    internal TabNode BuildTabNode(JsonTabNode json, bool addToModel)
    {
        var tab = new TabNode(this, addToModel);
        if (json.Id is not null)
        {
            tab.SetId(json.Id);
        }
        if (json.Name is not null)
        {
            tab.SetName(json.Name);
        }
        tab.Component = json.Component;
        tab.IsDocument = json.IsDocument ?? true;
        tab.SubLayoutId = json.SubLayoutId;
        tab.HelpText = json.HelpText;
        tab.AltName = json.AltName;
        tab.EnableCloseOverride = json.EnableClose;
        tab.EnableDragOverride = json.EnableDrag;
        tab.EnableRenameOverride = json.EnableRename;
        tab.EnablePopoutOverride = json.EnablePopout;
        if (json.Config is JsonElement config)
        {
            tab.Config = config;
        }
        return tab;
    }

    private BorderNode BuildBorderNode(JsonBorderNode json)
    {
        var border = new BorderNode(this, DockLocationExtensions.GetByName(json.Location));
        if (json.Selected is int selected)
        {
            border.SetSelected(selected);
        }
        if (json.Show is bool show)
        {
            border.IsShowing = show;
        }
        border.Size = json.Size;
        border.EnableAutoHideOverride = json.EnableAutoHide;
        if (json.Children is not null)
        {
            foreach (var tabJson in json.Children)
            {
                border.AddChild(BuildTabNode(tabJson, addToModel: true));
            }
        }
        return border;
    }

    private protected virtual void BuildSubLayout(string id, JsonSubLayout json)
    {
        var rect = json.Rect is JsonRect r ? new Rect(r.X, r.Y, r.Width, r.Height) : Rect.Empty;
        var layout = new Layout(id, ParseLayoutType(json.Type), rect);
        layout.SetRootRow(BuildRowNode(json.Layout, layout));
        _layouts[id] = layout;
    }

    private static LayoutType ParseLayoutType(string? name) => name switch
    {
        "float" => LayoutType.Float,
        "tab" => LayoutType.Tab,
        _ => LayoutType.Window,
    };

    private protected JsonRowNode RowToJson(RowNode row) => new()
    {
        Id = row.IdOrNull,
        Weight = row.Weight,
        Children = row.Children.Select(RowChildToJson).ToList(),
    };

    private JsonRowChild RowChildToJson(Node node) => node switch
    {
        RowNode row => RowToJson(row),
        TabSetNode tabSet => TabSetToJson(tabSet),
        _ => throw new InvalidOperationException($"Unexpected row child '{node.Type}'."),
    };

    private JsonTabSetNode TabSetToJson(TabSetNode tabSet) => new()
    {
        Id = tabSet.IdOrNull,
        Weight = tabSet.Weight,
        Selected = tabSet.Selected,
        Active = tabSet.IsActive ? true : null,
        Maximized = tabSet.IsMaximized ? true : null,
        Children = tabSet.Children.Select(child => TabToJson((TabNode)child)).ToList(),
    };

    private JsonTabNode TabToJson(TabNode tab) => new()
    {
        Id = tab.IdOrNull,
        Name = tab.Name,
        Component = tab.Component,
        IsDocument = tab.IsDocument ? null : false, // document is the unmarked default; only panes are marked
        SubLayoutId = tab.SubLayoutId,
        HelpText = tab.HelpText,
        AltName = tab.AltName,
        EnableClose = tab.EnableCloseOverride,
        EnableDrag = tab.EnableDragOverride,
        EnableRename = tab.EnableRenameOverride,
        EnablePopout = tab.EnablePopoutOverride,
        Config = tab.Config as JsonElement?,
    };

    private JsonBorderNode BorderToJson(BorderNode border) => new()
    {
        Location = border.Location.GetName(),
        Selected = border.Selected,
        Show = border.IsShowing ? null : false,
        Size = border.Size,
        EnableAutoHide = border.EnableAutoHideOverride,
        Children = border.Children.Select(child => TabToJson((TabNode)child)).ToList(),
    };

    private JsonGlobal? GlobalToJson()
    {
        var global = new JsonGlobal();
        bool any = false;
        if (IsRootOrientationVertical)
        {
            global.RootOrientationVertical = true;
            any = true;
        }
        if (SplitterSize != 8)
        {
            global.SplitterSize = SplitterSize;
            any = true;
        }
        if (!EnableEdgeDock)
        {
            global.EnableEdgeDock = false;
            any = true;
        }
        if (BorderSize != 200)
        {
            global.BorderSize = BorderSize;
            any = true;
        }
        return any ? global : null;
    }
}
