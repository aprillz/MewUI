using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

public abstract class TextBase : Control
{
    private ValueBinding<string>? _textBinding;
    private bool _suppressBindingSet;
    private readonly TextDocument _document = new();
    private int _documentVersion;
    private string? _cachedText;
    private int _cachedTextVersion = -1;

    protected int _selectionStart;
    protected int _selectionLength;

    private protected TextDocument Document => _document;

    protected int DocumentVersion => _documentVersion;

    public string Text
    {
        get => GetTextCore();
        set
        {
            var normalized = NormalizeText(value ?? string.Empty);
            var current = GetTextCore();
            if (current == normalized)
                return;

            var old = current;
            SetTextCore(normalized);

            CaretPosition = Math.Min(CaretPosition, GetTextLengthCore());
            _selectionStart = 0;
            _selectionLength = 0;

            OnTextChanged(old, normalized);
            if (TextChanged != null)
                TextChanged(GetTextCore());
        }
    }

    public string Placeholder
    {
        get;
        set { field = value ?? string.Empty; InvalidateVisual(); }
    } = string.Empty;

    public bool IsReadOnly
    {
        get;
        set { field = value; InvalidateVisual(); }
    }

    public bool AcceptTab { get; set; }

    public int CaretPosition
    {
        get;
        set { field = Math.Clamp(value, 0, GetTextLengthCore()); InvalidateVisual(); }
    }

    public Action<string>? TextChanged { get; set; }

    public override bool Focusable => true;

    protected virtual string NormalizeText(string text) => text ?? string.Empty;

    protected virtual string GetTextCore()
    {
        if (_cachedTextVersion == _documentVersion && _cachedText != null)
            return _cachedText;

        _cachedText = _document.GetText();
        _cachedTextVersion = _documentVersion;
        return _cachedText;
    }

    protected virtual void SetTextCore(string normalizedText)
    {
        BumpDocumentVersion();
        _document.SetText(normalizedText ?? string.Empty);
    }

    protected virtual int GetTextLengthCore() => _document.Length;

    protected virtual char GetTextCharCore(int index) => _document[index];

    protected virtual string GetTextSubstringCore(int start, int length) => _document.GetText(start, length);

    protected virtual void OnTextChanged(string oldText, string newText)
    {
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        bool ctrl = e.ControlKey;
        if (!ctrl)
            return;

        switch (e.Key)
        {
            case Key.A:
                SelectAllCore();
                e.Handled = true;
                return;

            case Key.C:
                CopyToClipboardCore();
                e.Handled = true;
                return;

            case Key.X:
                if (!IsReadOnly)
                    CutToClipboardCore();
                e.Handled = true;
                return;

            case Key.V:
                if (!IsReadOnly)
                    PasteFromClipboardCore();
                e.Handled = true;
                return;
        }
    }

    public void SetTextBinding(
        Func<string> get,
        Action<string> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        _textBinding?.Dispose();
        _textBinding = new ValueBinding<string>(
            get,
            set,
            subscribe,
            unsubscribe,
            onSourceChanged: () =>
            {
                if (IsFocused)
                    return;

                var value = NormalizeText(get() ?? string.Empty);
                if (GetTextCore() == value)
                    return;

                _suppressBindingSet = true;
                try { Text = value; }
                finally { _suppressBindingSet = false; }
            });

        var existing = TextChanged;
        TextChanged = text =>
        {
            existing?.Invoke(text);

            if (_suppressBindingSet)
                return;

            _textBinding?.Set(text);
        };

        _suppressBindingSet = true;
        try { Text = NormalizeText(get() ?? string.Empty); }
        finally { _suppressBindingSet = false; }
    }

    protected override void OnDispose()
    {
        _document.Dispose();
        _textBinding?.Dispose();
        _textBinding = null;
    }

    protected void NotifyTextChanged()
    {
        if (TextChanged != null)
            TextChanged(GetTextCore());
    }

    protected void BumpDocumentVersion()
    {
        _documentVersion++;
        _cachedTextVersion = -1;
        _cachedText = null;
    }

    protected void InsertIntoDocument(int index, ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
            return;
        BumpDocumentVersion();
        _document.Insert(index, text);
    }

    protected void RemoveFromDocument(int index, int length)
    {
        if (length <= 0)
            return;
        BumpDocumentVersion();
        _document.Remove(index, length);
    }

    protected virtual void SelectAllCore()
    {
        _selectionStart = 0;
        _selectionLength = GetTextLengthCore();
        CaretPosition = GetTextLengthCore();
        InvalidateVisual();
    }

    protected virtual void CopyToClipboardCore()
    {
        if (_selectionLength == 0)
            return;

        int start = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int end = Math.Max(_selectionStart, _selectionStart + _selectionLength);
        string selected = GetTextSubstringCore(start, end - start);
        TryClipboardSetText(selected);
    }

    protected virtual void CutToClipboardCore()
    {
        if (_selectionLength == 0)
            return;

        CopyToClipboardCore();
        DeleteSelectionForEdit();
    }

    protected virtual void PasteFromClipboardCore()
    {
        if (!TryClipboardGetText(out var text) || string.IsNullOrEmpty(text))
            return;

        InsertTextAtCaretForEdit(text);
    }

    protected virtual bool DeleteSelectionForEdit()
    {
        if (_selectionLength == 0)
            return false;

        int start = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int length = Math.Abs(_selectionLength);

        RemoveFromDocument(start, length);
        CaretPosition = start;
        _selectionStart = start;
        _selectionLength = 0;
        NotifyTextChanged();
        return true;
    }

    protected virtual void InsertTextAtCaretForEdit(string text)
    {
        text = NormalizeText(text ?? string.Empty);
        if (text.Length == 0)
            return;

        DeleteSelectionForEdit();

        InsertIntoDocument(CaretPosition, text.AsSpan());
        CaretPosition += text.Length;
        _selectionStart = CaretPosition;
        _selectionLength = 0;
        NotifyTextChanged();
    }

    protected bool TryClipboardSetText(string text)
    {
        if (!Application.IsRunning)
            return false;

        var clipboard = Application.Current.PlatformHost.Clipboard;
        return clipboard.TrySetText(text ?? string.Empty);
    }

    protected bool TryClipboardGetText(out string text)
    {
        text = string.Empty;
        if (!Application.IsRunning)
            return false;

        var clipboard = Application.Current.PlatformHost.Clipboard;
        return clipboard.TryGetText(out text);
    }
}
