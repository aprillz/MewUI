using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aprillz.MewUI.MewDock.Model.Json;

/// <summary>Serialized rectangle (port of IJsonRect).</summary>
public sealed class JsonRect
{
    /// <summary>The left edge.</summary>
    public double X { get; set; }
    /// <summary>The top edge.</summary>
    public double Y { get; set; }
    /// <summary>The width.</summary>
    public double Width { get; set; }
    /// <summary>The height.</summary>
    public double Height { get; set; }
}

/// <summary>
/// A child of a row: either a nested row or a tabset (port of the <c>IJsonRowNode | IJsonTabSetNode</c> union).
/// The <c>type</c> discriminator drives System.Text.Json polymorphic (de)serialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JsonRowNode), "row")]
[JsonDerivedType(typeof(JsonTabSetNode), "tabset")]
public abstract class JsonRowChild
{
    /// <summary>The node id; null lets the model assign one.</summary>
    public string? Id { get; set; }
    /// <summary>The share of the row's space, relative to the siblings' weights.</summary>
    public double? Weight { get; set; }
}

/// <summary>A row of tabsets and child rows (port of IJsonRowNode).</summary>
public sealed class JsonRowNode : JsonRowChild
{
    /// <summary>The child rows and tabsets, in order.</summary>
    public List<JsonRowChild>? Children { get; set; }
}

/// <summary>A tabset and its tabs (port of IJsonTabSetNode).</summary>
public sealed class JsonTabSetNode : JsonRowChild
{
    /// <summary>The tabset name, if any.</summary>
    public string? Name { get; set; }
    /// <summary>The index of the selected tab.</summary>
    public int? Selected { get; set; }
    /// <summary>Whether this is the active tabset.</summary>
    public bool? Active { get; set; }
    /// <summary>Whether this tabset is maximized.</summary>
    public bool? Maximized { get; set; }
    /// <summary>The tabs, in order.</summary>
    public List<JsonTabNode>? Children { get; set; }
}

/// <summary>A tab (port of IJsonTabNode). Per-node enable overrides are null unless the JSON sets them, in which
/// case they win over the model globals.</summary>
public sealed class JsonTabNode
{
    /// <summary>The tab id; null lets the model assign one.</summary>
    public string? Id { get; set; }
    /// <summary>The tab title.</summary>
    public string? Name { get; set; }
    /// <summary>The component key the host uses to restore the content.</summary>
    public string? Component { get; set; }
    /// <summary>Whether the tab is a document tab; null means it is.</summary>
    public bool? IsDocument { get; set; }
    /// <summary>The id of the sub-layout the tab hosts, if any.</summary>
    public string? SubLayoutId { get; set; }
    /// <summary>The icon source, if any.</summary>
    public string? Icon { get; set; }
    /// <summary>The tooltip text, if any.</summary>
    public string? HelpText { get; set; }
    /// <summary>The shorter name shown where the title does not fit, if any.</summary>
    public string? AltName { get; set; }
    /// <summary>Whether the tab can be closed; null follows the model global.</summary>
    public bool? EnableClose { get; set; }
    /// <summary>Whether the tab can be dragged; null follows the model global.</summary>
    public bool? EnableDrag { get; set; }
    /// <summary>Whether the tab can be renamed; null follows the model global.</summary>
    public bool? EnableRename { get; set; }
    /// <summary>Whether the tab can be popped out; null follows the model global.</summary>
    public bool? EnablePopout { get; set; }
    /// <summary>Host-defined configuration, kept as written.</summary>
    public JsonElement? Config { get; set; }
}

/// <summary>An edge border and its tabs (port of IJsonBorderNode).</summary>
public sealed class JsonBorderNode
{
    /// <summary>The edge: top, bottom, left or right.</summary>
    public string Location { get; set; } = "bottom";
    /// <summary>The index of the revealed tab, or -1 for none.</summary>
    public int? Selected { get; set; }
    /// <summary>Whether the border is shown.</summary>
    public bool? Show { get; set; }
    /// <summary>The size of the revealed tab's area; null uses the global border size.</summary>
    public double? Size { get; set; }
    /// <summary>Whether the border's tabs auto-hide.</summary>
    public bool? EnableAutoHide { get; set; } = true;
    /// <summary>The tabs, in order.</summary>
    public List<JsonTabNode>? Children { get; set; }
}

/// <summary>A sub-layout space for a popout/float/tab, or an Extended tool dock (port of IJsonSubLayout, plus the
/// Dock fields). <see cref="Edge"/>/<see cref="Size"/>/<see cref="DockRank"/> are set for <c>dock</c> layouts only.</summary>
public sealed class JsonSubLayout
{
    /// <summary>The kind of space: window, float, tab or dock.</summary>
    public string? Type { get; set; }
    /// <summary>The rectangle of a window or float layout.</summary>
    public JsonRect? Rect { get; set; }
    /// <summary>The edge of a dock layout.</summary>
    public string? Edge { get; set; }
    /// <summary>The size of a dock layout.</summary>
    public double? Size { get; set; }
    /// <summary>The order of a dock layout among those on the same edge.</summary>
    public double? DockRank { get; set; }
    /// <summary>The root row.</summary>
    public JsonRowNode Layout { get; set; } = new();
}

/// <summary>
/// Global model defaults (subset of IGlobalAttributes); unknown JSON members are ignored on read and the rest
/// keep their model defaults.
/// </summary>
public sealed class JsonGlobal
{
    /// <summary>Whether the root row stacks its children vertically.</summary>
    public bool? RootOrientationVertical { get; set; }
    /// <summary>The splitter thickness in DIPs.</summary>
    public double? SplitterSize { get; set; }
    /// <summary>Whether nodes can be dropped onto the edges of the layout.</summary>
    public bool? EnableEdgeDock { get; set; }
    /// <summary>The default size of a border's revealed area in DIPs.</summary>
    public double? BorderSize { get; set; }
}

/// <summary>The root serialized model (port of IJsonModel).</summary>
public sealed class JsonModel
{
    /// <summary>The global defaults.</summary>
    public JsonGlobal? Global { get; set; }
    /// <summary>The edge borders.</summary>
    public List<JsonBorderNode>? Borders { get; set; }
    /// <summary>The main layout's root row.</summary>
    public JsonRowNode Layout { get; set; } = new();
    /// <summary>The sub-layouts keyed by id.</summary>
    public Dictionary<string, JsonSubLayout>? SubLayouts { get; set; }
}

/// <summary>Source-generated, reflection-free (de)serialization context for the model JSON (AOT-safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonModel))]
internal partial class MewDockJsonContext : JsonSerializerContext;
