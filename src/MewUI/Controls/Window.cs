using System.Runtime.CompilerServices;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a top-level window.
/// </summary>
public partial class Window : ContentControl, ILayoutRoundingHost
{
    private readonly DispatcherMergeKey _layoutMergeKey = new(DispatcherPriority.Layout);
    private readonly DispatcherMergeKey _renderMergeKey = new(DispatcherPriority.Render);

    private enum WindowLifetimeState
    {
        New,
        Shown,
        Hidden,
        Closed,
    }

    private const double DefaultWidth = 800;
    private const double DefaultHeight = 600;

    private IWindowBackend? _backend;
    private Size _clientSizeDip = new(DefaultWidth, DefaultHeight);
    private Size _lastLayoutClientSizeDip = Size.Empty;
    private Thickness _lastLayoutPadding = Thickness.Zero;
    private Element? _lastLayoutContent;
    private readonly List<AdornerEntry> _adorners = new();
    private readonly RadioGroupManager _radioGroups = new();
    private readonly List<Window> _ownedChildren = new();
    private readonly List<Window> _modalChildren = new();
    private readonly List<UIElement> _mouseOverOldPath = new(capacity: 16);
    private readonly List<UIElement> _mouseOverNewPath = new(capacity: 16);
    private UIElement? _mouseOverElement;
    private UIElement? _capturedElement;
    private Point _lastMousePositionDip;
    private Point _lastMouseScreenPositionPx;
    private bool _loadedRaised;
    private bool _firstFrameRenderedRaised;
    private bool _firstFrameRenderedPending;
    private bool _subscribedToDispatcherChanged;
    private WindowLifetimeState _lifetimeState;
    private int _modalDisableCount;
    private bool _isDialogWindow;
    internal Action<Window>? BuildCallback { get; private set; }

    internal Point LastMousePositionDip => _lastMousePositionDip;

    internal Point LastMouseScreenPositionPx => _lastMouseScreenPositionPx;

    internal UIElement? MouseOverElement => _mouseOverElement;

    internal UIElement? CapturedElement => _capturedElement;

    internal bool HasMouseCapture => _capturedElement != null;

    internal void ClearMouseCaptureState()
    {
        if (_capturedElement != null)
        {
            _capturedElement.SetMouseCaptured(false);
            _capturedElement = null;
        }
    }

    internal void ClearMouseOverState()
    {
        if (_mouseOverElement != null)
        {
            UpdateMouseOverChain(_mouseOverElement, null);
            _mouseOverElement = null;
        }
    }

    public void ClearMouseOver()
    {
        ClearMouseOverState();
    }

    internal void SetMouseOverElement(UIElement? element) => _mouseOverElement = element;

    /// <summary>
    /// Resolves the effective cursor for the given element by walking up the visual tree
    /// until a non-None cursor is found, then applies it via the backend.
    /// </summary>
    internal void UpdateCursorForElement(UIElement? element)
    {
        var cursor = CursorType.None;
        for (var current = element; current != null; current = current.Parent as UIElement)
        {
            var c = current.Cursor;
            if (c != CursorType.None)
            {
                cursor = c;
                break;
            }
        }

        _backend?.SetCursor(cursor == CursorType.None ? CursorType.Arrow : cursor);
    }

    internal void UpdateLastMousePosition(Point positionDip, Point screenPositionPx)
    {
        _lastMousePositionDip = positionDip;
        _lastMouseScreenPositionPx = screenPositionPx;
    }

    internal void ReevaluateMouseOver()
    {
        ApplicationDispatcher?.BeginInvoke(DispatcherPriority.Layout, () =>
        {
            // When layout/scroll offsets change without an actual mouse move, the element under the cursor can change.
            // Re-run hit testing at the last known mouse position to keep IsMouseOver state accurate.
            var leaf = WindowInputRouter.HitTest(this, _lastMousePositionDip);
            WindowInputRouter.UpdateMouseOver(this, leaf);
        });
    }

