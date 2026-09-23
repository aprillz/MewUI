using System.Text.Json;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Extended;

namespace MewUI.MewDock.Test;

/// <summary>Layout builders and visual-tree queries shared by the MewDock tests. Views are built by LoadLayout, so no window is needed.</summary>
internal static class DockTestSupport
{
    public static string Tool(string name, string attributes = "") =>
        attributes.Length == 0
            ? $$"""{ "type": "tab", "name": "{{name}}", "component": "tool", "isDocument": false }"""
            : $$"""{ "type": "tab", "name": "{{name}}", "component": "tool", "isDocument": false, {{attributes}} }""";

    /// <summary>A document area with one document, one docked tool group on the left holding <paramref name="tools"/>, and an empty left border to unpin into.</summary>
    public static string DockedTools(params string[] tools) => $$"""
        {
          "borders": [ { "location": "left", "children": [] } ],
          "layout": { "type": "row", "children": [
            { "type": "tabset", "children": [ { "type": "tab", "name": "Document", "component": "document" } ] }
          ]},
          "subLayouts": {
            "dock-left": {
              "type": "dock", "edge": "left", "size": 200, "dockRank": 0,
              "layout": { "type": "row", "children": [
                { "type": "tabset", "children": [ {{string.Join(", ", tools)}} ] }
              ]}
            }
          }
        }
        """;

    /// <summary>A document area with one document, and <paramref name="tools"/> auto-hidden in the left border.</summary>
    public static string AutoHiddenTools(params string[] tools) => $$"""
        {
          "borders": [ { "location": "left", "children": [ {{string.Join(", ", tools)}} ] } ],
          "layout": { "type": "row", "children": [
            { "type": "tabset", "children": [ { "type": "tab", "name": "Document", "component": "document" } ] }
          ]}
        }
        """;

    public static DockingManager Load(string json)
    {
        var manager = new DockingManager();
        manager.LoadLayout(json);
        return manager;
    }

    public static DockPane Pane(DockingManager manager, string title) =>
        manager.Panes.Concat(manager.DocumentPanes).Single(pane => pane.Title == title);

    /// <summary>The caption of the docked tool group (hosted by the group's tabset view).</summary>
    public static DockCaption ToolCaption(DockingManager manager) =>
        Descendants(manager).OfType<DockCaption>().Single(caption => caption.Parent is FlexTabSetView);

    /// <summary>The caption of the revealed auto-hide panel (hosted by the border bar).</summary>
    public static DockCaption BorderCaption(DockingManager manager) =>
        Descendants(manager).OfType<DockCaption>().Single(caption => caption.Parent is not FlexTabSetView && IsShown(caption, manager));

    /// <summary>Glyphs of the caption buttons the user can see.</summary>
    public static HashSet<GlyphKind> VisibleGlyphs(DockCaption caption) =>
        Descendants(caption).OfType<Button>()
            .Where(button => IsShown(button, caption) && button.Content is GlyphElement)
            .Select(button => ((GlyphElement)button.Content!).Kind)
            .ToHashSet();

    /// <summary>The serialized tab object named <paramref name="name"/> in a saved layout.</summary>
    public static JsonElement SavedTab(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return FindTab(document.RootElement, name)?.Clone() ?? throw new AssertFailedException($"Tab '{name}' is not in the saved layout.");
    }

    private static JsonElement? FindTab(JsonElement node, string name)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("name", out var nodeName) && nodeName.GetString() == name && node.TryGetProperty("component", out _))
            {
                return node;
            }
            foreach (var property in node.EnumerateObject())
            {
                if (FindTab(property.Value, name) is JsonElement found)
                {
                    return found;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                if (FindTab(item, name) is JsonElement found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    private static bool IsShown(Element element, Element root)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (current is UIElement uiElement && !uiElement.IsVisible)
            {
                return false;
            }
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }
        return false;
    }

    public static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        if (root is IVisualTreeHost host)
        {
            var children = new List<Element>();
            host.VisitChildren(child => { children.Add(child); return true; });
            foreach (var child in children)
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
