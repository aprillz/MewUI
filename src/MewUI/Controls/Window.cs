using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Event arguments for the <see cref="Window.Closing"/> event.
/// </summary>
public sealed class ClosingEventArgs
{
    private Action? _decisionResolved;
    private int _deferralsPending;

    /// <summary>
    /// Set to <c>true</c> to cancel the close operation.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Defers the close decision until the returned deferral completes, letting an asynchronous
    /// handler decide <see cref="Cancel"/> after an await. Take it before the first await.
    /// </summary>
    public ClosingDeferral GetDeferral()
    {
        _deferralsPending++;
        return new ClosingDeferral(this);
    }

    internal bool IsDecisionPending => _deferralsPending > 0;

    internal void AttachDecisionResolved(Action callback) => _decisionResolved = callback;

    internal void CompleteDeferral()
    {
        _deferralsPending--;
        if (_deferralsPending == 0)
        {
            _decisionResolved?.Invoke();
        }
    }
}

/// <summary>
/// Keeps a <see cref="Window.Closing"/> decision pending until <see cref="Complete"/> (or dispose) is called.
/// </summary>
public sealed class ClosingDeferral : IDisposable
{
    private ClosingEventArgs? _owner;

    internal ClosingDeferral(ClosingEventArgs owner) => _owner = owner;

    /// <summary>Completes this deferral; the close decision resolves once every deferral has completed.</summary>
    public void Complete()
    {
        var owner = _owner;
        if (owner == null)
        {
            return;
        }

        _owner = null;
        owner.CompleteDeferral();
    }

    public void Dispose() => Complete();
}

/// <summary>
/// Represents a top-level window.
/// </summary>
public partial class Window : ContentControl, ILayoutRoundingHost
{
    private readonly DispatcherMergeKey _updatePassMergeKey = new(DispatcherPriority.Layout);
    private readonly DispatcherMergeKey _renderMergeKey = new(DispatcherPriority.Render);

    private enum WindowLifetimeState
    {
        New,
        Shown,
        Hidden,
        Closed,
    }

    // Monotonic teardown phase owned by the close coordinator. Backends drive the native destroy
    // between transitions; the core enforces that each teardown step runs once and in order.
    private enum WindowClosePhase
    {
        Live,
        Closed,
        GraphicsReleased,
        VisualsDisposed,
    }

    private const double DefaultWidth = 800;
    private const double DefaultHeight = 600;

    private IWindowBackend? _backend;
    private WindowRenderTarget? _cachedRenderTarget;
    private IGraphicsContext? _renderContext;
    // True while RenderFrame is inside RenderFrameCore; guards against reentrant paints.
    private bool _renderFrameActive;
    private Action? _cachedInvalidateBackend;
    private Action? _cachedUpdatePass;
    private LayoutPerformanceStats _lastLayoutPerformanceStats;

    // Update-pass scheduler: the generation counts every RequestUpdatePass arrival, a pass converges
    // when one measure/arrange round completes without the generation moving. Element dirty flags
    // say where work is; they are never used as a convergence criterion (overlay chrome and hidden
    // elements stay legitimately dirty forever).
    private ulong _updateGeneration;
    private ulong _layoutCompletedGeneration = ulong.MaxValue;
    private int _updatePassDepth;

    // A settled pass recorded its generation; an unsettled pass leaves it behind and posts
    // exactly one continuation.
    internal bool IsUpdatePassSettled => _updateGeneration == _layoutCompletedGeneration;

    // Sizing transaction (spec -> desired -> requested -> applied): the fit branch submits a
    // target once per change and accepts whatever client size the platform applies.
    private Size _requestedClientSize;
    private bool _hasRequestedClientSize;

    private Size _clientSizeDip = new(DefaultWidth, DefaultHeight);
    private Size _lastLayoutClientSizeDip = Size.Empty;
    private Thickness _lastLayoutPadding = Thickness.Zero;
    private Element? _lastLayoutContent;

    private Element? _hostedPortalRoot;

    // Layout, render, hit test, and propagation must all follow the same visual root:
    // the hosted portal root when this window is a surface for an externally-owned subtree
    // (native popup), else the template root when a template is applied, otherwise the content.
    // Internal: platform backends (caption hit testing, input guards) must use this
    // instead of Content, which is the logical user content only.
    internal Element? EffectiveVisualRoot => _hostedPortalRoot ?? TemplateVisualRoot ?? Content;

    /// <summary>
    /// Sets an externally-owned subtree for this window to lay out, render, and hit-test as its visual root
    /// without adopting it (its <c>Parent</c> stays with the true owner window). Used by the native popup host
    /// so a popup window is only a surface: resolution, DPI, and visual root of the content stay with the owner.
    /// </summary>
    internal void SetHostedPortalRoot(Element? root) => _hostedPortalRoot = root;

    private Point _hostedPortalOrigin;
    private Point _lastLayoutPortalOrigin;

