using System.Collections.Concurrent;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Decides whether a visual's composition can be trusted to describe how it draws. One method draws the
/// children and parts and the other says where they sit; a type that overrides one without the other has
/// an order the scene cannot know, so it is recorded as one compatibility subtree instead. When the
/// composition was written at or below the class that draws the children, the pair holds: the same class
/// wrote both, or a class further down described what its base draws for it. That also covers every
/// type that inherits the pair without drawing children of its own.
/// </summary>
internal static class DeclaredComposition
{
    private static readonly ConcurrentDictionary<Type, bool> _supported = new();

    /// <summary>The answer already worked out for this type, so asking again costs no delegates.</summary>
    internal static bool TryGetKnown(Type type, out bool supported) => _supported.TryGetValue(type, out supported);

    /// <summary>
    /// Answers for the type of <paramref name="visual"/> from the two delegates it hands over. A delegate
    /// to a virtual method binds to the override that would run, and says which class declared it,
    /// without looking methods up by name, which trimming and ahead-of-time compilation cannot follow.
    /// </summary>
    internal static bool IsSupported(
        UIElement visual,
        Action<IGraphicsContext> renderSubtree,
        Action<CompositionPlanBuilder> writeComposition)
    {
        var type = visual.GetType();
        if (_supported.TryGetValue(type, out bool supported))
        {
            return supported;
        }

        var subtreeOwner = renderSubtree.Method.DeclaringType;
        var compositionOwner = writeComposition.Method.DeclaringType;
        supported = subtreeOwner != null && compositionOwner != null && subtreeOwner.IsAssignableFrom(compositionOwner);
        _supported[type] = supported;
        return supported;
    }
}