    internal void UpdateMouseOverChain(UIElement? oldLeaf, UIElement? newLeaf)
    {
        if (ReferenceEquals(oldLeaf, newLeaf))
        {
            return;
        }

        _mouseOverOldPath.Clear();
        for (var current = oldLeaf; current != null; current = current.Parent as UIElement)
        {
            _mouseOverOldPath.Add(current);
        }

        _mouseOverNewPath.Clear();
        for (var current = newLeaf; current != null; current = current.Parent as UIElement)
        {
            _mouseOverNewPath.Add(current);
        }

        int commonFromRoot = 0;
        while (commonFromRoot < _mouseOverOldPath.Count && commonFromRoot < _mouseOverNewPath.Count)
        {
            var oldAt = _mouseOverOldPath[_mouseOverOldPath.Count - 1 - commonFromRoot];
            var newAt = _mouseOverNewPath[_mouseOverNewPath.Count - 1 - commonFromRoot];
            if (!ReferenceEquals(oldAt, newAt))
            {
                break;
            }

            commonFromRoot++;
        }

        int oldUniqueCount = _mouseOverOldPath.Count - commonFromRoot;
        for (int i = 0; i < oldUniqueCount; i++)
        {
            _mouseOverOldPath[i].SetMouseOver(false);
        }

        int newUniqueCount = _mouseOverNewPath.Count - commonFromRoot;
        for (int i = newUniqueCount - 1; i >= 0; i--)
        {
            _mouseOverNewPath[i].SetMouseOver(true);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Window"/> class.
    /// </summary>
    public Window()
    {
        AdornerLayer = new AdornerLayer(this);
        OverlayLayer = new OverlayLayer(this);
        _popupManager = new PopupManager(this);

#if DEBUG
        if (this is not DebugVisualTreeWindow)
        {
            InitializeDebugDevTools();
        }
#endif
    }

    public AdornerLayer AdornerLayer { get; }

    /// <summary>
    /// Window-level overlay layer for elements positioned relative to the full window area.
    /// Renders above adorners but below popups. Examples: toast, progress ring, dim background.
    /// </summary>
    public OverlayLayer OverlayLayer { get; }

    /// <summary>
    /// Shows a toast notification at 2/3 of the window height.
    /// Auto-dismisses after a duration based on text length.
    /// </summary>
    public void ShowToast(string text)
    {
        _toastPresenter ??= new Controls.ToastPresenter();

        if (!OverlayLayer.Contains(_toastPresenter))
            OverlayLayer.Add(_toastPresenter);

        var t = text ?? string.Empty;
        _toastPresenter.Show(t, Controls.ToastPresenter.ComputeDuration(t));
    }

    private Controls.ToastPresenter? _toastPresenter;

    private readonly PopupManager _popupManager;

    private sealed class AdornerEntry
    {
        public required UIElement Adorned { get; init; }
        public required UIElement Element { get; init; }
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

    /// <summary>
    /// Gets the platform window handle.
    /// </summary>
    public nint Handle => _backend?.Handle ?? 0;

    /// <summary>
    /// Converts a client-relative point in DIPs to screen coordinates in device pixels.
    /// </summary>
    /// <param name="clientPointDip">The client point in DIPs.</param>
    /// <returns>The screen point in device pixels.</returns>
    public Point ClientToScreen(Point clientPointDip)
    {
        if (_backend == null || Handle == 0)
        {
            throw new InvalidOperationException("Window is not initialized.");
        }

        return _backend.ClientToScreen(clientPointDip);
    }

    /// <summary>
    /// Converts a screen point in device pixels to client-relative coordinates in DIPs.
    /// </summary>
    /// <param name="screenPointPx">The screen point in device pixels.</param>
    /// <returns>The client point in DIPs.</returns>
    public Point ScreenToClient(Point screenPointPx)
    {
        if (_backend == null || Handle == 0)
        {
            throw new InvalidOperationException("Window is not initialized.");
        }

        return _backend.ScreenToClient(screenPointPx);
    }

    /// <summary>
    /// Gets or sets the window size configuration.
    /// </summary>
    public WindowSize WindowSize
    {
        get;
        set
        {
            var previous = field;
            field = value;

            if (_backend != null)
            {
                if (previous.IsResizable != field.IsResizable)
                    _backend.SetResizable(field.IsResizable);

                // FitContent defers sizing to PerformLayout.
                if (!double.IsNaN(field.Width) && !double.IsNaN(field.Height))
                    _backend.SetClientSize(field.Width, field.Height);
            }
        }
    } = WindowSize.Resizable(DefaultWidth, DefaultHeight);

    public static readonly MewProperty<string> TitleProperty =
        MewProperty<string>.Register<Window>(nameof(Title), "Window", MewPropertyOptions.None,
            static (self, _, _) =>self._backend?.SetTitle(self.Title));

    public static readonly MewProperty<IconSource?> IconProperty =
        MewProperty<IconSource?>.Register<Window>(nameof(Icon), null, MewPropertyOptions.None,
            static (self, _, _) =>self._backend?.SetIcon(self.Icon));

    public static readonly MewProperty<WindowStartupLocation> StartupLocationProperty =
        MewProperty<WindowStartupLocation>.Register<Window>(nameof(StartupLocation), WindowStartupLocation.CenterScreen, MewPropertyOptions.None);

    public static readonly MewProperty<double> OpacityProperty =
        MewProperty<double>.Register<Window>(nameof(Opacity), 1.0, MewPropertyOptions.None,
            static (self, _, _) =>self._backend?.SetOpacity(self.Opacity));

    public static readonly MewProperty<bool> AllowsTransparencyProperty =
        MewProperty<bool>.Register<Window>(nameof(AllowsTransparency), false, MewPropertyOptions.AffectsRender,
            static (self, _, _) =>self._backend?.SetAllowsTransparency(self.AllowsTransparency));

    public static readonly MewProperty<bool> UseLayoutRoundingProperty =
        MewProperty<bool>.Register<Window>(nameof(UseLayoutRounding), true, MewPropertyOptions.None);

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the window icon.
    /// </summary>
    public IconSource? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets the owner window. Set via <see cref="Show(Window?)"/> or <see cref="ShowDialogAsync(Window?)"/>.
    /// Used for <see cref="WindowStartupLocation.CenterOwner"/> positioning and modal dialog ownership.
    /// </summary>
    public Window? Owner { get; private set; }

    internal bool IsDialogWindow => _isDialogWindow;

    /// <summary>
    /// Gets or sets the initial window placement behavior.
    /// Must be set before <see cref="Show"/> is called.
    /// </summary>
    public WindowStartupLocation StartupLocation
    {
        get => GetValue(StartupLocationProperty);
        set
        {
            ThrowIfShown();
            SetValue(StartupLocationProperty, value);
        }
    }

    /// <summary>
    /// Gets the resolved startup position in DIPs for <see cref="WindowStartupLocation.CenterOwner"/>
    /// and <see cref="WindowStartupLocation.Manual"/> modes. <see langword="null"/> for <see cref="WindowStartupLocation.CenterScreen"/>.
    /// </summary>
    internal Point? ResolvedStartupPosition => StartupPosition;

    internal Point? StartupPosition
    {
        get;
        set
        {
            ThrowIfShown();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the window opacity (0..1).
    /// </summary>
    public double Opacity
    {
        get => GetValue(OpacityProperty);
        set => SetValue(OpacityProperty, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>
    /// Gets or sets whether the window supports per-pixel transparency (platform dependent).
    /// </summary>
    public bool AllowsTransparency
    {
        get => GetValue(AllowsTransparencyProperty);
        set => SetValue(AllowsTransparencyProperty, value);
    }

    /// <summary>
    /// Gets the actual window client width in DIPs.
    /// To change the window size, use <see cref="WindowSize"/>.
    /// </summary>
    public new double Width => ClientSize.Width;

    /// <summary>
    /// Gets the actual window client height in DIPs.
    /// To change the window size, use <see cref="WindowSize"/>.
    /// </summary>
    public new double Height => ClientSize.Height;

    /// <summary>
    /// Gets the minimum width from <see cref="WindowSize"/>. Use <see cref="WindowSize"/> to configure constraints.
    /// </summary>
    public new double MinWidth => WindowSize.MinWidth;

    /// <summary>
    /// Gets the minimum height from <see cref="WindowSize"/>. Use <see cref="WindowSize"/> to configure constraints.
    /// </summary>
    public new double MinHeight => WindowSize.MinHeight;

    /// <summary>
    /// Gets the maximum width from <see cref="WindowSize"/>. Use <see cref="WindowSize"/> to configure constraints.
    /// </summary>
    public new double MaxWidth => WindowSize.MaxWidth;

    /// <summary>
    /// Gets the maximum height from <see cref="WindowSize"/>. Use <see cref="WindowSize"/> to configure constraints.
    /// </summary>
    public new double MaxHeight => WindowSize.MaxHeight;

    /// <summary>
    /// Gets whether the window is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the current DPI value.
    /// </summary>
    public uint Dpi { get; private set; } = 96;

    /// <summary>
    /// Gets the DPI scale factor relative to 96 DPI.
    /// </summary>
    public double DpiScale => Dpi / 96.0;

    /// <summary>
    /// Gets the client size in DIPs.
    /// </summary>
    public Size ClientSize => _clientSizeDip;

    /// <summary>
    /// Gets or sets the window position in screen coordinates (DIPs).
    /// </summary>
    public Point Position
    {
        get
        {
            if (_backend == null || Handle == 0)
            {
                return default;
            }

            return _backend.GetPosition();
        }
        set
        {
            if (_backend == null || Handle == 0)
            {
                return;
            }

            _backend.SetPosition(value.X, value.Y);
        }
    }

    /// <summary>
    /// Moves the window to the specified screen position (DIPs).
    /// </summary>
    public void MoveTo(double leftDip, double topDip)
    {
        if (_backend == null || Handle == 0)
        {
            return;
        }

        _backend.SetPosition(leftDip, topDip);
    }

    /// <summary>
    /// Gets or sets whether layout rounding is enabled.
    /// </summary>
    public bool UseLayoutRounding
    {
        get => GetValue(UseLayoutRoundingProperty);
        set => SetValue(UseLayoutRoundingProperty, value);
    }

    /// <summary>
    /// Gets the focus manager for this window.
    /// </summary>
    public FocusManager FocusManager => field ??= new FocusManager(this);

    /// <summary>
    /// Gets the graphics factory for rendering.
    /// </summary>
    public IGraphicsFactory GraphicsFactory => Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;

    internal IDispatcher? ApplicationDispatcher => Application.IsRunning ? Application.Current.Dispatcher : null;

    #region Events

    /// <summary>
    /// Occurs when the window is loaded and ready.
    /// </summary>
    public event Action? Loaded;

    /// <summary>
    /// Occurs when the window is closed.
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// Occurs when the window is activated.
    /// </summary>
    public event Action? Activated;

    /// <summary>
    /// Occurs when the window is deactivated.
    /// </summary>
    public event Action? Deactivated;

    /// <summary>
    /// Occurs when the client size changes.
    /// </summary>
    public event Action<Size>? ClientSizeChanged;

    /// <summary>
    /// Occurs when the DPI changes.
    /// </summary>
    public event Action<uint, uint>? DpiChanged;

    /// <summary>
    /// Occurs when the theme changes.
    /// </summary>
    public event Action<Theme, Theme>? ThemeChanged;

    /// <summary>
    /// Occurs when the first frame is rendered.
    /// </summary>
    public event Action? FirstFrameRendered;

    /// <summary>
    /// Occurs after each frame is rendered.
    /// </summary>
    public event Action? FrameRendered;

    /// <summary>
    /// Gets the rendering statistics from the most recent frame.
    /// </summary>
    public RenderStats LastFrameStats { get; private set; }

    /// <summary>
    /// Preview (tunneling) keyboard events for the whole window.
    /// If <see cref="KeyEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<KeyEventArgs>? PreviewKeyDown;

    /// <summary>
    /// Preview (tunneling) keyboard events for the whole window.
    /// If <see cref="KeyEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<KeyEventArgs>? PreviewKeyUp;

    /// <summary>
    /// Preview (tunneling) text input for the whole window.
    /// If <see cref="TextInputEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<TextInputEventArgs>? PreviewTextInput;

    /// <summary>
    /// Preview (tunneling) text composition (IME pre-edit) start for the whole window.
    /// If <see cref="TextCompositionEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<TextCompositionEventArgs>? PreviewTextCompositionStart;

    /// <summary>
    /// Preview (tunneling) text composition (IME pre-edit) update for the whole window.
    /// If <see cref="TextCompositionEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<TextCompositionEventArgs>? PreviewTextCompositionUpdate;

    /// <summary>
    /// Preview (tunneling) text composition (IME pre-edit) end for the whole window.
    /// If <see cref="TextCompositionEventArgs.Handled"/> is set, the focused element will not receive the event.
    /// </summary>
    public event Action<TextCompositionEventArgs>? PreviewTextCompositionEnd;

    #endregion

    internal void RaisePreviewKeyDown(KeyEventArgs e) => PreviewKeyDown?.Invoke(e);

    internal void RaisePreviewKeyUp(KeyEventArgs e) => PreviewKeyUp?.Invoke(e);

    internal void RaisePreviewTextInput(TextInputEventArgs e) => PreviewTextInput?.Invoke(e);

    internal void RaisePreviewTextCompositionStart(TextCompositionEventArgs e) => PreviewTextCompositionStart?.Invoke(e);

    internal void RaisePreviewTextCompositionUpdate(TextCompositionEventArgs e) => PreviewTextCompositionUpdate?.Invoke(e);

    internal void RaisePreviewTextCompositionEnd(TextCompositionEventArgs e) => PreviewTextCompositionEnd?.Invoke(e);

    internal void RaiseActivated() => Activated?.Invoke();

    internal void RaiseDeactivated() => Deactivated?.Invoke();

    /// <summary>
    /// Shows the window.
    /// </summary>
    /// <param name="owner">Optional owner window for <see cref="WindowStartupLocation.CenterOwner"/> positioning.</param>
    public void Show(Window? owner = null)
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            throw new InvalidOperationException("Cannot show a closed window.");
        }

        if (owner != null)
        {
            Owner = owner;
            owner.RegisterOwnedChild(this);
        }

        EnsureBackend();
        Application.Current.RegisterWindow(this);

        if (_lifetimeState == WindowLifetimeState.Shown)
        {
            return;
        }

        ResolveStartupPosition();
        _backend!.EnsureTheme(Theme.IsDark);
        _backend!.Show();
        _lifetimeState = WindowLifetimeState.Shown;

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

    /// <summary>
    /// Hides the window.
    /// </summary>
    public void Hide()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            return;
        }

        if (_backend == null)
        {
            return;
        }

        if (_lifetimeState == WindowLifetimeState.Hidden)
        {
            return;
        }

        _backend.Hide();
        _lifetimeState = WindowLifetimeState.Hidden;
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    public void Close()
        => RequestClose(fromBackend: false);

    internal void RequestClose(bool fromBackend)
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            return;
        }

        if (_backend == null)
        {
            RaiseClosed();
            return;
        }

        if (fromBackend)
        {
            RaiseClosed();
            return;
        }

        _backend.Close();
    }

    /// <summary>
    /// Attempts to activate the window (bring to front / focus), platform dependent.
    /// </summary>
    public void Activate()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            return;
        }

        if (_backend == null || Handle == 0)
        {
            return;
        }

        _backend.Activate();
    }

    /// <summary>
    /// Shows the window as a modal dialog and completes when the dialog is closed.
    /// </summary>
    /// <param name="owner">Optional owner window to disable while the dialog is open.</param>
    public Task ShowDialogAsync(Window? owner = null)
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            throw new InvalidOperationException("Cannot show a closed window.");
        }

