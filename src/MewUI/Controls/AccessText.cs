namespace Aprillz.MewUI.Controls;

/// <summary>
/// A TextBlock that parses "_" access key markers.
/// The displayed text has markers removed ("_File" → "File").
/// Draws an underline under the access key character when ShowAccessKeys is active.
/// Automatically registers/unregisters with the Window's AccessKeyManager.
/// Activation is delegated to the owning control via <see cref="UIElement.OnAccessKey"/>.
/// </summary>
internal sealed class AccessText : TextBlock
{
    private string _rawText = string.Empty;
    private bool _updatingText;
    private Window? _registeredWindow;

    /// <summary>
    /// Gets the parsed access key character, or default if none.
    /// </summary>
    public char AccessKey { get; private set; }

    /// <summary>
    /// Gets the index in the display text where the underline should be drawn (-1 if none).
    /// </summary>
    public int UnderlineIndex { get; private set; } = -1;

    /// <summary>
    /// Sets the raw text with "_" markers. The displayed text will have markers removed.
    /// </summary>
    public void SetRawText(string rawText)
    {
        rawText ??= string.Empty;
        if (_rawText == rawText) return;

        UnregisterAccessKey();
        _rawText = rawText;

        if (AccessKeyHelper.TryParse(rawText, out var key, out var display))
        {
            AccessKey = key;
            UnderlineIndex = AccessKeyHelper.GetUnderlineIndex(rawText);
        }
        else
        {
            AccessKey = default;
            UnderlineIndex = -1;
        }

        _updatingText = true;
        Text = display;
        _updatingText = false;

        RegisterAccessKey();
    }

    protected override void OnTextChanged()
    {
        if (!_updatingText)
        {
            UnregisterAccessKey();
            _rawText = Text;
            AccessKey = default;
            UnderlineIndex = -1;
        }

        base.OnTextChanged();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        UnregisterAccessKey();
        RegisterAccessKey();
    }

    private void RegisterAccessKey()
    {
        if (AccessKey == default)
            return;

        var root = FindVisualRoot();
        if (root is not Window window)
            return;

        window.AccessKeyManager.Register(AccessKey, this, OnAccessKey);
        _registeredWindow = window;
    }

    private void UnregisterAccessKey()
    {
        if (_registeredWindow == null) return;
        _registeredWindow.AccessKeyManager.Unregister(this);
        _registeredWindow = null;
    }

    protected override void OnRender(Rendering.IGraphicsContext context)
    {
        base.OnRender(context);

        if (UnderlineIndex < 0 || string.IsNullOrEmpty(Text))
            return;

        if (!GetValue(Window.ShowAccessKeysProperty))
            return;

        var text = Text;
        if (UnderlineIndex >= text.Length)
            return;

        AccessKeyRenderer.DrawUnderline(
            context, text, UnderlineIndex, Bounds,
            EnsureFont(GetGraphicsFactory()), Foreground,
            TextAlignment, VerticalTextAlignment,
            GetDpi() / 96.0);
    }
}
