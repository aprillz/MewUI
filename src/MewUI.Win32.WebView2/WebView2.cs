using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

[SupportedOSPlatform("windows")]
public sealed partial class WebView2 : FrameworkElement
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int GWLP_WNDPROC = -4;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_PARENTNOTIFY = 0x0210;

    private static readonly nint HWND_TOP = 0;

    private WebViewInfo? _webViewInfo;
    private Task<IComObject<ICoreWebView2>?>? _loadingWebView2;
    private ComObject<ICoreWebView2Controller>? _controller;
    private ComObject<ICoreWebView2>? _webView2;
    private EventRegistrationToken _navigationStarting;
    private EventRegistrationToken _navigationCompleted;
    private EventRegistrationToken _documentTitleChanged;
    private EventRegistrationToken _newWindowRequested;
    private EventRegistrationToken _frameNavigationCompleted;
    private EventRegistrationToken _webMessageReceived;
    private EventRegistrationToken _sourceChanged;
    private EventRegistrationToken _contentLoading;
    private EventRegistrationToken _zoomFactorChanged;

    private nint _hostHandle;
    private nint _hostWndProcPrev;
    private bool _disposed;
    private string? _browserExecutableFolder;
    private string? _userDataFolder;
    private Rect _lastArrangedBounds;

    private Rendering.IFont? _font;
    private WebViewEnvironmentOptions? _options;

    private static readonly ConcurrentDictionary<nint, WebView2> s_hostMap = new();
    private static readonly WndProcDelegate s_hostWndProc = HostWndProc;
    private static readonly nint s_hostWndProcPtr = Marshal.GetFunctionPointerForDelegate(s_hostWndProc);

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    // Treat WebView2 as a focusable element so keyboard navigation can enter the embedded browser.
    public override bool Focusable => true;

    /// <summary>
    /// Occurs when navigation has completed.
    /// </summary>
    public event Action<WebViewNavigationCompletedEventArgs>? NavigationCompleted;

    /// <summary>
    /// Occurs when navigation starts.
    /// </summary>
    public event Action<WebViewNavigationStartingEventArgs>? NavigationStarting;

    /// <summary>
    /// Occurs when frame navigation has completed.
    /// </summary>
    public event Action<WebViewNavigationCompletedEventArgs>? FrameNavigationCompleted;

    /// <summary>
    /// Occurs when a new window is requested.
    /// </summary>
    public event Action<WebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    /// <summary>
    /// Occurs when the document title changes.
    /// </summary>
    public event Action<string>? DocumentTitleChanged;

    /// <summary>
    /// Occurs when the CoreWebView2 initialization has completed.
    /// </summary>
    public event Action<WebViewInitializationCompletedEventArgs>? CoreWebView2InitializationCompleted;

    /// <summary>
    /// Occurs when a message is received from the web content.
    /// </summary>
    public event Action<WebViewWebMessageReceivedEventArgs>? WebMessageReceived;

    /// <summary>
    /// Occurs when the Source property changes.
    /// </summary>
    public event Action<WebViewSourceChangedEventArgs>? SourceChanged;

    /// <summary>
    /// Occurs when content starts loading.
    /// </summary>
    public event Action<WebViewContentLoadingEventArgs>? ContentLoading;

    /// <summary>
    /// Occurs when the ZoomFactor property changes.
    /// </summary>
    public event Action? ZoomFactorChanged;

    public static bool UseSharedEnvironmentDefault { get; set; } = true;

    /// <summary>
    /// Gets or sets the URI of the top-level document.
    /// </summary>
    public Uri? Source
    {
        get => CoreWebView2?.Source;
        set
        {
            if (Source == value)
            {
                return;
            }

            _ = NavigateAsync(value);
        }
    }

    /// <summary>
    /// Gets the title of the current document.
    /// </summary>
    public string? DocumentTitle => CoreWebView2?.DocumentTitle;

    /// <summary>
    /// Gets a value indicating whether the WebView can navigate backward.
    /// </summary>
    public bool CanGoBack => CoreWebView2?.CanGoBack ?? false;

    /// <summary>
    /// Gets a value indicating whether the WebView can navigate forward.
    /// </summary>
    public bool CanGoForward => CoreWebView2?.CanGoForward ?? false;

    /// <summary>
    /// Gets or sets the zoom factor for the WebView.
    /// </summary>
    public double ZoomFactor
    {
        get
        {
            var controller = _controller;
            if (controller == null || controller.IsDisposed)
            {
                return 1.0;
            }

            double zoomFactor = 1.0;
            controller.Object.get_ZoomFactor(ref zoomFactor);
            return zoomFactor;
        }
        set
        {
            var controller = _controller;
            if (controller == null || controller.IsDisposed)
            {
                return;
            }

            controller.Object.put_ZoomFactor(value);
        }
    }


    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        // WebView2 is hosted in a native child HWND. When this element is focused at the MewUI level,
        // transfer Win32 focus to the host so keyboard input is routed to the browser.
        if (_hostHandle != 0)
        {
            Interop.SetFocus(_hostHandle);
        }

        // If controller is ready, ask the WebView to move focus to a reasonable element.
        var controller = _controller;
        if (controller != null && !controller.IsDisposed)
        {
            try
            {
                controller.Object.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON.COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
            }
            catch
            {
                // Best-effort: not all controller implementations expose MoveFocus.
            }
        }
    }

    public bool UseSharedEnvironment
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            WebViewInfo.ThrowIfInitialized(_webViewInfo);
            field = value;
        }
    } = UseSharedEnvironmentDefault;

    public string? BrowserExecutableFolder
    {
        get => _browserExecutableFolder;
        set
        {
            if (_browserExecutableFolder == value)
            {
                return;
            }

            WebViewInfo.ThrowIfInitialized(_webViewInfo);
            _browserExecutableFolder = value;
        }
    }

    public string? UserDataFolder
    {
        get => _userDataFolder;
        set
        {
            if (_userDataFolder == value)
            {
                return;
            }

            WebViewInfo.ThrowIfInitialized(_webViewInfo);
            _userDataFolder = value;
        }
    }

    public WebViewEnvironmentOptions? Options
    {
        get => _options;
        set
        {
            if (_options == value)
            {
                return;
            }

            WebViewInfo.ThrowIfInitialized(_webViewInfo);
            _options = value;
        }
    }

    /// <summary>
    /// Gets or sets the controller options used when creating the WebView2 controller.
    /// Must be set before the controller is created.
    /// </summary>
    public WebViewControllerOptions? ControllerOptions
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            if (_controller != null || _loadingWebView2 != null)
            {
                throw new InvalidOperationException($"{nameof(ControllerOptions)} cannot be set after WebView2 Controller is created.");
            }

            field = value;
        }
    }

    public bool IsInitialized => _webView2 != null && !_webView2.IsDisposed;

    /// <summary>
    /// Gets the CoreWebView2 instance associated with this WebView2 control.
    /// This property is null until initialization is complete.
    /// </summary>
    public CoreWebView2? CoreWebView2 { get; private set; }

    /// <summary>
    /// Gets the underlying CoreWebView2 COM object for internal use.
    /// </summary>
    internal ICoreWebView2? CoreWebView2Internal => CoreWebView2?.ComObject;

    /// <summary>
    /// Reloads the current document.
    /// </summary>
    public void Reload() => CoreWebView2?.Reload();

    /// <summary>
    /// Navigates to the previous page in the navigation history.
    /// </summary>
    public void GoBack() => CoreWebView2?.GoBack();

    /// <summary>
    /// Navigates to the next page in the navigation history.
    /// </summary>
    public void GoForward() => CoreWebView2?.GoForward();

    /// <summary>
    /// Stops any in-progress navigation.
    /// </summary>
    public void Stop() => CoreWebView2?.Stop();

    /// <summary>
    /// Navigates to the specified HTML content.
    /// </summary>
    /// <param name="htmlContent">The HTML content to display.</param>
    public void NavigateToString(string htmlContent)
    {
        _ = NavigateToStringAsync(htmlContent);
    }

    public async Task NavigateToStringAsync(string htmlContent)
    {
        if (_disposed)
        {
            return;
        }

        await EnsureWebView2LoadedAsync();
        var coreWebView2 = CoreWebView2;
        if (coreWebView2 == null || coreWebView2.IsDisposed)
        {
            return;
        }

        Source = null;

        if (!string.IsNullOrEmpty(htmlContent))
        {
            coreWebView2.NavigateToString(htmlContent);
            return;
        }

        coreWebView2.Navigate("about:blank");
    }

    /// <summary>
    /// Ensures that the CoreWebView2 is initialized.
    /// </summary>
    /// <returns>A task that completes when initialization is complete.</returns>
    public Task EnsureCoreWebView2Async()
    {
        return EnsureWebView2LoadedAsync();
    }

    /// <summary>
    /// Executes JavaScript code in the WebView.
    /// </summary>
    /// <param name="script">The JavaScript code to execute.</param>
    /// <returns>The result of the script execution as a JSON string.</returns>
    public Task<string?> ExecuteScriptAsync(string script)
        => CoreWebView2?.ExecuteScriptAsync(script) ?? Task.FromResult<string?>(null);

    /// <summary>
    /// Posts a message to the web content as a JSON string.
    /// </summary>
    /// <param name="webMessageAsJson">The message to post as JSON.</param>
    public void PostWebMessageAsJson(string webMessageAsJson)
        => CoreWebView2?.PostWebMessageAsJson(webMessageAsJson);

    /// <summary>
    /// Posts a message to the web content as a string.
    /// </summary>
    /// <param name="webMessageAsString">The message to post as a string.</param>
    public void PostWebMessageAsString(string webMessageAsString)
        => CoreWebView2?.PostWebMessageAsString(webMessageAsString);

    public async Task NavigateAsync(Uri? source)
    {
        if (_disposed)
        {
            return;
        }

        await EnsureWebView2LoadedAsync();
        var coreWebView2 = CoreWebView2;
        if (coreWebView2 == null || coreWebView2.IsDisposed)
        {
            return;
        }

        if (source != null)
        {
            coreWebView2.Navigate(source);
            return;
        }

        coreWebView2.Navigate("about:blank");
    }

    protected override Size MeasureContent(Size availableSize) => new(320, 240);

    protected override void OnParentChanged()
    {
        base.OnParentChanged();

        UpdateWebViewVisibility();
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        UpdateWebViewBounds();
        UpdateWebViewVisibility();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        UpdateWebViewVisibility();
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _lastArrangedBounds = bounds;
        _ = EnsureWebView2LoadedAsync();
        UpdateWebViewBounds();
        UpdateWebViewVisibility();
    }

    protected override void OnVisibilityChanged()
    {
        base.OnVisibilityChanged();
        UpdateWebViewVisibility();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (!IsInitialized)
        {
            _font ??= GetGraphicsFactory().CreateFont(Theme.Metrics.FontFamily, Theme.Metrics.FontSize);

            var message = _webViewInfo?.ErrorMessage;
            context.DrawText(
                string.IsNullOrWhiteSpace(message) ? "WebView2 (Win32)" : message,
                Bounds,
                _font,
                Theme.Palette.DisabledText,
                TextAlignment.Center,
                TextAlignment.Center,
                string.IsNullOrWhiteSpace(message) ? TextWrapping.NoWrap : TextWrapping.Wrap);
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _font?.Dispose();
            _font = null;

            if (_navigationStarting.value != 0)
            {
                _webView2?.Object.remove_NavigationStarting(_navigationStarting);
                _navigationStarting.value = 0;
            }

            if (_navigationCompleted.value != 0)
            {
                _webView2?.Object.remove_NavigationCompleted(_navigationCompleted);
                _navigationCompleted.value = 0;
            }

            if (_frameNavigationCompleted.value != 0)
            {
                _webView2?.Object.remove_FrameNavigationCompleted(_frameNavigationCompleted);
                _frameNavigationCompleted.value = 0;
            }

            if (_documentTitleChanged.value != 0)
            {
                _webView2?.Object.remove_DocumentTitleChanged(_documentTitleChanged);
                _documentTitleChanged.value = 0;
            }

            if (_newWindowRequested.value != 0)
            {
                _webView2?.Object.remove_NewWindowRequested(_newWindowRequested);
                _newWindowRequested.value = 0;
            }

            if (_webMessageReceived.value != 0)
            {
                _webView2?.Object.remove_WebMessageReceived(_webMessageReceived);
                _webMessageReceived.value = 0;
            }

            if (_sourceChanged.value != 0)
            {
                _webView2?.Object.remove_SourceChanged(_sourceChanged);
                _sourceChanged.value = 0;
            }

            if (_contentLoading.value != 0)
            {
                _webView2?.Object.remove_ContentLoading(_contentLoading);
                _contentLoading.value = 0;
            }

            if (_zoomFactorChanged.value != 0)
            {
                _controller?.Object.remove_ZoomFactorChanged(_zoomFactorChanged);
                _zoomFactorChanged.value = 0;
            }
        }
        catch
        {
        }

        // Close controller first, then dispose COM objects.
        // Controller owns the WebView2, so it should be released first.
        try
        {
            _controller?.Object.Close();
        }
        catch
        {
        }

        _controller?.Dispose();
        _controller = null;
        CoreWebView2?.DisposeManaged();
        CoreWebView2 = null;
        _webView2?.Dispose();
        _webView2 = null;

        if (_hostHandle != 0)
        {
            s_hostMap.TryRemove(_hostHandle, out _);
            if (_hostWndProcPrev != 0)
            {
                Interop.SetWindowLongPtr(_hostHandle, GWLP_WNDPROC, _hostWndProcPrev);
                _hostWndProcPrev = 0;
            }

            try
            {
                Interop.DestroyWindow(_hostHandle);
            }
            catch
            {
            }
            _hostHandle = 0;
        }

        if (_webViewInfo != null)
        {
            if (_webViewInfo.IsShared)
            {
                var count = Interlocked.Decrement(ref WebViewInfo.SharedCount);
                if (count == 0)
                {
                    _webViewInfo.Dispose();
                }
            }
            else
            {
                _webViewInfo.Dispose();
            }

            _webViewInfo = null;
        }
    }

    private IComObject<ICoreWebView2Environment3>? EnsureWebView2EnvironmentLoaded()
    {
        if (_disposed)
        {
            return null;
        }

        if (_webViewInfo != null && _webViewInfo.Initialized)
        {
            return _webViewInfo.Environment;
        }

        if (UseSharedEnvironment)
        {
            WebViewInfo.Shared.EnsureEnvironment(BrowserExecutableFolder, UserDataFolder, Options);
            _webViewInfo = WebViewInfo.Shared;
            Interlocked.Increment(ref WebViewInfo.SharedCount);
            _browserExecutableFolder = WebViewInfo.Shared.BrowserExecutableFolder;
            _userDataFolder = WebViewInfo.Shared.UserDataFolder;
            _options = WebViewInfo.Shared.Options;
        }
        else
        {
            _webViewInfo = new WebViewInfo();
            _webViewInfo.EnsureEnvironment(BrowserExecutableFolder, UserDataFolder, Options);
        }

        var environment = _webViewInfo.Environment;
        if (environment != null && !environment.IsDisposed)
        {
            return environment;
        }

        return null;
    }

    private Task<IComObject<ICoreWebView2>?> EnsureWebView2LoadedAsync()
    {
        if (_disposed)
        {
            return Task.FromResult<IComObject<ICoreWebView2>?>(null);
        }

        if (_webView2 != null && !_webView2.IsDisposed)
        {
            return Task.FromResult<IComObject<ICoreWebView2>?>(_webView2);
        }

        var environment = EnsureWebView2EnvironmentLoaded();
        if (environment == null || environment.IsDisposed)
        {
            return Task.FromResult<IComObject<ICoreWebView2>?>(null);
        }

        var root = FindVisualRoot();
        if (root is not Window window || window.Handle == 0)
        {
            return Task.FromResult<IComObject<ICoreWebView2>?>(null);
        }

        EnsureHostWindow(window);
        if (_hostHandle == 0)
        {
            return Task.FromResult<IComObject<ICoreWebView2>?>(null);
        }

        if (_loadingWebView2 != null)
        {
            return _loadingWebView2;
        }

        var tcs = new TaskCompletionSource<IComObject<ICoreWebView2>?>();
        _loadingWebView2 = tcs.Task;

        var hr = environment.Object.CreateCoreWebView2Controller(
            _hostHandle,
            new CoreWebView2CreateCoreWebView2ControllerCompletedHandler((result, controller) =>
            {
                if (result.IsError)
                {
                    var ex = new InvalidOperationException($"WebView controller cannot be initialized: {result}.");
                    CoreWebView2InitializationCompleted?.Invoke(new WebViewInitializationCompletedEventArgs(false, ex));
                    tcs.TrySetException(ex);
                    _loadingWebView2 = null;
                    return;
                }

                try
                {
                    _controller = new ComObject<ICoreWebView2Controller>(controller);
                    _controller.Object.get_CoreWebView2(out var webView).ThrowOnError();


                    _webView2 = new ComObject<ICoreWebView2>(webView);
                    CoreWebView2 = new CoreWebView2(_webView2);

                    // Configure defaults.
                    _webView2.Object.get_Settings(out var settingsObj).ThrowOnError();
                    using (var settings = new ComObject<ICoreWebView2Settings3>(settingsObj))
                    {
                        settingsObj.put_IsBuiltInErrorPageEnabled(false).ThrowOnError();
                        settingsObj.put_AreDefaultContextMenusEnabled(false).ThrowOnError();
                        settingsObj.put_IsStatusBarEnabled(false).ThrowOnError();
                        settings.Object.put_AreBrowserAcceleratorKeysEnabled(false).ThrowOnError();
                    }

                    _webView2.Object.add_FrameNavigationCompleted(
                        new CoreWebView2NavigationCompletedEventHandler((_, args) =>
                            FrameNavigationCompleted?.Invoke(new WebViewNavigationCompletedEventArgs(args))),
                        ref _frameNavigationCompleted);

                    _webView2.Object.add_NavigationStarting(
                        new CoreWebView2NavigationStartingEventHandler((_, args) =>
                        {
                            var e = new WebViewNavigationStartingEventArgs(args);
                            NavigationStarting?.Invoke(e);
                            if (e.Cancel)
                            {
                                args.put_Cancel(1);
                            }
                        }),
                        ref _navigationStarting);

                    _webView2.Object.add_NavigationCompleted(
                        new CoreWebView2NavigationCompletedEventHandler((_, args) =>
                            NavigationCompleted?.Invoke(new WebViewNavigationCompletedEventArgs(args))),
                        ref _navigationCompleted);

                    _webView2.Object.add_DocumentTitleChanged(
                        new CoreWebView2DocumentTitleChangedEventHandler((sender, _) =>
                        {
                            sender.get_DocumentTitle(out var title);
                            try
                            {
                                DocumentTitleChanged?.Invoke(title.ToString() ?? string.Empty);
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(title.Value);
                            }
                        }),
                        ref _documentTitleChanged);

                    _webView2.Object.add_NewWindowRequested(
                        new CoreWebView2NewWindowRequestedEventHandler((_, args) =>
                        {
                            // Handled and NewWindow are applied directly to args via their setters.
                            // This supports both sync and async (deferral) patterns.
                            var e = new WebViewNewWindowRequestedEventArgs(args);
                            NewWindowRequested?.Invoke(e);
                        }),
                        ref _newWindowRequested);

                    _webView2.Object.add_WebMessageReceived(
                        new CoreWebView2WebMessageReceivedEventHandler((_, args) =>
                            WebMessageReceived?.Invoke(new WebViewWebMessageReceivedEventArgs(args))),
                        ref _webMessageReceived);

                    _webView2.Object.add_SourceChanged(
                        new CoreWebView2SourceChangedEventHandler((_, args) =>
                            SourceChanged?.Invoke(new WebViewSourceChangedEventArgs(args))),
                        ref _sourceChanged);

                    _webView2.Object.add_ContentLoading(
                        new CoreWebView2ContentLoadingEventHandler((_, args) =>
                            ContentLoading?.Invoke(new WebViewContentLoadingEventArgs(args))),
                        ref _contentLoading);

                    _controller.Object.add_ZoomFactorChanged(
                        new CoreWebView2ZoomFactorChangedEventHandler((_, _) =>
                            ZoomFactorChanged?.Invoke()),
                        ref _zoomFactorChanged);

                    CoreWebView2InitializationCompleted?.Invoke(new WebViewInitializationCompletedEventArgs(true, null));

                    UpdateWebViewBounds();
                    UpdateWebViewVisibility();
                    tcs.TrySetResult(_webView2);
                }
                catch (Exception ex)
                {
                    CoreWebView2InitializationCompleted?.Invoke(new WebViewInitializationCompletedEventArgs(false, ex));
                    tcs.TrySetException(ex);
                }
                finally
                {
                    _loadingWebView2 = null;
                }
            }));

        if (hr.IsError)
        {
            tcs.TrySetException(new InvalidOperationException($"WebView controller cannot be created: {hr}."));
            _loadingWebView2 = null;
        }

        return tcs.Task;
    }

    private void UpdateWebViewBounds()
    {
        if (_disposed)
        {
            return;
        }

        var controller = _controller;
        if (controller == null || controller.IsDisposed)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window || window.Handle == 0 || window.Dpi == 0)
        {
            return;
        }

        EnsureHostWindow(window);
        if (_hostHandle == 0)
        {
            return;
        }

        var local = new Rect(0, 0, _lastArrangedBounds.Width, _lastArrangedBounds.Height);
        var inWindowDip = TranslateRect(local, window);

        var dpiScale = window.Dpi / 96.0;
        int left = LayoutRounding.RoundToPixelInt(inWindowDip.X, dpiScale);
        int top = LayoutRounding.RoundToPixelInt(inWindowDip.Y, dpiScale);
        int right = LayoutRounding.RoundToPixelInt(inWindowDip.Right, dpiScale);
        int bottom = LayoutRounding.RoundToPixelInt(inWindowDip.Bottom, dpiScale);

        int width = Math.Max(0, right - left);
        int height = Math.Max(0, bottom - top);

        Interop.SetWindowPos(
            _hostHandle,
            HWND_TOP,
            left,
            top,
            width,
            height,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);

        // Bounds are relative to the host window.
        controller.Object.put_Bounds(new RECT(0, 0, width, height)).ThrowOnError();

    }

    private void UpdateWebViewVisibility()
    {
        if (_disposed)
        {
            return;
        }

        var controller = _controller;
        if (controller == null || controller.IsDisposed)
        {
            return;
        }

        var root = FindVisualRoot();
        bool attached = root is Window window && window.Handle != 0;
        controller.Object.put_IsVisible(attached && IsVisible ? 1 : 0).ThrowOnError();

        if (_hostHandle != 0)
        {
            Interop.ShowWindow(_hostHandle, attached && IsVisible ? SW_SHOWNOACTIVATE : SW_HIDE);
        }

    }

    private void EnsureHostWindow(Window window)
    {
        if (_hostHandle != 0)
        {
            return;
        }

        if (window.Handle == 0)
        {
            return;
        }

        // A lightweight child window to host the WebView2 controller.
        // Use a focusable built-in window class so a click inside the WebView can naturally transfer
        // Win32 focus to the hosted browser.
        const int BS_OWNERDRAW = 0x0000000B;
        _hostHandle = Interop.CreateWindowExW(
            0,
            "BUTTON",
            string.Empty,
            WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN | BS_OWNERDRAW,
            0,
            0,
            0,
            0,
            window.Handle,
            0,
            0,
            0);

        if (_hostHandle != 0)
        {
            s_hostMap[_hostHandle] = this;
            _hostWndProcPrev = Interop.SetWindowLongPtr(_hostHandle, GWLP_WNDPROC, s_hostWndProcPtr);
        }

        // Do not show here; visibility is controlled by UpdateWebViewVisibility after bounds are set.
    }

    private static nint HostWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_PARENTNOTIFY && s_hostMap.TryGetValue(hWnd, out var webViewFromNotify))
        {
            uint childMsg = (uint)wParam & 0xFFFF;
            if (childMsg == WM_MOUSEMOVE)
            {
                var root = webViewFromNotify.FindVisualRoot();
                if (root is Window window)
                {
                    window.ClearMouseOver();
                }
            }
            else if (childMsg == WM_LBUTTONDOWN || childMsg == WM_RBUTTONDOWN || childMsg == WM_MBUTTONDOWN)
            {
                var root = webViewFromNotify.FindVisualRoot();
                if (root is Window window)
                {
                    window.ClearMouseOver();
                    window.FocusManager.SetFocus(webViewFromNotify);
                }
            }
        }

        if (s_hostMap.TryGetValue(hWnd, out var target) && target._hostWndProcPrev != 0)
        {
            return Interop.CallWindowProc(target._hostWndProcPrev, hWnd, msg, wParam, lParam);
        }

        return Interop.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