        if (owner != null && ReferenceEquals(owner, this))
        {
            throw new ArgumentException("Owner cannot be the dialog itself.", nameof(owner));
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnClosed()
        {
            Closed -= OnClosed;
            if (owner != null)
            {
                owner.ReleaseModalDisable();
                owner.UnregisterModalChild(this);
                if (owner._lifetimeState != WindowLifetimeState.Closed)
                {
                    var target = owner.GetTopModalChild() ?? owner;
                    target.Activate();
                }
            }
            tcs.TrySetResult();
        }

        Closed += OnClosed;

        try
        {
            _isDialogWindow = true;
            if (owner != null)
            {
                owner.AcquireModalDisable();
                owner.RegisterModalChild(this);
            }

            if (owner != null && Icon == null && owner.Icon != null)
                Icon = owner.Icon;

            Show(owner);
            if (owner != null && _backend != null && Handle != 0)
            {
                _backend.SetOwner(owner.Handle);
            }
            Activate();
        }
        catch (Exception ex)
        {
            Closed -= OnClosed;
            if (owner != null)
            {
                owner.ReleaseModalDisable();
                owner.UnregisterModalChild(this);
            }
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    private void RegisterModalChild(Window child)
    {
        if (child == null || ReferenceEquals(child, this))
        {
            return;
        }

        if (_modalChildren.Contains(child))
        {
            return;
        }

        _modalChildren.Add(child);
    }

    private void UnregisterModalChild(Window child)
    {
        _modalChildren.Remove(child);
    }

    private void RegisterOwnedChild(Window child)
    {
        if (child == null || ReferenceEquals(child, this))
        {
            return;
        }

        if (_ownedChildren.Contains(child))
        {
            return;
        }

        _ownedChildren.Add(child);
    }

    private void UnregisterOwnedChild(Window child)
    {
        _ownedChildren.Remove(child);
    }

    internal Window? GetTopModalChild()
    {
        Window? current = this;
        Window? result = null;

        while (current != null)
        {
            Window? next = null;
            for (int i = current._modalChildren.Count - 1; i >= 0; i--)
            {
                var child = current._modalChildren[i];
                if (child != null && child._lifetimeState != WindowLifetimeState.Closed)
                {
                    next = child;
                    break;
                }
            }

            if (next == null)
            {
                break;
            }

            result = next;
            current = next;
        }

        return result;
    }

    internal void NotifyInputWhenDisabled()
    {
        var child = GetTopModalChild();
        child?.Activate();
    }

    private void AcquireModalDisable()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            return;
        }

        _modalDisableCount++;
        if (_modalDisableCount == 1)
        {
            _backend?.SetEnabled(false);
        }
    }

