namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Tracks <see cref="TextLayout"/> instances with native handles.
/// Releases native resources when layouts are no longer referenced.
/// </summary>
public sealed class TextResourceTracker
{
    private sealed class Entry(WeakReference<TextLayout> weakRef, nint handle)
    {
        public readonly WeakReference<TextLayout> WeakRef = weakRef;
        public readonly nint Handle = handle;
    }

    private readonly LinkedList<Entry> _layouts = new();

    public Action<nint>? ReleaseNativeHandle { get; set; }

    public void TrackLayout(TextLayout layout)
    {
        if (layout.BackendHandle != 0)
        {
            _layouts.AddFirst(new Entry(new WeakReference<TextLayout>(layout), layout.BackendHandle));
        }
    }

    public void Cleanup()
    {
        var node = _layouts.First;
        while (node != null)
        {
            var next = node.Next;
            if (!node.Value.WeakRef.TryGetTarget(out _))
            {
                if (node.Value.Handle != 0)
                {
                    ReleaseNativeHandle?.Invoke(node.Value.Handle);
                }

                _layouts.Remove(node);
            }
            node = next;
        }
    }

    public void ReleaseAll()
    {
        foreach (var entry in _layouts)
        {
            if (entry.Handle != 0)
            {
                ReleaseNativeHandle?.Invoke(entry.Handle);
            }
        }
        _layouts.Clear();
    }
}
