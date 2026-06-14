using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aprillz.MewUI.MewDock.Model.Json;

/// <summary>Serialized rectangle (port of IJsonRect).</summary>
public sealed class JsonRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
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
    public string? Id { get; set; }
    public double? Weight { get; set; }
}

/// <summary>A row of tabsets and child rows (port of IJsonRowNode).</summary>
public sealed class JsonRowNode : JsonRowChild
{
    public List<JsonRowChild>? Children { get; set; }
}

/// <summary>A tabset and its tabs (port of IJsonTabSetNode).</summary>
public sealed class JsonTabSetNode : JsonRowChild
{
    public string? Name { get; set; }
    public int? Selected { get; set; }
    public bool? Active { get; set; }
    public bool? Maximized { get; set; }
    public List<JsonTabNode>? Children { get; set; }
}

/// <summary>A tab (port of IJsonTabNode). Per-node enable overrides are null unless the JSON sets them, in which
/// case they win over the model globals.</summary>
public sealed class JsonTabNode
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Component { get; set; }
    public bool? IsDocument { get; set; }
    public string? SubLayoutId { get; set; }
    public string? Icon { get; set; }
    public string? HelpText { get; set; }
    public string? AltName { get; set; }
    public bool? EnableClose { get; set; }
    public bool? EnableDrag { get; set; }
    public bool? EnableRename { get; set; }
    public bool? EnablePopout { get; set; }
    public JsonElement? Config { get; set; }
}

/// <summary>An edge border and its tabs (port of IJsonBorderNode).</summary>
public sealed class JsonBorderNode
{
    public string Location { get; set; } = "bottom";
    public int? Selected { get; set; }
    public bool? Show { get; set; }
    public double? Size { get; set; }
    public bool? EnableAutoHide { get; set; } = true;
    public List<JsonTabNode>? Children { get; set; }
}

/// <summary>A sub-layout space for a popout/float/tab, or an Extended tool dock (port of IJsonSubLayout, plus the
/// Dock fields). <see cref="Edge"/>/<see cref="Size"/>/<see cref="DockRank"/> are set for <c>dock</c> layouts only.</summary>
public sealed class JsonSubLayout
{
    public string? Type { get; set; }
    public JsonRect? Rect { get; set; }
    public string? Edge { get; set; }
    public double? Size { get; set; }
    public double? DockRank { get; set; }
    public JsonRowNode Layout { get; set; } = new();
}

/// <summary>
/// Global model defaults (subset of IGlobalAttributes; unknown JSON members are ignored on read and the rest
/// keep their model defaults). Expanded as more attributes are wired in later steps.
/// </summary>
public sealed class JsonGlobal
{
    public bool? RootOrientationVertical { get; set; }
    public double? SplitterSize { get; set; }
    public bool? EnableEdgeDock { get; set; }
    public double? BorderSize { get; set; }
}

/// <summary>The root serialized model (port of IJsonModel).</summary>
public sealed class JsonModel
{
    public JsonGlobal? Global { get; set; }
    public List<JsonBorderNode>? Borders { get; set; }
    public JsonRowNode Layout { get; set; } = new();
    public Dictionary<string, JsonSubLayout>? SubLayouts { get; set; }
}

/// <summary>Source-generated, reflection-free (de)serialization context for the model JSON (AOT-safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonModel))]
internal partial class MewDockJsonContext : JsonSerializerContext;