    private void ReleaseModalDisable()
    {
        if (_modalDisableCount <= 0)
        {
            return;
        }

        _modalDisableCount--;
        if (_modalDisableCount == 0 && _backend != null)
        {
            _backend.SetEnabled(true);
        }
    }

    private void EnsureBackend()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            throw new InvalidOperationException("The window is closed.");
        }

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

    /// <summary>
    /// Performs layout measurement and arrangement for the window content.
    /// </summary>
    public void PerformLayout()
    {
        if (Handle == 0 || Content == null)
        {
            return;
        }

        // Window is the visual root — it has no Parent, so OnVisualRootChanged never fires.
        // Resolve its style here before reading layout-affecting properties like Padding.
        EnsureStyleResolved();

        var padding = Padding;
        var mode = WindowSize.Mode;

        // For FitContent modes, measure content with max constraints first,
        // then resize the window to match the content's desired size.
        if (mode is WindowSizeMode.FitContentWidth or WindowSizeMode.FitContentHeight or WindowSizeMode.FitContentSize)
        {
            var measureWidth = mode is WindowSizeMode.FitContentWidth or WindowSizeMode.FitContentSize
                ? WindowSize.MaxWidth - padding.HorizontalThickness
                : Width - padding.HorizontalThickness;
            var measureHeight = mode is WindowSizeMode.FitContentHeight or WindowSizeMode.FitContentSize
                ? WindowSize.MaxHeight - padding.VerticalThickness
                : Height - padding.VerticalThickness;

            Content.Measure(new Size(Math.Max(0, measureWidth), Math.Max(0, measureHeight)));

            var desired = Content.DesiredSize;
            var fitWidth = mode is WindowSizeMode.FitContentWidth or WindowSizeMode.FitContentSize
                ? Math.Min(desired.Width + padding.HorizontalThickness, WindowSize.MaxWidth)
                : Width;
            var fitHeight = mode is WindowSizeMode.FitContentHeight or WindowSizeMode.FitContentSize
                ? Math.Min(desired.Height + padding.VerticalThickness, WindowSize.MaxHeight)
                : Height;

            if (fitWidth != Width || fitHeight != Height)
            {
                _clientSizeDip = new Size(fitWidth, fitHeight);
                _backend?.SetClientSize(fitWidth, fitHeight);
            }
        }

        var clientSize = _clientSizeDip;

        // Layout can be expensive (e.g., large item collections). If nothing is dirty and the
        // client size hasn't changed, avoid re-running Measure/Arrange on every paint.
        if (clientSize == _lastLayoutClientSizeDip &&
            padding == _lastLayoutPadding &&
            Content == _lastLayoutContent &&
            !IsLayoutDirty(Content) &&
            !HasOverlayLayoutDirty())
        {
            return;
        }

        const int maxPasses = 8;
        var contentSize = clientSize.Deflate(padding);

        bool needMeasure = HasMeasureDirty(Content)
            || clientSize != _lastLayoutClientSizeDip
            || padding != _lastLayoutPadding
            || Content != _lastLayoutContent;
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

        LayoutAdorners();
        LayoutPopups();
        OverlayLayer.Layout(clientSize);
    }

    private bool HasOverlayLayoutDirty()
    {
        // Popups/adorners are not part of the window Content tree, but they still bubble invalidation
        // up to the Window (Parent = this). If we early-return purely based on Content dirtiness,
        // overlay elements can get stuck with stale DesiredSize/Bounds until the owner explicitly
        // re-calls ShowPopup/UpdatePopup.
        if (OverlayLayer.HasLayoutDirty())
        {
            return true;
        }

        for (int i = 0; i < _adorners.Count; i++)
        {
            var element = _adorners[i].Element;
            if (element.IsMeasureDirty || element.IsArrangeDirty)
            {
                return true;
            }
        }

        if (_popupManager.HasLayoutDirty())
        {
            return true;
        }

        return false;
    }

    private void LayoutAdorners()
    {
        if (_adorners.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _adorners.Count; i++)
        {
            var adorned = _adorners[i].Adorned;
            var adorner = _adorners[i].Element;

            if (!adorned.IsVisible || !adorner.IsVisible)
            {
                continue;
            }

            // MewUI bounds are in window coordinates, so we can arrange directly.
            var bounds = adorned.Bounds;
            adorner.Measure(new Size(bounds.Width, bounds.Height));
            adorner.Arrange(bounds);
        }
    }

    private void LayoutPopups()
    {
        _popupManager.LayoutDirtyPopups();
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

    /// <summary>
    /// Requests that the window be redrawn.
    /// </summary>
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

    /// <summary>
    /// Invalidates arrangement and schedules a layout pass.
    /// </summary>
    public override void InvalidateArrange()
    {
        base.InvalidateArrange();
        RequestLayout();
    }

    internal void RequestLayout()
    {
        var dispatcher = ApplicationDispatcher;
        if (dispatcher == null)
        {
            // Fallback: we have no UI dispatcher yet; rely on immediate invalidation.
            InvalidateBackend();
            return;
        }

        (dispatcher as IDispatcherCore)?.PostMerged(_layoutMergeKey, () =>
        {
            PerformLayout();
            RequestRender();
        }, DispatcherPriority.Layout);
    }

    internal void RequestRender()
    {
        var dispatcher = ApplicationDispatcher;
        if (dispatcher == null)
        {
            InvalidateBackend();
            return;
        }

        (dispatcher as IDispatcherCore)?.PostMerged(_renderMergeKey, InvalidateBackend, DispatcherPriority.Render);
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

    /// <summary>
    /// Captures mouse input for the specified element until released.
    /// </summary>
    /// <param name="element">Element that should receive captured mouse events.</param>
    public void CaptureMouse(UIElement element)
    {
        EnsureBackend();
        if (_backend!.Handle == 0)
        {
            return;
        }

        _backend.CaptureMouse();

        if (_capturedElement != null && !ReferenceEquals(_capturedElement, element))
        {
            _capturedElement.SetMouseCaptured(false);
        }

        _capturedElement = element;
        element.SetMouseCaptured(true);
    }

    /// <summary>
    /// Releases any active mouse capture for this window.
    /// </summary>
    public void ReleaseMouseCapture()
    {
        _backend?.ReleaseMouseCapture();
        ClearMouseCaptureState();
    }

    internal void AttachBackend(IWindowBackend backend)
    {
        _backend = backend;
        _backend.SetTitle(Title);
        _backend.SetResizable(WindowSize.IsResizable);
        _backend.SetIcon(Icon);
        _backend.SetOpacity(Opacity);
        _backend.SetAllowsTransparency(AllowsTransparency);
        if (!double.IsNaN(WindowSize.Width) && !double.IsNaN(WindowSize.Height))
            _backend.SetClientSize(WindowSize.Width, WindowSize.Height);
    }


    internal void ReleaseWindowGraphicsResources(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        if (GraphicsFactory is IWindowResourceReleaser releaser)
        {
            releaser.ReleaseWindowResources(windowHandle);
        }
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
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            return;
        }

        if (Owner != null)
        {
            Owner.UnregisterOwnedChild(this);
        }

        if (_ownedChildren.Count > 0)
        {
            var ownedChildren = _ownedChildren.ToArray();
            _ownedChildren.Clear();
            for (int i = 0; i < ownedChildren.Length; i++)
            {
                try { ownedChildren[i]?.Close(); } catch { }
            }
        }

        // Close modal children first (best-effort) so modal tasks complete before owner closes.
        // Copy to avoid modification during Close()->RaiseClosed cascades.
        if (_modalChildren.Count > 0)
        {
            var children = _modalChildren.ToArray();
            _modalChildren.Clear();
            for (int i = 0; i < children.Length; i++)
            {
                try { children[i]?.Close(); } catch { }
            }
        }

        _lifetimeState = WindowLifetimeState.Closed;
        UnsubscribeFromDispatcherChanged();

        if (Application.IsRunning)
        {
            Application.Current.UnregisterWindow(this);
        }

        Closed?.Invoke();
    }

    internal void RaiseClientSizeChanged(double widthDip, double heightDip) => ClientSizeChanged?.Invoke(new Size(widthDip, heightDip));

    internal void RenderFrame(IWindowSurface surface)
    {
        // Some platforms can render before Loaded is raised due to Run/Show/Dispatcher ordering.
        // Ensure Loaded is raised as soon as the dispatcher is available, and always before FirstFrameRendered.
        if (!_loadedRaised && Application.IsRunning && Application.Current.Dispatcher != null)
        {
            RaiseLoaded();
        }

        ArgumentNullException.ThrowIfNull(surface);
        var clientSize = _clientSizeDip;
        RenderFrameCore(new WindowRenderTarget(surface), clientSize);

    }

    internal void RenderFrameToBitmap(IBitmapRenderTarget bitmapTarget)
    {
        if (bitmapTarget == null)
        {
            throw new ArgumentNullException(nameof(bitmapTarget));
        }

        var clientSizeDip = new Size(
            bitmapTarget.PixelWidth / Math.Max(1.0, bitmapTarget.DpiScale),
            bitmapTarget.PixelHeight / Math.Max(1.0, bitmapTarget.DpiScale));

        RenderFrameCore(bitmapTarget, clientSizeDip);
    }

    private void RenderFrameCore(IRenderTarget target, Size clientSize)
    {
        // Update animations before rendering so controls see current values.
        AnimationManager.Instance.Update();

        using var context = GraphicsFactory.CreateContext(target);

        Color clearColor;
        if (AllowsTransparency)
        {
            // Always clear to transparent black so the output remains premultiplied when composited.
            clearColor = Color.Transparent;
        }
        else
        {
            // Default to an opaque window background when the user does not specify one.
            clearColor = Background.A > 0 ? Background : Theme.Palette.WindowBackground;
        }

        context.Clear(clearColor);

        if (AllowsTransparency && Background.A > 0)
        {
            // Draw the background through the normal pipeline so alpha is handled consistently.
            context.FillRectangle(new Rect(0, 0, clientSize.Width, clientSize.Height), Background);
        }

        // Ensure nothing paints outside the client area.
        context.Save();
        // Clip should not shrink due to edge rounding; snap outward to avoid 1px clipping at non-100% DPI.
        context.SetClip(LayoutRounding.SnapViewportRectToPixels(new Rect(0, 0, clientSize.Width, clientSize.Height), DpiScale));

        try
        {
            Content?.Render(context);

            for (int i = 0; i < _adorners.Count; i++)
            {
                _adorners[i].Element.Render(context);
            }

            _popupManager.Render(context);

            OverlayLayer.Render(context);
        }
        finally
        {
            context.Restore();
        }

        if (context is GraphicsContextBase gcb)
            LastFrameStats = new RenderStats(gcb.DrawCallCount, gcb.CullCount);

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

        FrameRendered?.Invoke();
    }

    internal void SetBuildCallback(Action<Window> build)
    {
        BuildCallback = build;
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

    private void OnDispatcherChanged(IDispatcher? dispatcher)
    {
        if (dispatcher == null)
        {
            return;
        }

        // Ensure Loaded is raised on the UI thread.
        dispatcher.Invoke(() =>
        {
            UnsubscribeFromDispatcherChanged();
            RaiseLoaded();
        });
    }

    internal void DisposeVisualTree()
    {
        if (Content == null)
        {
            DisposeAdorners();
            _popupManager.Dispose();
            return;
        }

        VisualTree.Visit(Content, element =>
        {
            if (element is IDisposable disposable)
            {
                disposable.Dispose();
            }
        });

        OverlayLayer.Dispose();
        _toastPresenter = null;

        DisposeAdorners();
        _popupManager.Dispose();
    }

    private void DisposeAdorners()
    {
        foreach (var adorner in _adorners)
        {
            if (adorner.Element is IDisposable disposable)
            {
                disposable.Dispose();
            }

            adorner.Element.Parent = null;
        }

        _adorners.Clear();
    }

    internal void BroadcastThemeChanged(Theme oldTheme, Theme newTheme)
    {
        OnThemeChanged(oldTheme, newTheme);

        _backend?.EnsureTheme(newTheme.IsDark);

        NotifyThemeChanged(oldTheme, newTheme);

        if (Content != null)
        {
            VisitVisualTree(Content, e =>
            {
                if (e is FrameworkElement c)
                {
                    c.NotifyThemeChanged(oldTheme, newTheme);
                }
            });
        }

        OverlayLayer.NotifyThemeChanged(oldTheme, newTheme);

        _popupManager.NotifyThemeChanged(oldTheme, newTheme);

        for (int i = 0; i < _adorners.Count; i++)
        {
            if (_adorners[i].Element is Control c)
            {
                c.NotifyThemeChanged(oldTheme, newTheme);
            }
        }

        ThemeChanged?.Invoke(oldTheme, newTheme);
    }

    internal static void VisitVisualTree(Element element, Action<Element> visitor) => VisualTree.Visit(element, visitor);

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

        OverlayLayer.NotifyDpiChanged(oldDpi, newDpi);
        _popupManager.NotifyDpiChanged(oldDpi, newDpi);

        for (int i = 0; i < _adorners.Count; i++)
        {
            if (_adorners[i].Element is Control c)
            {
                c.NotifyDpiChanged(oldDpi, newDpi);
            }

            _adorners[i].Element.ClearDpiCacheDeep();
        }
    }

    internal void CloseAllPopups()
        => _popupManager.CloseAllPopups();

    internal void ShowPopup(UIElement owner, UIElement popup, Rect bounds, bool staysOpen = false)
        => _popupManager.ShowPopup(owner, popup, bounds, staysOpen);

    internal void RequestClosePopups(PopupCloseRequest request)
        => _popupManager.RequestClosePopups(request);

    internal Size MeasureToolTip(string text, Size availableSize)
        => _popupManager.MeasureToolTip(text, availableSize);

    internal Size MeasureToolTip(Element content, Size availableSize)
        => _popupManager.MeasureToolTip(content, availableSize);

    internal void ShowToolTip(UIElement owner, string text, Rect bounds)
        => _popupManager.ShowToolTip(owner, text, bounds);

    internal void ShowToolTip(UIElement owner, Element content, Rect bounds)
        => _popupManager.ShowToolTip(owner, content, bounds);

    internal void CloseToolTip(UIElement? owner = null)
        => _popupManager.CloseToolTip(owner);

    internal bool TryGetPopupOwner(UIElement popup, out UIElement owner)
        => _popupManager.TryGetPopupOwner(popup, out owner);

    internal void UpdatePopup(UIElement popup, Rect bounds)
        => _popupManager.UpdatePopup(popup, bounds);

    internal void ClosePopup(UIElement popup)
        => _popupManager.ClosePopup(popup, PopupCloseKind.UserInitiated);

    internal void ClosePopup(UIElement popup, PopupCloseKind kind)
        => _popupManager.ClosePopup(popup, kind);

    internal void OnAfterMouseDownHitTest(Point positionInWindow, MouseButton button, UIElement? element)
    {
        // Centralized "mouse down" policy invoked by platform backends after hit testing.
        // Clicking on window background should clear keyboard focus (e.g. TextBox loses focus),
        // even when no element participates in hit testing for that point.
        if (button == MouseButton.Left && element == null)
        {
            FocusManager.ClearFocus();
        }

#if DEBUG
        DebugOnAfterMouseDownHitTest(positionInWindow, button, element);
#endif
    }

#if DEBUG
    partial void DebugOnAfterMouseDownHitTest(Point positionInWindow, MouseButton button, UIElement? element);
#endif

    internal void OnFocusChanged(UIElement? newFocusedElement)
        => _popupManager.RequestClosePopups(PopupCloseRequest.FocusChanged(newFocusedElement));

    protected override UIElement? OnHitTest(Point point)
    {
        var overlayHit = OverlayLayer.HitTest(point);
        if (overlayHit != null)
        {
            return overlayHit;
        }

        var popupHit = _popupManager.HitTest(point);
        if (popupHit != null)
        {
            return popupHit;
        }

        for (int i = _adorners.Count - 1; i >= 0; i--)
        {
            var hit = _adorners[i].Element.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return (Content as UIElement)?.HitTest(point);
    }

    internal void AddAdornerInternal(UIElement adornedElement, UIElement adorner)
    {
        ArgumentNullException.ThrowIfNull(adornedElement);
        ArgumentNullException.ThrowIfNull(adorner);

        // Attach to this window so FindVisualRoot()/theme/DPI work.
        adorner.Parent = this;

        _adorners.Add(new AdornerEntry
        {
            Adorned = adornedElement,
            Element = adorner
        });

        RequestLayout();
        RequestRender();
    }

    internal bool RemoveAdornerInternal(UIElement adorner)
    {
        for (int i = _adorners.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_adorners[i].Element, adorner))
            {
                _adorners[i].Element.Parent = null;
                _adorners.RemoveAt(i);
                RequestLayout();
                RequestRender();
                return true;
            }
        }

        return false;
    }

    internal int RemoveAllAdornersInternal(UIElement adornedElement)
    {
        int removed = 0;
        for (int i = _adorners.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_adorners[i].Adorned, adornedElement))
            {
                _adorners[i].Element.Parent = null;
                _adorners.RemoveAt(i);
                removed++;
            }
        }

        if (removed > 0)
        {
            RequestLayout();
            RequestRender();
        }

        return removed;
    }

    internal void ClearAdornersInternal()
    {
        if (_adorners.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _adorners.Count; i++)
        {
            _adorners[i].Element.Parent = null;
        }

        _adorners.Clear();
        RequestLayout();
        RequestRender();
    }

    internal void EnsureTheme(Theme theme)
    {
        _backend?.EnsureTheme(theme.IsDark);
    }

    private void ResolveStartupPosition()
    {
        if (StartupLocation != WindowStartupLocation.CenterOwner || Owner == null)
            return;

        if (Owner._backend == null || Owner.Handle == 0)
            return;

        var ownerPos = Owner.Position;
        var ownerSize = Owner.ClientSize;
        StartupPosition = new Point(
            ownerPos.X + (ownerSize.Width - Width) / 2,
            ownerPos.Y + (ownerSize.Height - Height) / 2);
    }

    private void ThrowIfShown()
    {
        if (_lifetimeState == WindowLifetimeState.Shown)
            throw new InvalidOperationException("This property must be set before the window is shown.");
    }
}
