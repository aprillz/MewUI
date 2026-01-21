using System.Runtime.CompilerServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a top-level window.
/// </summary>
public class Window : ContentControl
    , ILayoutRoundingHost
{
    private readonly DispatcherMergeKey _layoutMergeKey = new(UiDispatcherPriority.Layout);
    private readonly DispatcherMergeKey _renderMergeKey = new(UiDispatcherPriority.Render);

    private Theme _theme = Theme.Current;
    private IWindowBackend? _backend;
    private Size _clientSizeDip = Size.Empty;
    private Size _lastLayoutClientSizeDip = Size.Empty;
    private Thickness _lastLayoutPadding = Thickness.Zero;
    private Element? _lastLayoutContent;
    private readonly List<PopupEntry> _popups = new();
    private readonly RadioGroupManager _radioGroups = new();
    private bool _loadedRaised;
    private bool _firstFrameRenderedRaised;
    private bool _firstFrameRenderedPending;
    private bool _subscribedToDispatcherChanged;

    public Window()
    {
        Padding = new Thickness(16);
    }
     
    private sealed class PopupEntry
    {
        public required UIElement Element { get; init; }

        public required UIElement Owner { get; set; }

        public Rect Bounds { get; set; }
    }

    private sealed class RadioGroupManager
    {
        private readonly Dictionary<string, WeakReference<RadioButton>> _namedSelected = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<Element, WeakReference<RadioButton>> _unnamedSelected = new();

        public void Checked(RadioButton source, string? groupName, Element? parentScope)
        {
            if (groupName != null)
            {
                _namedSelected.TryGetValue(groupName, out var existingRef);
                var existing = TryGet(existingRef);

                _namedSelected[groupName] = new WeakReference<RadioButton>(source);

                if (existing != null && existing != source && existing.IsChecked)
                {
                    existing.IsChecked = false;
                }

                return;
            }

            if (parentScope == null)
            {
                return;
            }

            _unnamedSelected.TryGetValue(parentScope, out var existingScopeRef);
            var existingScope = TryGet(existingScopeRef);

            _unnamedSelected.Remove(parentScope);
            _unnamedSelected.Add(parentScope, new WeakReference<RadioButton>(source));

            if (existingScope != null && existingScope != source && existingScope.IsChecked)
            {
                existingScope.IsChecked = false;
            }
        }

        public void Unchecked(RadioButton source, string? groupName, Element? parentScope)
        {
            if (groupName != null)
            {
                if (_namedSelected.TryGetValue(groupName, out var existingRef) &&
                    TryGet(existingRef) == source)
                {
                    _namedSelected.Remove(groupName);
                }

                return;
            }

            if (parentScope == null)
            {
                return;
            }

            if (_unnamedSelected.TryGetValue(parentScope, out var scopeRef) &&
                TryGet(scopeRef) == source)
            {
                _unnamedSelected.Remove(parentScope);
            }
        }

        private static RadioButton? TryGet(WeakReference<RadioButton>? weak)
        {
            if (weak == null)
            {
                return null;
            }

            return weak.TryGetTarget(out var value) ? value : null;
        }
    }

    internal void RadioGroupChecked(RadioButton source, string? groupName, Element? parentScope)
        => _radioGroups.Checked(source, groupName, parentScope);

    internal void RadioGroupUnchecked(RadioButton source, string? groupName, Element? parentScope)
        => _radioGroups.Unchecked(source, groupName, parentScope);

    public nint Handle => _backend?.Handle ?? 0;

    internal Point ClientToScreen(Point clientPointDip)
    {
        if (_backend == null || Handle == 0)
        {
            throw new InvalidOperationException("Window is not initialized.");
        }

        return _backend.ClientToScreen(clientPointDip);
    }

    internal Point ScreenToClient(Point screenPointPx)
    {
        if (_backend == null || Handle == 0)
        {
            throw new InvalidOperationException("Window is not initialized.");
        }

        return _backend.ScreenToClient(screenPointPx);
    }

    public WindowSize WindowSize
    {
        get;
        set
        {
            field = value;
            if (!double.IsNaN(field.Width))
            {
                Width = field.Width;
            }

            if (!double.IsNaN(field.Height))
            {
                Height = field.Height;
            }

            _backend?.SetResizable(field.IsResizable);
        }
    } = WindowSize.Resizable(800, 600);

    public string Title
    {
        get;
        set
        {
            field = value ?? string.Empty;
            _backend?.SetTitle(field);
        }
    } = "Window";

    public IconSource? Icon
    {
        get;
        set
        {
            field = value;
            _backend?.SetIcon(field);
        }
    }

    public new double Width
    {
        get;
        private set
        {
            field = value;
            _backend?.SetClientSize(Width, Height);
        }
    } = WindowSize.Resizable(800, 600).Width;

    public new double Height
    {
        get;
        private set
        {
            field = value;
            _backend?.SetClientSize(Width, Height);
        }
    } = WindowSize.Resizable(800, 600).Height;

    public bool IsActive { get; private set; }

    public uint Dpi { get; private set; } = 96;

    public double DpiScale => Dpi / 96.0;

    internal Size ClientSizeDip => _clientSizeDip.IsEmpty ? new Size(Width, Height) : _clientSizeDip;

    public bool UseLayoutRounding { get; set; } = true;

    public FocusManager FocusManager => field ??= new FocusManager(this);

    public IGraphicsFactory GraphicsFactory => Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;

    internal IUiDispatcher? ApplicationDispatcher => Application.IsRunning ? Application.Current.Dispatcher : null;

    #region Events

    public event Action? Loaded;

    public event Action? Closed;

    public event Action? Activated;

    public event Action? Deactivated;

    public event Action<Size>? SizeChanged;

    public event Action<uint, uint>? DpiChanged;

    public event Action<Theme, Theme>? ThemeChanged;

    public event Action? FirstFrameRendered;

    /// <summary>
    /// Preview (tunneling) keyboard events for the whole window.
    /// If <see cref="KeyEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<KeyEventArgs>? PreviewKeyDown;

    public event Action<KeyEventArgs>? PreviewKeyUp;

    /// <summary>
    /// Preview (tunneling) text input for the whole window.
    /// If <see cref="TextInputEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<TextInputEventArgs>? PreviewTextInput;

    #endregion

    internal void RaisePreviewKeyDown(KeyEventArgs e) => PreviewKeyDown?.Invoke(e);

    internal void RaisePreviewKeyUp(KeyEventArgs e) => PreviewKeyUp?.Invoke(e);

    internal void RaisePreviewTextInput(TextInputEventArgs e) => PreviewTextInput?.Invoke(e);

    internal void RaiseActivated() => Activated?.Invoke();

    internal void RaiseDeactivated() => Deactivated?.Invoke();

    public Theme Theme
    {
        get => _theme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_theme == value)
            {
                return;
            }

            var old = _theme;
            _theme = value;

            if (Handle != 0)
            {
                BroadcastThemeChanged(old, value);
                PerformLayout();
                Invalidate();
            }
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        if (Background == oldTheme.Palette.WindowBackground)
        {
            Background = newTheme.Palette.WindowBackground;
        }

        base.OnThemeChanged(oldTheme, newTheme);
    }

    public void Show()
    {
        EnsureBackend();
        _backend!.Show();

        // Raise Loaded once, and only after the application's dispatcher is ready.
        // Do not rely on PlatformHost.Run ordering: a first render can happen during Show on some platforms.
        if (!_loadedRaised && Application.IsRunning)
        {
            if (Application.Current.Dispatcher != null)
            {
                RaiseLoaded();
            }
            else
            {
                SubscribeToDispatcherChanged();
            }
        }
    }

    public void Hide() => _backend?.Hide();

    public void Close() => _backend?.Close();

    private void EnsureBackend()
    {
        if (_backend != null)
        {
            return;
        }

        if (!Application.IsRunning)
        {
            throw new InvalidOperationException("Application is not running. Call Application.Run() first.");
        }

        _backend = Application.Current.PlatformHost.CreateWindowBackend(this);
        _backend.SetResizable(WindowSize.IsResizable);
    }

    public void PerformLayout()
    {
        if (Handle == 0 || Content == null)
        {
            return;
        }

        var clientSize = _clientSizeDip.IsEmpty ? new Size(Width, Height) : _clientSizeDip;
        var padding = Padding;

        // Layout can be expensive (e.g., large item collections). If nothing is dirty and the
        // client size hasn't changed, avoid re-running Measure/Arrange on every paint.
        if (clientSize == _lastLayoutClientSizeDip &&
            padding == _lastLayoutPadding &&
            ReferenceEquals(Content, _lastLayoutContent) &&
            !IsLayoutDirty(Content))
        {
            return;
        }

#if DEV_DEBUG
        Debug.WriteLine("PerformLayout");
#endif

        const int maxPasses = 8;
        var contentSize = clientSize.Deflate(padding);

        bool needMeasure = HasMeasureDirty(Content);
        for (int pass = 0; pass < maxPasses; pass++)
        {
            if (needMeasure)
            {
                Content.Measure(contentSize);
            }

            Content.Arrange(new Rect(padding.Left, padding.Top, contentSize.Width, contentSize.Height));

            if (!IsLayoutDirty(Content))
            {
                break;
            }

            // If only Arrange dirtiness remains after the first pass, avoid re-running Measure.
            if (needMeasure && !HasMeasureDirty(Content))
            {
                needMeasure = false;
            }
        }

        _lastLayoutClientSizeDip = clientSize;
        _lastLayoutPadding = padding;
        _lastLayoutContent = Content;
    }

    private static bool HasMeasureDirty(Element root)
    {
        bool dirty = false;
        VisitVisualTree(root, e =>
        {
            if (dirty)
            {
                return;
            }

            if (e.IsMeasureDirty)
            {
                dirty = true;
            }
        });

        return dirty;
    }

    private static bool IsLayoutDirty(Element root)
    {
        bool dirty = false;
        VisitVisualTree(root, e =>
        {
            if (dirty)
            {
                return;
            }

            if (e.IsMeasureDirty || e.IsArrangeDirty)
            {
                dirty = true;
            }
        });
        return dirty;
    }

    public void Invalidate() => RequestRender();

    public override void InvalidateVisual() => RequestRender();

    private void InvalidateBackend()
    {
        if (_backend == null)
        {
            return;
        }

        _backend.Invalidate(true);
    }

    public override void InvalidateMeasure()
    {
        base.InvalidateMeasure();
        RequestLayout();
    }

    public override void InvalidateArrange()
    {
        base.InvalidateArrange();
        RequestLayout();
    }

    private void RequestLayout()
    {
        var dispatcher = ApplicationDispatcher;
        if (dispatcher == null)
        {
            // Fallback: we have no UI dispatcher yet; rely on immediate invalidation.
            InvalidateBackend();
            return;
        }

        dispatcher.PostMerged(_layoutMergeKey, () =>
        {
            PerformLayout();
            RequestRender();
        }, UiDispatcherPriority.Layout);
    }

    private void RequestRender()
    {
        var dispatcher = ApplicationDispatcher;
        if (dispatcher == null)
        {
            InvalidateBackend();
            return;
        }

        dispatcher.PostMerged(_renderMergeKey, InvalidateBackend, UiDispatcherPriority.Render);
    }

    internal bool SetFocusedElement(UIElement element) => FocusManager.SetFocus(element);

    public void RequerySuggested()
    {
        if (Content == null)
        {
            return;
        }

        VisitVisualTree(Content, e =>
        {
            if (e is UIElement u)
            {
                u.ReevaluateSuggestedIsEnabled();
            }
        });
    }

    public void CaptureMouse(UIElement element)
    {
        EnsureBackend();
        _backend!.CaptureMouse(element);
    }

    public void ReleaseMouseCapture() => _backend?.ReleaseMouseCapture();

    internal void AttachBackend(IWindowBackend backend)
    {
        _backend = backend;
        _backend.SetTitle(Title);
        _backend.SetResizable(WindowSize.IsResizable);
        _backend.SetIcon(Icon);
    }

    internal void SetDpi(uint dpi) => Dpi = dpi;

    internal void SetClientSizeDip(double widthDip, double heightDip) => _clientSizeDip = new Size(widthDip, heightDip);

    internal void SetIsActive(bool isActive) => IsActive = isActive;

    internal void RaiseLoaded()
    {
        if (_loadedRaised)
        {
            return;
        }

        _loadedRaised = true;

        PerformLayout();
        Loaded?.Invoke();

        if (_firstFrameRenderedPending && !_firstFrameRenderedRaised)
        {
            _firstFrameRenderedPending = false;
            _firstFrameRenderedRaised = true;
            FirstFrameRendered?.Invoke();
        }
    }

    internal void RaiseClosed()
    {
        UnsubscribeFromDispatcherChanged();

        if (Application.IsRunning)
        {
            Application.Current.UnregisterWindow(this);
        }

        Closed?.Invoke();
    }

    internal void RaiseSizeChanged(double widthDip, double heightDip) => SizeChanged?.Invoke(new Size(widthDip, heightDip));

    internal void RenderFrame(nint hdc)
    {
        // Some platforms can render before Loaded is raised due to Run/Show/Dispatcher ordering.
        // Ensure Loaded is raised as soon as the dispatcher is available, and always before FirstFrameRendered.
        if (!_loadedRaised && Application.IsRunning && Application.Current.Dispatcher != null)
        {
            RaiseLoaded();
        }

        using var context = GraphicsFactory.CreateContext(Handle, hdc, DpiScale);
        context.Clear(Background.A > 0 ? Background : Theme.Palette.WindowBackground);

        // Ensure nothing paints outside the client area.
        var clientSize = _clientSizeDip.IsEmpty ? new Size(Width, Height) : _clientSizeDip;
        context.Save();
        context.SetClip(new Rect(0, 0, clientSize.Width, clientSize.Height));

        try
        {
            Content?.Render(context);

            // Popups render last (on top).
            for (int i = 0; i < _popups.Count; i++)
            {
                _popups[i].Element.Render(context);
            }
        }
        finally
        {
            context.Restore();
        }

        if (!_firstFrameRenderedRaised)
        {
            if (_loadedRaised)
            {
                _firstFrameRenderedRaised = true;
                FirstFrameRendered?.Invoke();
            }
            else
            {
                _firstFrameRenderedPending = true;
            }
        }
    }

    private void SubscribeToDispatcherChanged()
    {
        if (_subscribedToDispatcherChanged)
        {
            return;
        }

        _subscribedToDispatcherChanged = true;
        Application.DispatcherChanged += OnDispatcherChanged;
    }

    private void UnsubscribeFromDispatcherChanged()
    {
        if (!_subscribedToDispatcherChanged)
        {
            return;
        }

        _subscribedToDispatcherChanged = false;
        Application.DispatcherChanged -= OnDispatcherChanged;
    }

    private void OnDispatcherChanged(IUiDispatcher? dispatcher)
    {
        if (dispatcher == null)
        {
            return;
        }

        // Ensure Loaded is raised on the UI thread.
        dispatcher.Send(() =>
        {
            UnsubscribeFromDispatcherChanged();
            RaiseLoaded();
        });
    }

    internal void DisposeVisualTree()
    {
        if (Content == null)
        {
            DisposePopups();
            return;
        }

        VisualTree.Visit(Content, element =>
        {
            if (element is IDisposable disposable)
            {
                disposable.Dispose();
            }
        });

        DisposePopups();
    }

    private void DisposePopups()
    {
        foreach (var popup in _popups)
        {
            if (popup.Element is IDisposable disposable)
            {
                disposable.Dispose();
            }

            popup.Element.Parent = null;
        }
        _popups.Clear();
    }

    private void BroadcastThemeChanged(Theme oldTheme, Theme newTheme)
    {
        OnThemeChanged(oldTheme, newTheme);
        ThemeChanged?.Invoke(oldTheme, newTheme);

        if (Content != null)
        {
            VisitVisualTree(Content, e =>
            {
                if (e is Control c)
                {
                    c.NotifyThemeChanged(oldTheme, newTheme);
                }
            });
        }

        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element is Control c)
            {
                c.NotifyThemeChanged(oldTheme, newTheme);
            }
        }
    }

    private static void VisitVisualTree(Element element, Action<Element> visitor) => VisualTree.Visit(element, visitor);

    internal void RaiseDpiChanged(uint oldDpi, uint newDpi)
    {
        OnDpiChanged(oldDpi, newDpi);
        DpiChanged?.Invoke(oldDpi, newDpi);

        if (Content != null)
        {
            // Clear cached DPI values so subsequent GetDpi() calls don't traverse parents.
            // This also ensures subtrees moved between windows/tabs don't retain stale DPI.
            VisitVisualTree(Content, e => e.ClearDpiCache());

            VisitVisualTree(Content, e =>
            {
                if (e is Control c)
                {
                    c.NotifyDpiChanged(oldDpi, newDpi);
                }
            });
        }

        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element is Control c)
            {
                c.NotifyDpiChanged(oldDpi, newDpi);
            }

            _popups[i].Element.ClearDpiCacheDeep();
        }
    }

    internal void ClosePopupsIfClickOutside(Point position)
    {
        if (_popups.Count == 0)
        {
            return;
        }

        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            if (_popups[i].Bounds.Contains(position))
            {
                return;
            }
        }

        CloseAllPopups();
    }

    internal void CloseAllPopups()
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            entry.Element.Parent = null;
            if (entry.Owner is IPopupOwner owner)
            {
                owner.OnPopupClosed(entry.Element);
            }
        }
        _popups.Clear();
        Invalidate();
    }

    internal void ShowPopup(UIElement owner, UIElement popup, Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(popup);

        // Replace if already present.
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element == popup)
            {
                _popups[i].Owner = owner;
                UpdatePopup(popup, bounds);
                return;
            }
        }

        // Popups can be cached/reused (e.g. ComboBox keeps a ListBox instance even while closed).
        // If a popup is moved between windows (or the window DPI differs), ensure the popup updates its DPI-sensitive
        // caches (fonts, layout) before measuring/arranging.
        uint oldDpi = popup.GetDpiCached();
        popup.Parent = this;
        ApplyPopupDpiChange(popup, oldDpi, Dpi);
        var entry = new PopupEntry { Owner = owner, Element = popup, Bounds = bounds };
        _popups.Add(entry);
        LayoutPopup(entry);
        Invalidate();
    }

    internal void UpdatePopup(UIElement popup, Rect bounds)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element != popup)
            {
                continue;
            }

            _popups[i].Bounds = bounds;
            LayoutPopup(_popups[i]);
            Invalidate();
            return;
        }
    }

    internal void ClosePopup(UIElement popup)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element != popup)
            {
                continue;
            }

            var entry = _popups[i];
            _popups[i].Element.Parent = null;
            _popups.RemoveAt(i);
            if (entry.Owner is IPopupOwner owner)
            {
                owner.OnPopupClosed(entry.Element);
            }

            Invalidate();
            return;
        }
    }

    private void LayoutPopup(PopupEntry entry)
    {
        entry.Element.Measure(new Size(entry.Bounds.Width, entry.Bounds.Height));
        entry.Element.Arrange(entry.Bounds);

        // Keep the stored bounds consistent with the actually arranged (layout-rounded) bounds,
        // otherwise hit-testing (e.g. mouse wheel on popup content) can miss by sub-pixel rounding.
        entry.Bounds = entry.Element.Bounds;
    }

    private static void ApplyPopupDpiChange(UIElement popup, uint oldDpi, uint newDpi)
    {
        if (oldDpi == 0 || newDpi == 0 || oldDpi == newDpi)
        {
            return;
        }

        // Clear DPI caches again (Parent assignment already does this, but be defensive for future changes),
        // and notify controls so they can recreate DPI-dependent resources (fonts, etc.).
        popup.ClearDpiCacheDeep();
        VisitVisualTree(popup, e =>
        {
            e.ClearDpiCache();
            if (e is Control c)
            {
                c.NotifyDpiChanged(oldDpi, newDpi);
            }
        });
    }

    protected override UIElement? OnHitTest(Point point)
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            if (!_popups[i].Bounds.Contains(point))
            {
                continue;
            }

            var hit = _popups[i].Element.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return (Content as UIElement)?.HitTest(point);
    }
}