    /// <summary>
    /// Position (in the owner window's coordinate space) where the hosted portal subtree is arranged.
    /// The subtree keeps the owner's coordinate space so owner-relative logic (placement from element
    /// bounds, screen conversion through the owner) stays correct in both hosting modes; this surface
    /// translates by the origin at its render and input edges instead.
    /// </summary>
    internal Point HostedPortalOrigin
    {
        get => _hostedPortalOrigin;
        set
        {
            if (_hostedPortalOrigin != value)
            {
                _hostedPortalOrigin = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Maps a surface-local point into the coordinate space the visual tree is arranged in.
    /// Identity unless this window hosts a portal subtree arranged at <see cref="HostedPortalOrigin"/>.
    /// </summary>
    internal Point SurfacePointToVisualTree(Point surfacePoint)
    {
        if (_hostedPortalRoot == null)
        {
            return surfacePoint;
        }

        return new Point(surfacePoint.X + _hostedPortalOrigin.X, surfacePoint.Y + _hostedPortalOrigin.Y);
    }

    private readonly List<AdornerEntry> _adorners = new();
    private readonly RadioGroupManager _radioGroups = new();
    private readonly List<Window> _ownedChildren = new();
    private readonly List<Window> _modalChildren = new();
    private readonly List<UIElement> _mouseOverOldPath = new(capacity: 16);
    private readonly List<UIElement> _mouseOverNewPath = new(capacity: 16);
    private readonly List<UIElement> _visualStateDirtyList = new();
    private UIElement? _mouseOverElement;
    private UIElement? _capturedElement;
    private Point _lastMousePositionDip;
    private Point _lastMouseScreenPositionPx;
    private bool _loadedRaised;
    private bool _firstFrameRenderedRaised;
    private bool _firstFrameRenderedPending;
    private bool _subscribedToDispatcherChanged;
    private bool _buildHookRan;
    private WindowLifetimeState _lifetimeState;
    private WindowClosePhase _closePhase;
    private int _modalDisableCount;
    private bool _isDialogWindow;
    private IGpuInteropInvalidationSource? _gpuInvalidationSource;

    /// <summary>
    /// Gets the window backend (internal use only, e.g. for IME mode switching from controls).
    /// </summary>
    internal IWindowBackend? Backend => _backend;

    internal Action<Window>? BuildCallback { get; private set; }

    // Set by the hot reload registry when an OnBuild override was registered; lets the reload
    // and preview paths pick the virtual hook as the rebuild owner without reflection.
    internal bool HasBuildHookRegistered { get; set; }

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
    /// until an element with a non-null cursor is found, then applies it via the backend.
    /// </summary>
    internal void UpdateCursorForElement(UIElement? element)
    {
        CursorType? cursor = null;
        for (var current = element; current != null; current = current.Parent as UIElement)
        {
            var c = current.Cursor;
            if (c.HasValue)
            {
                cursor = c;
                break;
            }
        }

        // The element chain can be empty (no hit-test target under the pointer, e.g. a kiosk background)
        // or never override the cursor, so fall back to the window's own cursor (which may be
        // CursorType.None to hide it), then to the platform arrow default. A resolved
        // CursorType.None means "hide the cursor".
        cursor ??= Cursor;
        _backend?.SetCursor(cursor ?? CursorType.Arrow);
    }

    internal void UpdateLastMousePosition(Point positionDip, Point screenPositionPx)
    {
        _lastMousePositionDip = positionDip;
        _lastMouseScreenPositionPx = screenPositionPx;
#if DEBUG
        InvalidateInspectorOverlayIfHoverChanged();
#endif
    }

    internal void ReevaluateMouseOver()
    {
        ApplicationDispatcher?.BeginInvoke(DispatcherPriority.Layout, () =>
        {
            // When layout/scroll offsets change without an actual mouse move, the element under the cursor can change.
            // Re-run hit testing at the last known mouse position to keep IsMouseOver state accurate.
            // Use a real hit test: mouse-over must track the pointer's actual target even during capture.
            var leaf = HitTest(_lastMousePositionDip);
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
        InitializeBitmapCacheDiagnostics();

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

            if (previous.IsResizable != field.IsResizable)
            {
                _backend?.SetResizable(field.IsResizable);
                CoerceValue(CanMaximizeProperty);
            }

            if (_backend != null && (!double.IsNaN(field.Width) || !double.IsNaN(field.Height)))
            {
                // Push the spec'd axes; a fit axis (NaN) keeps its current value until the fit
                // branch submits its content-derived target.
                _backend.SetClientSize(
                    double.IsNaN(field.Width) ? _clientSizeDip.Width : field.Width,
                    double.IsNaN(field.Height) ? _clientSizeDip.Height : field.Height);
            }

            // A new spec starts a new sizing transaction: the fit branch must re-submit.
            _hasRequestedClientSize = false;
            RequestUpdatePass();
        }
    } = WindowSize.Resizable(DefaultWidth, DefaultHeight);

    public static readonly MewProperty<string> TitleProperty =
        MewProperty<string>.Register<Window>(nameof(Title), "Window", MewPropertyOptions.None,
            static (self, _, _) => self.OnTitleChanged());

    public static readonly MewProperty<IconSource?> IconProperty =
        MewProperty<IconSource?>.Register<Window>(nameof(Icon), null, MewPropertyOptions.None,
            static (self, _, _) => self.OnIconChanged());

    public static readonly MewProperty<WindowStartupLocation> StartupLocationProperty =
        MewProperty<WindowStartupLocation>.Register<Window>(nameof(StartupLocation), WindowStartupLocation.CenterScreen, MewPropertyOptions.None);

    public static readonly MewProperty<double> OpacityProperty =
        MewProperty<double>.Register<Window>(nameof(Opacity), 1.0, MewPropertyOptions.None,
            static (self, _, _) => self.OnOpacityChanged(),
            static (_, value) => Math.Clamp(value, 0.0, 1.0));

    public static readonly MewProperty<bool> AllowsTransparencyProperty =
        MewProperty<bool>.Register<Window>(nameof(AllowsTransparency), false, MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnAllowsTransparencyChanged());

    public static readonly MewProperty<double> ExtendClientAreaTitleBarHeightProperty =
        MewProperty<double>.Register<Window>(nameof(ExtendClientAreaTitleBarHeight), 0.0, MewPropertyOptions.None,
            static (self, _, _) => self.OnExtendClientAreaChanged());

    public static readonly MewProperty<bool> BorderlessProperty =
        MewProperty<bool>.Register<Window>(nameof(Borderless), false, MewPropertyOptions.None,
            static (self, _, _) => self.OnBorderlessChanged());

    public static readonly MewProperty<PlatformWindowOptions?> PlatformOptionsProperty =
        MewProperty<PlatformWindowOptions?>.Register<Window>(nameof(PlatformOptions), null, MewPropertyOptions.None,
            static (self, _, _) => self._backend?.SetPlatformOptions(self.PlatformOptions));

    public static readonly MewProperty<bool> UseLayoutRoundingProperty =
        MewProperty<bool>.Register<Window>(nameof(UseLayoutRounding), true, MewPropertyOptions.None);

    private static readonly MewPropertyKey<bool> IsActivePropertyKey =
        MewProperty<bool>.RegisterReadOnly<Window>(nameof(IsActive), false, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> IsActiveProperty = IsActivePropertyKey.Property;

    public static readonly MewProperty<WindowState> WindowStateProperty =
        MewProperty<WindowState>.Register<Window>(nameof(WindowState), WindowState.Normal, MewPropertyOptions.None,
            static (self, _, newValue) => self.OnWindowStateChanged(newValue));

    public static readonly MewProperty<bool> CanMinimizeProperty =
        MewProperty<bool>.Register<Window>(nameof(CanMinimize), true, MewPropertyOptions.None,
            static (self, _, _) => self._backend?.SetCanMinimize(self.CanMinimize));

    public static readonly MewProperty<bool> CanMaximizeProperty =
        MewProperty<bool>.Register<Window>(nameof(CanMaximize), true, MewPropertyOptions.None,
            static (self, _, _) => self._backend?.SetCanMaximize(self.CanMaximize),
            static (self, value) => value && self.WindowSize.IsResizable);

    public static readonly MewProperty<bool> CanCloseProperty =
        MewProperty<bool>.Register<Window>(nameof(CanClose), true, MewPropertyOptions.None,
            static (self, _, _) => self._backend?.SetCanClose(self.CanClose));

    public static readonly MewProperty<bool> TopmostProperty =
        MewProperty<bool>.Register<Window>(nameof(Topmost), false, MewPropertyOptions.None,
            static (self, _, _) => self._backend?.SetTopmost(self.Topmost));

    public static readonly MewProperty<bool> ShowInTaskbarProperty =
        MewProperty<bool>.Register<Window>(nameof(ShowInTaskbar), true, MewPropertyOptions.None,
            static (self, _, _) => self._backend?.SetShowInTaskbar(self.ShowInTaskbar));

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
    /// Hint for platform backends to use alert-panel animation (e.g. macOS bounce).
    /// </summary>
    internal bool IsAlertWindow { get; set; }

    /// <summary>
    /// Gets or sets whether this is a floating tool/utility window: a thin native title bar with move and
    /// close only (no minimize/maximize), excluded from the taskbar, floating above its <see cref="Owner"/>.
    /// Must be set BEFORE <see cref="Show(Window?)"/> (the native window is built from it); setting it
    /// after the window is shown throws. Ignored when <see cref="AllowsTransparency"/> is true (which removes
    /// the chrome entirely).
    /// </summary>
    public bool IsToolWindow
    {
        get => _kind == WindowKind.Tool;
        set
        {
            if (_backend is not null)
            {
                throw new InvalidOperationException("IsToolWindow must be set before the window is shown.");
            }
            if (value)
            {
                _kind = WindowKind.Tool;
            }
            else if (_kind == WindowKind.Tool)
            {
                _kind = WindowKind.Normal;
            }
        }
    }

    private WindowKind _kind;

    /// <summary>
    /// The mutually exclusive surface role of this window: framework surfaces (popup, overlay) set it
    /// at construction, tool windows via <see cref="IsToolWindow"/>. Backends branch on this for
    /// creation class, style mask, and show path; cumulative traits (dialog, alert) stay separate flags.
    /// </summary>
    internal WindowKind Kind
    {
        get => _kind;
        init => _kind = value;
    }

    /// <summary>
    /// Whether this window is a non-activating surface (popup or overlay): it must never take native
    /// activation/key/main status away from its owner. Backends consult this at every decision point
    /// that can activate a window - show command, mouse activation, programmatic move/resize/style
    /// refresh, zoom/state sync, and close path. The decision-point checklist lives in
    /// agent/popup-native-window/plan.md.
    /// </summary>
    internal bool IsNonActivatingSurface => _kind is WindowKind.Popup or WindowKind.Overlay;

    /// <summary>
    /// Whether this window is a chrome-less surface: no native title bar, border, shadow, or close
    /// affordance - the framework draws everything. Backends consult this where OS chrome machinery
    /// would otherwise engage (style mask selection, close path that requires a close affordance).
    /// </summary>
    internal bool UsesBorderlessSurfaceChrome => _kind is WindowKind.Popup or WindowKind.Overlay;

    /// <summary>
    /// The opaque color a non-transparent window clears to: the user-set <see cref="Control.Background"/> when it
    /// is opaque, otherwise the themed window background. Backends use this to seed the native window background so
    /// the surface is filled on map instead of showing through until the first paint.
    /// </summary>
    internal Color EffectiveOpaqueBackground => Background.A > 0 ? Background : Theme.Palette.WindowBackground;

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
        set => SetValue(OpacityProperty, value);
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
    /// Gets or sets the height of the custom title bar area (in DIPs).
    /// When set to a value greater than 0, the client area extends into the title bar,
    /// hiding the default title bar while preserving the native frame (rounded corners, shadow).
    /// Set to 0 to restore the default title bar.
    /// </summary>
    public double ExtendClientAreaTitleBarHeight
    {
        get => GetValue(ExtendClientAreaTitleBarHeightProperty);
        set => SetValue(ExtendClientAreaTitleBarHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the entire native non-client area (title bar and border) is removed.
    /// Independent of <see cref="WindowState"/> and preserved across fullscreen transitions.
    /// A borderless window has no native resize/move grips; use <see cref="DragMove"/> / <see cref="DragResize"/>.
    /// </summary>
    public bool Borderless
    {
        get => GetValue(BorderlessProperty);
        set => SetValue(BorderlessProperty, value);
    }

    /// <summary>
    /// Gets or sets the platform-specific window options.
    /// Setting options for a mismatched platform throws
    /// <see cref="InvalidOperationException"/> at backend attach time.
    /// </summary>
    public PlatformWindowOptions? PlatformOptions
    {
        get => GetValue(PlatformOptionsProperty);
        set => SetValue(PlatformOptionsProperty, value);
    }

    /// <summary>
    /// Gets the native window chrome capabilities supported by the current platform.
    /// </summary>
    public WindowChromeCapabilities ChromeCapabilities => _backend?.ChromeCapabilities ?? WindowChromeCapabilities.None;

    /// <summary>
    /// Gets whether the platform provides native chrome buttons (close, minimize, maximize)
    /// when <see cref="ExtendClientAreaTitleBarHeight"/> is active.
    /// </summary>
    public bool HasNativeChromeButtons => ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeChromeButtons);

    /// <summary>
    /// Gets the reserved area (in DIPs) for native chrome buttons.
    /// Use as margin/padding on the title bar to avoid overlapping native buttons.
    /// </summary>
    public Thickness NativeChromeButtonInset => _backend?.NativeChromeButtonInset ?? default;

    /// <summary>
    /// Sets the native window border color (Win11+). Use null to restore default.
    /// </summary>
    public void SetWindowBorderColor(Color? color) => _backend?.SetWindowBorderColor(color);

    private void OnTitleChanged() => _backend?.SetTitle(Title);

    private void OnIconChanged() => _backend?.SetIcon(Icon);

    private void OnOpacityChanged() => _backend?.SetOpacity(Opacity);

    private void OnAllowsTransparencyChanged() => _backend?.SetAllowsTransparency(AllowsTransparency);

    private void OnExtendClientAreaChanged() => _backend?.SetExtendClientAreaToTitleBar(ExtendClientAreaTitleBarHeight);

    private void OnBorderlessChanged() => _backend?.SetBorderless(Borderless);

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
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        private set => SetValue(IsActivePropertyKey, value);
    }

    /// <summary>
    /// Gets or sets the window display state.
    /// </summary>
    public WindowState WindowState
    {
        get => GetValue(WindowStateProperty);
        set => SetValue(WindowStateProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the minimize button is enabled. Default is true.
    /// </summary>
    public bool CanMinimize
    {
        get => GetValue(CanMinimizeProperty);
        set => SetValue(CanMinimizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the maximize button is enabled. Default is true.
    /// </summary>
    public bool CanMaximize
    {
        get => GetValue(CanMaximizeProperty);
        set => SetValue(CanMaximizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the window can be closed. Default is true.
    /// When false, the close button in the native chrome is disabled.
    /// </summary>
    public bool CanClose
    {
        get => GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the window stays on top of other windows.
    /// </summary>
    public bool Topmost
    {
        get => GetValue(TopmostProperty);
        set => SetValue(TopmostProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the window appears in the taskbar.
    /// </summary>
    public bool ShowInTaskbar
    {
        get => GetValue(ShowInTaskbarProperty);
        set => SetValue(ShowInTaskbarProperty, value);
    }

    /// <summary>
    /// Gets the window bounds before it was minimized or maximized.
    /// </summary>
    public Rect RestoreBounds { get; private set; }

    /// <summary>Minimizes the window.</summary>
    public void Minimize() => WindowState = WindowState.Minimized;

    /// <summary>Maximizes the window.</summary>
    public void Maximize() => WindowState = WindowState.Maximized;

    /// <summary>Restores the window to its normal state.</summary>
    public void Restore() => WindowState = WindowState.Normal;

    /// <summary>
    /// Initiates a window drag move using the platform's native mechanism.
    /// Call this from a mouse down handler on a custom title bar element.
    /// </summary>
    public void DragMove() => _backend?.BeginDragMove();

    /// <summary>
    /// Initiates a window resize from the specified edge using the platform's native mechanism.
    /// </summary>
    public void DragResize(ResizeEdge edge) => _backend?.BeginDragResize(edge);

    private bool _windowStateFromBackend;

    private void OnWindowStateChanged(WindowState newState)
    {
        if (newState != WindowState.Normal && !_windowStateFromBackend)
        {
            RestoreBounds = new Rect(Position.X, Position.Y, ClientSize.Width, ClientSize.Height);
        }

        if (!_windowStateFromBackend)
            _backend?.SetWindowState(newState);

        // Force WM_NCCALCSIZE recalculation when using extended client area,
        // so the maximized frame compensation is applied/removed.
        if (ExtendClientAreaTitleBarHeight > 0)
            _backend?.SetExtendClientAreaToTitleBar(ExtendClientAreaTitleBarHeight);

        WindowStateChanged?.Invoke(newState);
        RequerySuggested();
    }

    /// <summary>
    /// Called by backend when the window state changes externally (e.g. user drags from maximized, taskbar minimize).
    /// </summary>
    internal void SetWindowStateFromBackend(WindowState state)
    {
        if (state == WindowState) return;

        if (state != WindowState.Normal && WindowState == WindowState.Normal)
        {
            RestoreBounds = new Rect(Position.X, Position.Y, ClientSize.Width, ClientSize.Height);
        }

        _windowStateFromBackend = true;
        try
        {
            SetValue(WindowStateProperty, state);
        }
        finally
        {
            _windowStateFromBackend = false;
        }
    }

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
    /// Centers this window on its owner window. No-op if there is no owner.
    /// </summary>
    public void CenterOnOwner()
    {
        if (_backend == null || Handle == 0 || Owner == null)
            return;

        _backend.CenterOnOwner();
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

    internal static readonly MewProperty<bool> ShowAccessKeysProperty =
        MewProperty<bool>.Register<Window>("ShowAccessKeys", false,
            MewPropertyOptions.Inherits | MewPropertyOptions.AffectsRender);

    internal AccessKeyManager AccessKeyManager => field ??= new AccessKeyManager(this);

    internal bool ShowAccessKeys
    {
        get => GetValue(ShowAccessKeysProperty);
        set => SetValue(ShowAccessKeysProperty, value);
    }

    /// <summary>
    /// Gets the list of global keyboard shortcuts for this window.
    /// Bindings are checked after bubbling (so control-level shortcuts like TextBox Ctrl+C take priority).
    /// </summary>
    public List<KeyBinding> KeyBindings { get; } = new();

    /// <summary>
    /// Processes global key bindings. Called after bubbling if the event is still unhandled.
    /// </summary>
    internal void ProcessKeyBindings(KeyEventArgs e)
    {
        if (e.Handled) return;

        for (int i = 0; i < KeyBindings.Count; i++)
        {
            if (KeyBindings[i].TryHandle(e))
                return;
        }
    }

    internal void ProcessAccessKeyDown(KeyEventArgs e) => AccessKeyManager.OnKeyDown(e);

    internal void ProcessAccessKeyUp(KeyEventArgs e) => AccessKeyManager.OnKeyUp(e);

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
    /// Occurs when the window is about to close. Set <see cref="ClosingEventArgs.Cancel"/> to <c>true</c> to prevent closing.
    /// </summary>
    public event Action<ClosingEventArgs>? Closing;

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
    /// Raised when <see cref="WindowState"/> changes.
    /// </summary>
    public event Action<WindowState>? WindowStateChanged;

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

    /// <summary>
    /// Raised before the framework processes a native platform message (Win32 WM_, X11 XEvent, macOS NSEvent).
    /// Set <see cref="NativeMessageEventArgs.Handled"/> to suppress default processing.
    /// Cast the argument to the platform-specific subclass to access raw message data.
    /// </summary>
    public event Action<NativeMessageEventArgs>? NativeMessage;

    #endregion

    internal bool HasNativeMessageHandler => NativeMessage is not null;

    internal bool RaiseNativeMessage(NativeMessageEventArgs args)
    {
        NativeMessage?.Invoke(args);
        return args.Handled;
    }

    internal void RaisePreviewKeyDown(KeyEventArgs e) => PreviewKeyDown?.Invoke(e);

    internal void RaisePreviewKeyUp(KeyEventArgs e) => PreviewKeyUp?.Invoke(e);

    internal void RaisePreviewTextInput(TextInputEventArgs e) => PreviewTextInput?.Invoke(e);

    internal void RaisePreviewTextCompositionStart(TextCompositionEventArgs e) => PreviewTextCompositionStart?.Invoke(e);

    internal void RaisePreviewTextCompositionUpdate(TextCompositionEventArgs e) => PreviewTextCompositionUpdate?.Invoke(e);

    internal void RaisePreviewTextCompositionEnd(TextCompositionEventArgs e) => PreviewTextCompositionEnd?.Invoke(e);

    internal void RaiseActivated() => Activated?.Invoke();

    internal void RaiseDeactivated()
    {
        // Close non-stays-open popups (ContextMenu, ComboBox dropdown, ToolTip, etc.)
        _popupManager.RequestClosePopups(PopupCloseRequest.Explicit());

        Deactivated?.Invoke();
    }

    /// <summary>
    /// Called by the backend when this window is a popup surface and its platform dismiss watch
    /// (mouse capture / pointer grab / event monitor) saw a press outside the popup.
    /// </summary>
    internal virtual void OnPopupSurfaceOutsidePress() { }

    /// <summary>
    /// Called by the backend when this popup surface lost its dismiss watch to another window.
    /// Returns true when the new holder is a related popup surface (submenu chain) and the loss
    /// must not dismiss.
    /// </summary>
    internal virtual bool OnPopupSurfaceWatchTransfer(nint newHolderHandle) => false;

    /// <summary>
    /// Whether pointer input intercepted by this popup surface's dismiss watch (a press or a move) may
    /// be handed to the window with the given native handle to act there normally: the owner window or a
    /// sibling popup surface of the same owner. This is the popup input domain membership test, so the
    /// owner and the active popup chain behave as one input surface. Presses over unrelated windows
    /// light-dismiss instead; moves over them are ignored.
    /// </summary>
    internal virtual bool IsPopupInputForwardTarget(nint windowHandle) => false;

    /// <summary>
    /// Re-arms the platform dismiss watch (raw backend mouse capture) for a popup surface without
    /// routing input to a captured element. No-op when the backend is absent.
    /// </summary>
    internal void RecapturePopupSurface() => _backend?.CaptureMouse();

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

        // Before the backend exists, so OnBuild-set specs (WindowSize, transparency) apply to
        // surface creation.
        RunBuildHookBeforeShow();

        EnsureBackend();
        Application.Current.RegisterWindow(this);

        if (_lifetimeState == WindowLifetimeState.Shown)
        {
            return;
        }

        ResolveStartupPosition();
        _backend!.EnsureTheme(Theme.IsDark);

        // Unified display sequence, identical on every backend (see agent/window-lifecycle/plan.md):
        //   1) CreateSurface   create hidden, Handle/DPI valid
        //   2) PerformLayout   confirm size (unconditional, so step 4 always sees a laid-out tree even
        //                      when step 3 defers Loaded)
        //   3) RaiseLoaded     lay out -> Loaded handlers -> lay out again (flush deferred changes)
        //   4) PresentSurface  paint the hidden window, then reveal it (no flash; Loaded changes are in
        //                      the first on-screen frame)
        _backend!.CreateSurface();
        PerformLayout();
        RaiseLoadedIfReady();
        _backend!.PresentSurface();

        // Re-apply after reveal for platforms (macOS) where window chrome appearance
        // may reset when the window is first ordered on screen.
        _backend!.EnsureTheme(Theme.IsDark);
        _lifetimeState = WindowLifetimeState.Shown;

        // Native ownership suppresses the child's own taskbar button on Windows. Keep the
        // framework owner for positioning/lifetime, but only native-own auxiliary windows
        // that opted out of the taskbar. The window handle exists only after the surface is
        // created (Win32 additionally applies the owner at creation itself).
        if (owner != null && !ShowInTaskbar && owner.Handle != 0 && Handle != 0)
        {
            _backend!.SetOwner(owner.Handle);
        }
    }

    // Raises Loaded once, and only after the application's dispatcher is ready. A window shown before
    // the dispatcher exists subscribes and raises later; its first paint then precedes Loaded (fallback).
    private void RaiseLoadedIfReady()
    {
        if (_loadedRaised || !Application.IsRunning)
        {
            return;
        }

        if (Application.Current.Dispatcher != null)
        {
            RaiseLoaded();
        }
        else
        {
            SubscribeToDispatcherChanged();
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

    // Async close state: result observed by CloseAsync, plus the deferral round flags.
    private TaskCompletionSource<bool>? _pendingCloseResult;
    private bool _closeApproved;
    private bool _closeDecisionPending;

    /// <summary>
    /// Closes the window.
    /// </summary>
    public void Close()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
            return;

        if (_backend == null)
        {
            if (!RequestClose())
                return;
            RaiseClosed();
            return;
        }

        _backend.Close();
    }

    /// <summary>
    /// Requests a close and completes with <see langword="true"/> when the window closed,
    /// or <see langword="false"/> when a <see cref="Closing"/> handler cancelled it.
    /// </summary>
    public Task<bool> CloseAsync()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
            return Task.FromResult(true);

        _pendingCloseResult ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = _pendingCloseResult.Task;
        Close();
        return task;
    }

    /// <summary>
    /// Raises the <see cref="Closing"/> event and returns true if close is allowed, false if cancelled
    /// or still pending on a deferral. Does not call <see cref="RaiseClosed"/> - the caller is
    /// responsible for proceeding with close.
    /// </summary>
    internal bool RequestClose()
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
            return true;

        if (_closeApproved)
        {
            // A deferred Closing round already allowed this close; don't raise Closing again.
            _closeApproved = false;
            return true;
        }

        if (_closeDecisionPending)
        {
            // A deferred Closing decision is in flight; this request joins it.
            return false;
        }

        if (Closing == null)
            return true;

        var args = new ClosingEventArgs();
        Closing.Invoke(args);

        if (args.IsDecisionPending)
        {
            _closeDecisionPending = true;
            args.AttachDecisionResolved(() => ResolveCloseDecision(args));
            return false;
        }

        if (args.Cancel)
        {
            CompletePendingCloseResult(false);
            return false;
        }

        return true;
    }

    private void ResolveCloseDecision(ClosingEventArgs args)
    {
        _closeDecisionPending = false;

        // The window can be destroyed externally while the decision was pending.
        if (_lifetimeState == WindowLifetimeState.Closed)
            return;

        if (args.Cancel)
        {
            CompletePendingCloseResult(false);
        }
        else
        {
            _closeApproved = true;
            Close();
        }
    }

    private void CompletePendingCloseResult(bool closed)
    {
        var pending = _pendingCloseResult;
        _pendingCloseResult = null;
        pending?.TrySetResult(closed);
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

        owner ??= ResolveDefaultOwner();

        if (owner != null && ReferenceEquals(owner, this))
        {
            throw new ArgumentException("Owner cannot be the dialog itself.", nameof(owner));
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnClosed()
        {
            Closed -= OnClosed;
            EndModal(owner);
            tcs.TrySetResult();
        }

        Closed += OnClosed;

        try
        {
            BeginModal(owner);
        }
        catch (Exception ex)
        {
            Closed -= OnClosed;
            EndModal(owner);
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Shows the window as a modal dialog and blocks the caller until it is closed, running a nested
    /// message loop so input, rendering, timers, and animation stay responsive. Requires a running
    /// application loop and must be called on the UI thread; otherwise use <see cref="ShowDialogAsync"/>.
    /// </summary>
    /// <param name="owner">Optional owner window to disable while the dialog is open.</param>
    public void ShowDialog(Window? owner = null)
    {
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            throw new InvalidOperationException("Cannot show a closed window.");
        }

        if (!Application.IsRunning)
        {
            throw new InvalidOperationException("ShowDialog requires a running application loop. Use Application.Run, or call ShowDialogAsync.");
        }

        owner ??= ResolveDefaultOwner();

        if (owner != null && ReferenceEquals(owner, this))
        {
            throw new ArgumentException("Owner cannot be the dialog itself.", nameof(owner));
        }

        // Release modal state from the Closed event, which fires before the native window is destroyed, so the
        // owner is re-enabled/activated *before* destruction. Doing it after the loop (post-destroy) makes Win32
        // hand the foreground elsewhere for a frame, flickering the top-level window. The finally is an
        // idempotent safety net for early loop exit (e.g. Application.Quit before the dialog closed).
        bool modalEnded = false;
        void EndModalOnce()
        {
            if (modalEnded)
            {
                return;
            }
            modalEnded = true;
            Closed -= OnClosed;
            EndModal(owner);
        }
        void OnClosed() => EndModalOnce();

        Closed += OnClosed;

        try
        {
            BeginModal(owner);
            Application.Current.PlatformHost.RunNestedLoop(() => _lifetimeState != WindowLifetimeState.Closed);
        }
        finally
        {
            EndModalOnce();
        }
    }

    /// <summary>
    /// Applies modal state and shows the window: marks it as a dialog, disables and parents to the owner,
    /// inherits the owner icon, then shows and activates. Shared by <see cref="ShowDialog"/> and <see cref="ShowDialogAsync"/>.
    /// </summary>
    private void BeginModal(Window? owner)
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
        Activate();
    }

    /// <summary>
    /// Releases modal state acquired by <see cref="BeginModal"/>: re-enables the owner, unregisters this dialog,
    /// and reactivates the next topmost modal (or the owner). Safe to call even when setup failed partway.
    /// </summary>
    private void EndModal(Window? owner)
    {
        if (owner == null)
        {
            return;
        }

        owner.ReleaseModalDisable();
        owner.UnregisterModalChild(this);
        if (owner._lifetimeState != WindowLifetimeState.Closed)
        {
            var target = owner.GetTopModalChild() ?? owner;
            target.Activate();
        }
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

    private Window? ResolveDefaultOwner()
    {
        if (!Application.IsRunning)
        {
            return null;
        }

        var windows = Application.Current.AllWindows;
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (!ReferenceEquals(w, this) && w.IsActive)
            {
                return w;
            }
        }

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (!ReferenceEquals(w, this) && w.Handle != 0)
            {
                return w;
            }
        }

        return null;
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

    // Internal so platform services (e.g. the X11 portal file dialog) can make a native dialog modal by
    // disabling the owner window for the dialog's duration, mirroring what Win32 does via the owner HWND.
    internal void AcquireModalDisable()
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

    internal void ReleaseModalDisable()
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
        if (ExtendClientAreaTitleBarHeight > 0)
            _backend.SetExtendClientAreaToTitleBar(ExtendClientAreaTitleBarHeight);
    }

    /// <summary>
    /// Queues <paramref name="element"/> for visual-state reconciliation at the next layout pass.
    /// Called by <see cref="UIElement.InvalidateVisualState"/>.
    /// </summary>
    internal void RegisterVisualStateDirty(UIElement element)
    {
        // Registrations while UpdateVisualStates is running are allowed: its indexed loop
        // re-reads Count, so entries appended mid-pass (e.g. a resolve dirtying a named part)
        // are reconciled in the same pass.
        _visualStateDirtyList.Add(element);
    }

    /// <summary>
    /// Reconciles visual states for all elements that called <see cref="UIElement.InvalidateVisualState"/>
    /// since the last update. Offscreen elements snap (no animation); onscreen elements animate.
    /// </summary>
    public void UpdateVisualStates()
    {
        if (_visualStateDirtyList.Count == 0)
        {
            return;
        }

        var viewport = new Rect(ClientSize);

        for (int i = 0; i < _visualStateDirtyList.Count; i++)
        {
            var element = _visualStateDirtyList[i];
            element.ClearVisualStateDirty();

            // Skip elements that got detached before the update (visual root no longer this window).
            if (element.FindVisualRoot() != this)
            {
                continue;
            }

            // Offscreen: snap to avoid wasting animations on invisible pixels.
            // SkipViewportCull elements (e.g. transformed subtrees) always animate since their
            // bounds don't reflect true visibility.
            bool onscreen = element.SkipViewportCull || viewport.IntersectsWith(element.Bounds);
            element.ResolveVisualStateInternal(snap: !onscreen);
        }

        _visualStateDirtyList.Clear();
    }

    /// <summary>
    /// Runs the update pass for the window content: reconciles queued visual-state
    /// invalidations, then performs measure/arrange when layout is dirty.
    /// </summary>
    public void PerformLayout()
    {
        if (Handle == 0)
        {
            return;
        }

        if (_updatePassDepth > 0)
        {
            // Synchronous re-entry: Win32 delivers WM_SIZE inside SetWindowPos while a pass is
            // running, and its handler calls back into PerformLayout. The applied client size is
            // already recorded; signal the outer pass to re-converge instead of nesting a layout.
            _updateGeneration++;
            return;
        }

        _updatePassDepth++;
        try
        {
            PerformLayoutCore();
        }
        finally
        {
            _updatePassDepth--;
        }
    }

    private void PerformLayoutCore()
    {
        var profiler = PerformanceProfiler.Instance;
        bool profiling = profiler.IsEnabled;
        long layoutStart = profiling ? Stopwatch.GetTimestamp() : 0;
        long measureTicks = 0;
        long arrangeTicks = 0;
        bool measureRan = false;
        bool arrangeRan = false;
        using var layoutScope = profiling ? ProfilerMarkers.WindowLayout.Auto() : default;

        // Reconcile queued visual-state invalidations before layout reads state-dependent
        // properties (e.g. a style trigger may adjust size/padding based on IsEnabled).
        using (profiling ? ProfilerMarkers.VisualStateUpdate.Auto() : default)
        {
            UpdateVisualStates();
        }

        // Window is the visual root - it has no Parent, so OnVisualRootChanged never fires.
        // Resolve its style here before reading layout-affecting properties like Padding.
        using (profiling ? ProfilerMarkers.StyleResolve.Auto() : default)
        {
            EnsureStyleResolved();
        }

        // Window bypasses MeasureOverride, so the template lifecycle runs here.
        ApplyTemplate();

        var visualRoot = EffectiveVisualRoot;
        if (visualRoot == null)
        {
            return;
        }

        var padding = Padding;
        var mode = WindowSize.Mode;

        // For FitContent modes, measure content with per-axis constraints first (max on fit axes,
        // the spec'd size on fixed axes), then resize the window to the content's desired size.
        if (mode is WindowSizeMode.FitContentWidth or WindowSizeMode.FitContentHeight or WindowSizeMode.FitContentSize)
        {
            var spec = WindowSize;
            var measureWidth = (mode is WindowSizeMode.FitContentWidth or WindowSizeMode.FitContentSize
                ? spec.MaxWidth
                : spec.Width) - padding.HorizontalThickness;
            var measureHeight = (mode is WindowSizeMode.FitContentHeight or WindowSizeMode.FitContentSize
                ? spec.MaxHeight
                : spec.Height) - padding.VerticalThickness;

            long measureStart = profiling ? Stopwatch.GetTimestamp() : 0;
            using (profiling ? ProfilerMarkers.ContentMeasure.Auto() : default)
            {
                visualRoot.Measure(new Size(Math.Max(0, measureWidth), Math.Max(0, measureHeight)));
            }
            if (profiling)
            {
                measureTicks += Stopwatch.GetTimestamp() - measureStart;
            }
            measureRan = true;

            var desired = visualRoot.DesiredSize;
            var fitWidth = mode is WindowSizeMode.FitContentWidth or WindowSizeMode.FitContentSize
                ? Math.Min(desired.Width + padding.HorizontalThickness, spec.MaxWidth)
                : spec.Width;
            var fitHeight = mode is WindowSizeMode.FitContentHeight or WindowSizeMode.FitContentSize
                ? Math.Min(desired.Height + padding.VerticalThickness, spec.MaxHeight)
                : spec.Height;

            // A zero-size client cannot back a render surface; keep at least one pixel per axis.
            fitWidth = Math.Max(fitWidth, 1);
            fitHeight = Math.Max(fitHeight, 1);

            // Snap to pixel boundaries to avoid fractional DIP sizes that cause
            // mismatches between the view backing size and the rendering surface.
            double dpiScale = DpiScale;
            if (dpiScale > 0)
            {
                fitWidth = Math.Ceiling(fitWidth * dpiScale) / dpiScale;
                fitHeight = Math.Ceiling(fitHeight * dpiScale) / dpiScale;
            }

            // Submit only when the target changes, and accept whatever client size the platform
            // applies - possibly clamped to an OS minimum. The fit contract is
            // max(content, OS minimum); a clamped result is never re-fought (issue #199).
            var target = new Size(fitWidth, fitHeight);
            if (!_hasRequestedClientSize || target != _requestedClientSize)
            {
                _hasRequestedClientSize = true;
                _requestedClientSize = target;
                _backend?.SetClientSize(target.Width, target.Height);
            }
        }

        var clientSize = _clientSizeDip;

        // Cheap per-paint path: nothing arrived since the last completed pass and the inputs are
        // unchanged, so measure/arrange would be a no-op tree walk.
        if (clientSize == _lastLayoutClientSizeDip &&
            padding == _lastLayoutPadding &&
            visualRoot == _lastLayoutContent &&
            _hostedPortalOrigin == _lastLayoutPortalOrigin &&
            _updateGeneration == _layoutCompletedGeneration)
        {
            if (profiling)
            {
                _lastLayoutPerformanceStats = new LayoutPerformanceStats(
                    FrameTimingBuilder.ToMilliseconds(Stopwatch.GetTimestamp() - layoutStart),
                    FrameTimingBuilder.ToMilliseconds(measureTicks),
                    FrameTimingBuilder.ToMilliseconds(arrangeTicks),
                    layoutRan: false,
                    measureRan,
                    arrangeRan);
            }
            return;
        }

        const int maxPasses = 8;
        bool converged = false;
        ulong cleanGeneration = 0;
        // Populated only in the final rounds of a run that is failing to settle, so a non-convergence
        // report can show which elements keep re-invalidating across passes rather than a lone final
        // snapshot. A layout that converges in the usual one or two passes never allocates this.
        List<string>? passDiagnostics = null;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            // Re-read per round: a synchronous WM_SIZE in the previous round may have recorded a
            // new applied client size, and a mid-round style change may have altered Padding.
            clientSize = _clientSizeDip;
            padding = Padding;
            var contentSize = clientSize.Deflate(padding);

            ulong generationBefore = _updateGeneration;

            long measureStart = profiling ? Stopwatch.GetTimestamp() : 0;
            using (profiling ? ProfilerMarkers.ContentMeasure.Auto() : default)
            {
                // A clean tree under an unchanged constraint returns immediately; no dirty gate needed.
                visualRoot.Measure(contentSize);
            }
            if (profiling)
            {
                measureTicks += Stopwatch.GetTimestamp() - measureStart;
            }
            measureRan = true;

            long arrangeStart = profiling ? Stopwatch.GetTimestamp() : 0;
            using (profiling ? ProfilerMarkers.ContentArrange.Auto() : default)
            {
                // A hosted portal subtree is arranged at the owner's coordinates (HostedPortalOrigin);
                // for regular windows the origin is zero and this is the plain client rect.
                visualRoot.Arrange(new Rect(
                    padding.Left + _hostedPortalOrigin.X,
                    padding.Top + _hostedPortalOrigin.Y,
                    contentSize.Width,
                    contentSize.Height));
            }
            if (profiling)
            {
                arrangeTicks += Stopwatch.GetTimestamp() - arrangeStart;
            }
            arrangeRan = true;

            if (_updateGeneration == generationBefore)
            {
                converged = true;
                cleanGeneration = generationBefore;
                break;
            }

            if (pass >= maxPasses - 2)
            {
                (passDiagnostics ??= new List<string>()).Add(
                    $"pass {pass}: {DescribeDirtyElements(visualRoot)}");
            }

            // Consume visual-state invalidations that arrived mid-round before the next round
            // reads state-dependent properties.
            UpdateVisualStates();
        }

        _lastLayoutClientSizeDip = clientSize;
        _lastLayoutPadding = padding;
        _lastLayoutContent = visualRoot;
        _lastLayoutPortalOrigin = _hostedPortalOrigin;

        using (profiling ? ProfilerMarkers.OverlayLayout.Auto() : default)
        {
            LayoutAdorners();
            LayoutPopups();
            OverlayLayer.Layout(clientSize);
        }

        if (converged && _updateGeneration == cleanGeneration)
        {
            _layoutCompletedGeneration = _updateGeneration;
        }
        else
        {
            // Unsettled: the pass budget ran out, or overlay layout invalidated again. Hand the
            // dispatcher exactly one continuation; chaining passes from inside is what spun (#199).
            if (!converged)
            {
                LogNonConvergedLayout(visualRoot, passDiagnostics);
            }

            PostUpdatePass();
        }

        if (profiling)
        {
            _lastLayoutPerformanceStats = new LayoutPerformanceStats(
                FrameTimingBuilder.ToMilliseconds(Stopwatch.GetTimestamp() - layoutStart),
                FrameTimingBuilder.ToMilliseconds(measureTicks),
                FrameTimingBuilder.ToMilliseconds(arrangeTicks),
                layoutRan: true,
                measureRan,
                arrangeRan);
        }
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
            // Window is the root element and is never arranged by a parent, so its Bounds
            // stays at (0,0,0,0). Use the client size rect when the adorned element is this window.
            var bounds = ReferenceEquals(adorned, this)
                ? new Rect(0, 0, _clientSizeDip.Width, _clientSizeDip.Height)
                : adorned.Bounds;
            adorner.Measure(new Size(bounds.Width, bounds.Height));
            adorner.Arrange(bounds);
        }
    }

    private void LayoutPopups()
    {
        _popupManager.LayoutDirtyPopups();
    }

    // Diagnostic only - runs when the convergence loop exhausts its pass budget. Dirty flags are
    // not a convergence criterion (overlay chrome and hidden elements stay legitimately dirty).
    [Conditional("DEBUG")]
    private static void LogNonConvergedLayout(Element root, List<string>? passDiagnostics)
    {
        Debug.WriteLine("[MewUI] layout did not converge within the pass budget");
        if (passDiagnostics != null)
        {
            foreach (var pass in passDiagnostics)
            {
                Debug.WriteLine($"[MewUI]   {pass}");
            }
        }

        Debug.WriteLine($"[MewUI]   still dirty: {DescribeDirtyElements(root)}");
    }

    // Lists the still-dirty elements with their ancestor path so a non-convergence report identifies
    // the re-invalidating subtree. Bounded so a large thrashing tree stays readable.
    private static string DescribeDirtyElements(Element root)
    {
        var builder = new StringBuilder();
        int count = 0;
        VisualTree.Visit(root, e =>
        {
            if (count >= 12 || (!e.IsMeasureDirty && !e.IsArrangeDirty))
            {
                return;
            }

            if (count > 0)
            {
                builder.Append("; ");
            }

            builder.Append(DescribeElementPath(e));
            if (e.IsMeasureDirty && e.IsArrangeDirty)
            {
                builder.Append(" (measure,arrange)");
            }
            else if (e.IsMeasureDirty)
            {
                builder.Append(" (measure)");
            }
            else
            {
                builder.Append(" (arrange)");
            }

            count++;
        });

        return count == 0 ? "none" : builder.ToString();
    }

    private static string DescribeElementPath(Element element)
    {
        var builder = new StringBuilder(element.GetType().Name);
        int depth = 0;
        for (Element? parent = element.Parent; parent != null && depth < 4; parent = parent.Parent, depth++)
        {
            builder.Append('<').Append(parent.GetType().Name);
        }

        return builder.ToString();
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
        RequestUpdatePass();
    }

    /// <summary>
    /// Invalidates arrangement and schedules an update pass.
    /// </summary>
    public override void InvalidateArrange()
    {
        base.InvalidateArrange();
        RequestUpdatePass();
    }

    /// <summary>
    /// Schedules the pre-render update pass: visual-state update, then measure/arrange when
    /// layout is dirty, then a render. Both layout invalidation and visual-state invalidation
    /// funnel here - the visual-state update is the first step of the pass, not a separate
    /// pipeline stage.
    /// </summary>
    internal void RequestUpdatePass()
    {
        _updateGeneration++;
        if (_updatePassDepth > 0)
        {
            // The running pass owns pass-internal invalidation: the generation bump above is the
            // arrival signal its convergence loop (or end-of-pass continuation) consumes. Posting
            // here would spin - the dispatcher releases the merge key before execution, so a
            // mid-pass post becomes a fresh work item instead of merging (issue #199).
            return;
        }

        PostUpdatePass();
    }

    private void PostUpdatePass()
    {
        var dispatcher = ApplicationDispatcher;
        if (dispatcher == null)
        {
            // Fallback: we have no UI dispatcher yet; rely on immediate invalidation.
            InvalidateBackend();
            return;
        }

        _cachedUpdatePass ??= () =>
        {
            PerformLayout();
            RequestRender();
        };
        (dispatcher as IDispatcherCore)?.PostMerged(_updatePassMergeKey, _cachedUpdatePass, DispatcherPriority.Layout);
    }

    internal void RequestRender()
    {
        var dispatcher = ApplicationDispatcher;
        if (dispatcher == null)
        {
            InvalidateBackend();
            return;
        }

        _cachedInvalidateBackend ??= InvalidateBackend;
        (dispatcher as IDispatcherCore)?.PostMerged(_renderMergeKey, _cachedInvalidateBackend, DispatcherPriority.Render);
    }

    internal bool SetFocusedElement(UIElement element) => FocusManager.SetFocus(element);

    public void RequerySuggested()
    {
        if (EffectiveVisualRoot == null)
        {
            return;
        }

        VisitVisualTree(EffectiveVisualRoot, e =>
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
        if (_lifetimeState == WindowLifetimeState.Closed)
        {
            return;
        }

        // Content hosted in a native popup window reaches this via its owner (FindVisualRoot returns
        // the owner in the portal model), but its input flows through the popup surface: capture there
        // instead so drag events route to the element and the owner's dismiss watch is not disturbed.
        var inputHost = element.ResolveInputHostWindow();
        if (inputHost != null && !ReferenceEquals(inputHost, this))
        {
            _captureDelegatedTo = inputHost;
            inputHost.CaptureMouse(element);
            return;
        }

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

    // The popup surface a capture was delegated to, so a later ReleaseMouseCapture on this owner window
    // (callers resolve capture/release symmetrically through FindVisualRoot) reaches the same surface.
    private Window? _captureDelegatedTo;

    /// <summary>
    /// Releases any active mouse capture for this window.
    /// </summary>
    public void ReleaseMouseCapture()
    {
        if (_captureDelegatedTo is Window delegated)
        {
            _captureDelegatedTo = null;
            delegated.ReleaseMouseCapture();
            return;
        }

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
        if (ExtendClientAreaTitleBarHeight > 0)
            _backend.SetExtendClientAreaToTitleBar(ExtendClientAreaTitleBarHeight);
        if (Borderless)
            _backend.SetBorderless(true);
        if (!double.IsNaN(WindowSize.Width) || !double.IsNaN(WindowSize.Height))
        {
            // Push the spec'd axes; a fit axis (NaN) keeps its current value until the fit
            // branch submits its content-derived target.
            _backend.SetClientSize(
                double.IsNaN(WindowSize.Width) ? _clientSizeDip.Width : WindowSize.Width,
                double.IsNaN(WindowSize.Height) ? _clientSizeDip.Height : WindowSize.Height);
        }

        // A fresh backend starts a fresh sizing transaction.
        _hasRequestedClientSize = false;
        if (Topmost)
            _backend.SetTopmost(true);
        if (!ShowInTaskbar)
            _backend.SetShowInTaskbar(false);
        if (!CanMinimize)
            _backend.SetCanMinimize(false);
        if (!CanMaximize)
            _backend.SetCanMaximize(false);
        if (!CanClose)
            _backend.SetCanClose(false);
        if (PlatformOptions != null)
            _backend.SetPlatformOptions(PlatformOptions);
        if (AllowDrop)
            _backend.SetAllowDrop(true);
    }

    internal void ReleaseWindowGraphicsResources(nint windowHandle)
    {
        if (_closePhase >= WindowClosePhase.GraphicsReleased)
        {
            return;
        }

        _closePhase = WindowClosePhase.GraphicsReleased;

        if (windowHandle == 0)
        {
            return;
        }

        // Release parked vector-cache surfaces (tied to this device) before the context goes away so
        // their deferred GPU disposal drains under a still-valid context.
        DisposeVectorSurfaceReclaimer();

        // Dispose the cached render context BEFORE the factory tears down its window
        // resources - backends may still hold references that the factory is about to free.
        _renderContext?.Dispose();
        _renderContext = null;
        _cachedRenderTarget = null;

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

        SubscribeGpuInteropInvalidation();

        // Lay out first so Loaded handlers observe an arranged tree (Bounds are valid), matching WPF.
        PerformLayout();
        Loaded?.Invoke();

        // Loaded handlers commonly set content or bindings that were deferred until load (e.g. filling
        // a label). Re-run layout so the arranged tree already reflects those changes before the first
        // paint (which every backend performs in PresentSurface, after this returns). Cheap no-op when
        // Loaded changed nothing (PerformLayout early-outs on a clean tree).
        PerformLayout();

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
                try { ownedChildren[i]?.Close(); }
                catch (Exception ex) { Application.RouteLifecycleException(ex); }
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
                try { children[i]?.Close(); }
                catch (Exception ex) { Application.RouteLifecycleException(ex); }
            }
        }

        _lifetimeState = WindowLifetimeState.Closed;
        _closePhase = WindowClosePhase.Closed;
        _closeApproved = false;
        _closeDecisionPending = false;
        CompletePendingCloseResult(true);
        UnsubscribeFromDispatcherChanged();
        UnsubscribeGpuInteropInvalidation();

        if (Application.IsRunning)
        {
            Application.Current.UnregisterWindow(this);
        }

        Closed?.Invoke();
    }


    /// <summary>
    /// Monotonic counter bumped whenever the backend reports GPU device/target invalidation
    /// (device lost, render-target device change, display change). Render caches compare against
    /// this to discard offscreen surfaces built on a now-invalid device and rebuild them.
    /// </summary>
    internal int DeviceGeneration { get; private set; }

    private void SubscribeGpuInteropInvalidation()
    {
        if (_gpuInvalidationSource != null)
        {
            return;
        }

        if (GraphicsFactory is IGpuInteropInvalidationSource source)
        {
            _gpuInvalidationSource = source;
            source.GpuInteropInvalidated += OnGpuInteropInvalidated;
        }
    }

    private void UnsubscribeGpuInteropInvalidation()
    {
        if (_gpuInvalidationSource != null)
        {
            _gpuInvalidationSource.GpuInteropInvalidated -= OnGpuInteropInvalidated;
            _gpuInvalidationSource = null;
        }
    }

    private void OnGpuInteropInvalidated(object? sender, GpuInteropInvalidatedEventArgs e)
    {
        // A new device generation invalidates every render cache built on the old device.
        DeviceGeneration++;
        InvalidateVisual();
    }

    internal void RaiseClientSizeChanged(double widthDip, double heightDip) => ClientSizeChanged?.Invoke(new Size(widthDip, heightDip));

    internal void RenderFrame(IWindowSurface surface)
    {
        // Reentrant paint (e.g. a cross-thread sent WM_PAINT dispatched while this frame is
        // still open, as with a window hosted in another process's tree) would nest
        // BeginFrame on the cached context and corrupt the backend's begin/end pairing.
        // Skip and repaint on the next dispatcher cycle instead.
        if (_renderFrameActive)
        {
            RequestRender();
            return;
        }

        // Some platforms can render before Loaded is raised due to Run/Show/Dispatcher ordering.
        // Ensure Loaded is raised as soon as the dispatcher is available, and always before FirstFrameRendered.
        if (!_loadedRaised && Application.IsRunning && Application.Current.Dispatcher != null)
        {
            RaiseLoaded();
        }

        ArgumentNullException.ThrowIfNull(surface);
        var clientSize = _clientSizeDip;
        var target = _cachedRenderTarget;
        if (target == null || !target.TryUpdateSurface(surface))
        {
            // Surface or pixel size changed - cached context references stale handles.
            _renderContext?.Dispose();
            _renderContext = null;
            target = new WindowRenderTarget(surface);
            _cachedRenderTarget = target;
        }

        _renderFrameActive = true;
        try
        {
            RenderFrameCore(target, clientSize);
        }
        finally
        {
            _renderFrameActive = false;
        }
    }

    internal void RenderFrameToSurface(IRenderSurface surface)
    {
        if (surface == null)
        {
            throw new ArgumentNullException(nameof(surface));
        }

        var clientSizeDip = new Size(
            surface.PixelWidth / Math.Max(1.0, surface.DpiScale),
            surface.PixelHeight / Math.Max(1.0, surface.DpiScale));

        RenderFrameCore(surface, clientSizeDip);
    }

    private void RenderFrameCore(IRenderTarget target, Size clientSize)
    {
        var profiler = PerformanceProfiler.Instance;
        var frameTiming = _excludeFromProfiler ? default : profiler.BeginFrame(_profilerSourceId);

        // Update animations before rendering so controls see current values.
        long phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
        using (frameTiming.Enabled ? ProfilerMarkers.AnimationUpdate.Auto() : default)
        {
            AnimationManager.Instance.Update();
        }
        if (frameTiming.Enabled)
        {
            frameTiming.AnimationTicks += Stopwatch.GetTimestamp() - phaseStart;
        }

        // Render surfaces are one-shot (different target instance per call).
        // Window-targeted contexts are cached so backends can pool per-frame state.
        bool oneShot = target is IRenderSurface;
        IGraphicsContext context = oneShot
            ? GraphicsFactory.CreateContext(target)
            : (_renderContext ??= GraphicsFactory.CreateContext(target));

        try
        {
            phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
            using (frameTiming.Enabled ? ProfilerMarkers.BeginFrame.Auto() : default)
            {
                context.BeginFrame(target);
            }
            if (frameTiming.Enabled)
            {
                frameTiming.BeginFrameTicks += Stopwatch.GetTimestamp() - phaseStart;
            }

            Color clearColor;
            if (AllowsTransparency)
            {
                // Layered windows use premultiplied alpha compositing.
                clearColor = Color.Transparent;
            }
            else
            {
                // Default to an opaque window background when the user does not specify one.
                clearColor = EffectiveOpaqueBackground;
            }

            phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
            using (frameTiming.Enabled ? ProfilerMarkers.Clear.Auto() : default)
            {
                context.Clear(clearColor);

                if (AllowsTransparency && Background.A > 0)
                {
                    // Draw the background through the normal pipeline so alpha is handled consistently.
                    context.FillRectangle(new Rect(0, 0, clientSize.Width, clientSize.Height), Background);
                }
            }
            if (frameTiming.Enabled)
            {
                frameTiming.RenderBodyTicks += Stopwatch.GetTimestamp() - phaseStart;
            }

            // Cull viewport in layout coordinates: this window's client rect, offset into the owner's
            // coordinate space when hosting a portal subtree so popup content that lies outside the
            // owner but inside this surface is not culled by the viewport-bounds check in Render.
            var previousCullViewport = UIElement.RenderCullViewport;
            UIElement.RenderCullViewport = new Rect(
                _hostedPortalOrigin.X, _hostedPortalOrigin.Y, clientSize.Width, clientSize.Height);

            // Ensure nothing paints outside the client area.
            context.Save();
            // Clip should not shrink due to edge rounding; snap outward to avoid 1px clipping at non-100% DPI.
            context.SetClip(LayoutRounding.SnapViewportRectToPixels(new Rect(0, 0, clientSize.Width, clientSize.Height), DpiScale));

            try
            {
                phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
                using (frameTiming.Enabled ? ProfilerMarkers.ContentRender.Auto() : default)
                {
                    if (_hostedPortalRoot != null && _hostedPortalOrigin != default)
                    {
                        // The portal subtree is arranged in the owner's coordinate space; shift it back
                        // to this surface's origin for painting.
                        context.Save();
                        context.Translate(-_hostedPortalOrigin.X, -_hostedPortalOrigin.Y);
                        _hostedPortalRoot.Render(context);
                        context.Restore();
                    }
                    else
                    {
                        EffectiveVisualRoot?.Render(context);
                    }
                }
                if (frameTiming.Enabled)
                {
                    frameTiming.RenderBodyTicks += Stopwatch.GetTimestamp() - phaseStart;
                }

                phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
                using (frameTiming.Enabled ? ProfilerMarkers.AdornerRender.Auto() : default)
                {
                    for (int i = 0; i < _adorners.Count; i++)
                    {
                        var adorner = _adorners[i].Element;
#if DEBUG
                        if (ReferenceEquals(adorner, _performanceAdorner))
                        {
                            continue;
                        }
#endif

                        adorner.Render(context);
                    }
                }
                if (frameTiming.Enabled)
                {
                    frameTiming.RenderBodyTicks += Stopwatch.GetTimestamp() - phaseStart;
                }

                phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
                using (frameTiming.Enabled ? ProfilerMarkers.PopupRender.Auto() : default)
                {
                    _popupManager.Render(context);
                }
                if (frameTiming.Enabled)
                {
                    frameTiming.RenderBodyTicks += Stopwatch.GetTimestamp() - phaseStart;
                }

                phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
                using (frameTiming.Enabled ? ProfilerMarkers.OverlayRender.Auto() : default)
                {
                    OverlayLayer.Render(context);
                }
                if (frameTiming.Enabled)
                {
                    frameTiming.RenderBodyTicks += Stopwatch.GetTimestamp() - phaseStart;
                }

#if DEBUG
                if (_performanceAdorner != null)
                {
                    phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
                    using (frameTiming.Enabled ? ProfilerMarkers.DevToolsRender.Auto() : default)
                    {
                        _performanceAdorner.Render(context);
                    }
                    if (frameTiming.Enabled)
                    {
                        frameTiming.DevToolsTicks += Stopwatch.GetTimestamp() - phaseStart;
                    }
                }
#endif
            }
            finally
            {
                context.Restore();
                UIElement.RenderCullViewport = previousCullViewport;
            }

            if (context is GraphicsContextBase gcb)
                LastFrameStats = new RenderStats(gcb.DrawCallCount, gcb.CullCount, gcb.PrimitiveStats);
        }
        finally
        {
            // EndFrame must run even if rendering throws so backend GPU/COM state is closed.
            // For oneShot contexts, Dispose must also run to return pooled collections.
            phaseStart = frameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
            try
            {
                bool presentWait = Application.IsRunning && Application.Current.RenderLoopSettings.VSyncEnabled;
                using (frameTiming.Enabled ? (presentWait ? ProfilerMarkers.Present.Auto() : ProfilerMarkers.EndFrame.Auto()) : default)
                {
                    context.EndFrame();
                }
            }
            finally { if (oneShot) context.Dispose(); }
            if (frameTiming.Enabled)
            {
                frameTiming.EndFrameTicks += Stopwatch.GetTimestamp() - phaseStart;
                if (Application.IsRunning && Application.Current.RenderLoopSettings.VSyncEnabled)
                {
                    frameTiming.PresentTicks += frameTiming.EndFrameTicks;
                    frameTiming.EndFrameTicks = 0;
                }
            }
        }

        profiler.CommitFrame(ref frameTiming, _lastLayoutPerformanceStats, LastFrameStats.DrawCalls, LastFrameStats.CullCount, LastFrameStats.PrimitiveStats);
        LastFramePerformanceStats = profiler.LatestFrame;

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

    internal void SetBuildCallback(Action<Window> callback, Delegate buildSource)
    {
        BuildCallback = callback;
        // Register the original user build delegate (not the wrapping callback) so Hot Reload
        // detects edits to the actual build lambda.
        HotReload.HotReloadRegistry.RegisterBuild(this, buildSource);
    }

    /// <summary>
    /// Builds this window's content and configuration. Runs once before the first
    /// <see cref="Show"/> when no composition-site build callback owns the build; override in
    /// subclasses instead of calling the fluent Build extension from the constructor.
    /// </summary>
    protected virtual void OnBuild()
    {
    }

    internal void RunBuildHookBeforeShow()
    {
        if (_buildHookRan)
        {
            return;
        }
        _buildHookRan = true;

        // Build ownership: a composition-site callback owns the build; the virtual hook only
        // runs when no callback was set (mirrors UserControl, where external Content wins).
        if (BuildCallback != null)
        {
            return;
        }

        HotReload.HotReloadRegistry.RegisterWindow(this);
        OnBuild();
    }

    /// <summary>Re-runs the virtual build hook for hot reload / preview rebuilds.</summary>
    internal void InvokeOnBuildHook() => OnBuild();

    /// <summary>
    /// Development-only misuse check: a window that overrides <see cref="OnBuild"/> owns its
    /// build, so attaching a composition-site build callback would silently shadow it.
    /// Compiled out of release builds, so no reflection reaches AOT.
    /// </summary>
    [Conditional("DEBUG")]
    internal void GuardBuildOwnership()
    {
        Action probe = OnBuild;
        if (probe.Method.DeclaringType != typeof(Window))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} overrides OnBuild(), so the type owns its build. " +
                "Use fluent property setters for composition-site tweaks instead of Build(...).");
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
        if (_closePhase >= WindowClosePhase.VisualsDisposed)
        {
            return;
        }

        _closePhase = WindowClosePhase.VisualsDisposed;

        var visualRoot = EffectiveVisualRoot;
        if (visualRoot == null)
        {
            DisposeAdorners();
            _popupManager.Dispose();
            return;
        }

        VisualTree.Visit(visualRoot, element =>
        {
            if (element is IDisposable disposable)
            {
                disposable.Dispose();
            }
        });

        // A templated window may hold logical content that no presenter projected;
        // it lives outside the visual root and must still be disposed.
        var content = Content;
        if (content != null && content.Parent == null)
        {
            VisualTree.Visit(content, element =>
            {
                if (element is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            });
        }

        OverlayLayer.Dispose();

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

        if (EffectiveVisualRoot != null)
        {
            VisitVisualTree(EffectiveVisualRoot, e =>
            {
                if (e is FrameworkElement c)
                {
                    c.NotifyThemeChanged(oldTheme, newTheme);
                }
            });
        }

        OverlayLayer.NotifyThemeChanged(oldTheme, newTheme);

        _popupManager.NotifyThemeChanged(oldTheme, newTheme);

        var adorners = _adorners.ToArray();
        for (int i = 0; i < adorners.Length; i++)
        {
            if (adorners[i].Element is FrameworkElement fe)
            {
                fe.NotifyThemeChanged(oldTheme, newTheme);
            }
        }

        ThemeChanged?.Invoke(oldTheme, newTheme);
    }

    internal static void VisitVisualTree(Element element, Action<Element> visitor) => VisualTree.Visit(element, visitor);

    internal void RaiseDpiChanged(uint oldDpi, uint newDpi)
    {
        OnDpiChanged(oldDpi, newDpi);
        DpiChanged?.Invoke(oldDpi, newDpi);

        if (EffectiveVisualRoot != null)
        {
            // Clear cached DPI values so subsequent GetDpi() calls don't traverse parents.
            // This also ensures subtrees moved between windows/tabs don't retain stale DPI.
            VisitVisualTree(EffectiveVisualRoot, e => e.ClearDpiCache());

            VisitVisualTree(EffectiveVisualRoot, e =>
            {
                if (e is FrameworkElement fe)
                {
                    fe.NotifyDpiChanged(oldDpi, newDpi);
                }
            });
        }

        OverlayLayer.NotifyDpiChanged(oldDpi, newDpi);
        _popupManager.NotifyDpiChanged(oldDpi, newDpi);

        for (int i = 0; i < _adorners.Count; i++)
        {
            if (_adorners[i].Element is FrameworkElement fe)
            {
                fe.NotifyDpiChanged(oldDpi, newDpi);
            }

            _adorners[i].Element.ClearDpiCacheDeep();
        }
    }

    internal void CloseAllPopups()
        => _popupManager.CloseAllPopups();

    internal Rect ShowPopup(UIElement owner, UIElement popup, Rect bounds, bool sizeToContent = false, bool staysOpen = false)
        => _popupManager.ShowPopup(owner, popup, bounds, sizeToContent, staysOpen);

    /// <summary>
    /// Opens a popup whose placement is measured only after it is rooted and style-resolved in this
    /// window. Use this overload when the placement math depends on the popup's own measured size.
    /// </summary>
    internal Rect ShowPopup(UIElement owner, UIElement popup, Func<Window, Rect> measureBounds, bool sizeToContent = false, bool staysOpen = false)
        => _popupManager.ShowPopup(owner, popup, measureBounds, sizeToContent, staysOpen);

    internal void RequestClosePopups(PopupCloseRequest request)
        => _popupManager.RequestClosePopups(request);

    /// <summary>
    /// The region, in this window's client coordinates, that popup placement math may use. In-surface
    /// popups are confined to the client area; natively hosted popups may use the work area of the
    /// monitor containing <paramref name="anchorBounds"/>, so they can extend beyond this window.
    /// Falls back to the client area when the platform cannot report a work area.
    /// </summary>
    internal Rect GetPopupPlacementRegion(Rect anchorBounds)
    {
        var clientRegion = new Rect(0, 0, ClientSize.Width, ClientSize.Height);
        if (!PopupManager.PreferNativePopups || _backend == null || Handle == 0 || !Application.IsRunning)
        {
            return clientRegion;
        }

        var anchorCenterPx = ClientToScreen(new Point(
            anchorBounds.X + (anchorBounds.Width / 2),
            anchorBounds.Y + (anchorBounds.Height / 2)));
        var workAreaPx = Application.Current.PlatformHost.GetWorkAreaForPoint(anchorCenterPx);
        if (workAreaPx.Width <= 0 || workAreaPx.Height <= 0)
        {
            return clientRegion;
        }

        var topLeft = ScreenToClient(new Point(workAreaPx.X, workAreaPx.Y));
        var bottomRight = ScreenToClient(new Point(workAreaPx.Right, workAreaPx.Bottom));
        return new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    internal Size MeasureToolTip(Element content, Size availableSize)
        => _popupManager.MeasureToolTip(content, availableSize);

    internal void ShowToolTip(UIElement owner, Element content, Rect bounds)
        => _popupManager.ShowToolTip(owner, content, bounds);

    internal void CloseToolTip(UIElement? owner = null)
        => _popupManager.CloseToolTip(owner);

    internal bool TryGetPopupOwner(UIElement popup, out UIElement owner)
        => _popupManager.TryGetPopupOwner(popup, out owner);

    internal bool TryGetEnclosingPopup(UIElement element, out UIElement popupRoot)
        => _popupManager.TryGetEnclosingPopup(element, out popupRoot);

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

    internal void CancelImeComposition() => _backend?.CancelImeComposition();

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

        return (EffectiveVisualRoot as UIElement)?.HitTest(point);
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

        RequestUpdatePass();
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
                RequestUpdatePass();
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
            RequestUpdatePass();
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
        RequestUpdatePass();
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
