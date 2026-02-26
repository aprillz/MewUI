namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class ClipStack
{
    private readonly Stack<bool> _stack = new();

    public bool HasClip { get; private set; }

    public void Save()
        => _stack.Push(HasClip);

    public void Restore()
        => HasClip = _stack.Count > 0 && _stack.Pop();

    public void Reset()
        => HasClip = false;

    public void Apply(Rect rect, Action<Rect> setClip, Action<Rect> intersectClip, Action resetClip)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            resetClip();
            HasClip = false;
            return;
        }

        if (HasClip)
        {
            intersectClip(rect);
        }
        else
        {
            setClip(rect);
            HasClip = true;
        }
    }
}
