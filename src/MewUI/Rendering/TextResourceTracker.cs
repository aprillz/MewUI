namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Tracks <see cref="TextFormat"/> and <see cref="TextLayout"/> instances
/// created by a backend. Cleans up native resources for detached items.
/// Shared across frame-scoped contexts via injection from the factory/resource owner.
/// </summary>
public sealed class TextResourceTracker
{
    private readonly LinkedList<TextFormat> _formats = new();
    private readonly LinkedList<TextLayout> _layouts = new();

    public void TrackFormat(TextFormat format) => _formats.AddFirst(format);
    public void TrackLayout(TextLayout layout) => _layouts.AddFirst(layout);

    /// <summary>
    /// Override point for native release. Set by the backend.
    /// </summary>
    public Action<TextFormat>? ReleaseNativeFormat { get; set; }
    public Action<TextLayout>? ReleaseNativeLayout { get; set; }

    /// <summary>
    /// Releases native resources for all items where <see cref="TextFormat.IsDetached"/>
    /// or <see cref="TextLayout.IsDetached"/> is true.
    /// </summary>
    public void CleanupDetached()
    {
        var fNode = _formats.First;
        while (fNode != null)
        {
            var next = fNode.Next;
            if (fNode.Value.IsDetached)
            {
                ReleaseNativeFormat?.Invoke(fNode.Value);
                fNode.Value.NativeHandle = 0;
                _formats.Remove(fNode);
            }
            fNode = next;
        }

        var lNode = _layouts.First;
        while (lNode != null)
        {
            var next = lNode.Next;
            if (lNode.Value.IsDetached)
            {
                ReleaseNativeLayout?.Invoke(lNode.Value);
                lNode.Value.NativeHandle = 0;
                _layouts.Remove(lNode);
            }
            lNode = next;
        }
    }

    /// <summary>Releases all tracked resources.</summary>
    public void ReleaseAll()
    {
        foreach (var f in _formats)
        {
            ReleaseNativeFormat?.Invoke(f);
            f.NativeHandle = 0;
        }
        _formats.Clear();

        foreach (var l in _layouts)
        {
            ReleaseNativeLayout?.Invoke(l);
            l.NativeHandle = 0;
        }
        _layouts.Clear();
    }
}
