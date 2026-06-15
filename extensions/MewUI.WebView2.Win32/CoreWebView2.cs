namespace Aprillz.MewUI.Controls;

/// <summary>
/// Represents a WebView2 browser instance. This class wraps the underlying ICoreWebView2 COM interface
/// and provides a C#-friendly API.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreWebView2
{
    private readonly IComObject<ICoreWebView2> _coreWebView2;
    private CoreWebView2Settings? _settings;
    private bool _disposed;

    internal CoreWebView2(IComObject<ICoreWebView2> coreWebView2)
    {
        _coreWebView2 = coreWebView2 ?? throw new ArgumentNullException(nameof(coreWebView2));
    }

    /// <summary>
    /// Gets the underlying COM object for internal use.
    /// </summary>
    internal ICoreWebView2 ComObject => _coreWebView2.Object;

    /// <summary>
    /// Gets a value indicating whether the underlying COM object has been disposed.
    /// </summary>
    internal bool IsDisposed => _disposed || _coreWebView2.IsDisposed;

    /// <summary>
    /// Disposes managed resources held by this wrapper (e.g., Settings).
    /// The underlying COM object is owned by WebView2 control.
    /// </summary>
    internal void DisposeManaged()
    {
        if (_disposed) return;
        _disposed = true;

        _settings?.Dispose();
        _settings = null;
    }

    /// <summary>
    /// Gets the URI of the current top-level document.
    /// </summary>
    public Uri? Source
    {
        get
        {
            if (_coreWebView2.IsDisposed) return null;

            _coreWebView2.Object.get_Source(out var uri);
            try
            {
                var uriString = uri.ToString();
                return string.IsNullOrEmpty(uriString) ? null : new Uri(uriString);
            }
            finally
            {
                Marshal.FreeCoTaskMem(uri.Value);
            }
        }
    }

    /// <summary>
    /// Gets the title of the current document.
    /// </summary>
    public string DocumentTitle
    {
        get
        {
            if (_coreWebView2.IsDisposed) return string.Empty;

            _coreWebView2.Object.get_DocumentTitle(out var title);
            try
            {
                return title.ToString() ?? string.Empty;
            }
            finally
            {
                Marshal.FreeCoTaskMem(title.Value);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the WebView can navigate backward.
    /// </summary>
    public bool CanGoBack
    {
        get
        {
            if (_coreWebView2.IsDisposed) return false;

            BOOL canGoBack = default;
            _coreWebView2.Object.get_CanGoBack(ref canGoBack);
            return canGoBack != 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the WebView can navigate forward.
    /// </summary>
    public bool CanGoForward
    {
        get
        {
            if (_coreWebView2.IsDisposed) return false;

            BOOL canGoForward = default;
            _coreWebView2.Object.get_CanGoForward(ref canGoForward);
            return canGoForward != 0;
        }
    }

    /// <summary>
    /// Gets the process ID of the browser process.
    /// </summary>
    public uint BrowserProcessId
    {
        get
        {
            if (_coreWebView2.IsDisposed) return 0;

            uint processId = 0;
            _coreWebView2.Object.get_BrowserProcessId(ref processId);
            return processId;
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is a fullscreen HTML element inside the WebView.
    /// </summary>
    public bool ContainsFullScreenElement
    {
        get
        {
            if (_coreWebView2.IsDisposed) return false;

            BOOL contains = default;
            _coreWebView2.Object.get_ContainsFullScreenElement(ref contains);
            return contains != 0;
        }
    }

    /// <summary>
    /// Gets the settings object for this CoreWebView2.
    /// </summary>
    public CoreWebView2Settings Settings
    {
        get
        {
            if (_settings != null) return _settings;

            _coreWebView2.Object.get_Settings(out var settings);
            _settings = new CoreWebView2Settings(settings);
            return _settings;
        }
    }

    /// <summary>
    /// Navigates to the specified URI.
    /// </summary>
    /// <param name="uri">The URI to navigate to.</param>
    public void Navigate(string uri)
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.Navigate(PWSTR.From(uri)).ThrowOnError();
    }

    /// <summary>
    /// Navigates to the specified URI.
    /// </summary>
    /// <param name="uri">The URI to navigate to.</param>
    public void Navigate(Uri uri)
    {
        Navigate(uri.AbsoluteUri);
    }

    /// <summary>
    /// Navigates to the specified HTML content.
    /// </summary>
    /// <param name="htmlContent">The HTML content to display.</param>
    public void NavigateToString(string htmlContent)
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.NavigateToString(PWSTR.From(htmlContent)).ThrowOnError();
    }

    /// <summary>
    /// Navigates to the previous page in the navigation history.
    /// </summary>
    public void GoBack()
    {
        if (_coreWebView2.IsDisposed || !CanGoBack) return;
        _coreWebView2.Object.GoBack().ThrowOnError();
    }

    /// <summary>
    /// Navigates to the next page in the navigation history.
    /// </summary>
    public void GoForward()
    {
        if (_coreWebView2.IsDisposed || !CanGoForward) return;
        _coreWebView2.Object.GoForward().ThrowOnError();
    }

    /// <summary>
    /// Reloads the current document.
    /// </summary>
    public void Reload()
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.Reload().ThrowOnError();
    }

    /// <summary>
    /// Stops any in-progress navigation.
    /// </summary>
    public void Stop()
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.Stop().ThrowOnError();
    }

    /// <summary>
    /// Executes JavaScript code in the WebView.
    /// </summary>
    /// <param name="script">The JavaScript code to execute.</param>
    /// <returns>The result of the script execution as a JSON string.</returns>
    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (_coreWebView2.IsDisposed)
        {
            return null;
        }

        var tcs = new TaskCompletionSource<string?>();
        var handler = new CoreWebView2ExecuteScriptCompletedHandler((hr, result) =>
        {
            if (hr.IsError)
            {
                tcs.TrySetException(new InvalidOperationException($"Script execution failed: {hr}"));
                return;
            }

            //try
            //{
            tcs.TrySetResult(result.ToString());
            //}
            //finally
            //{
            //    Marshal.FreeCoTaskMem(result.Value);
            //}
        });
        _coreWebView2.Object.ExecuteScript(PWSTR.From(script), handler).ThrowOnError();

        return await tcs.Task;
    }

    /// <summary>
    /// Posts a message to the web content as a JSON string.
    /// </summary>
    /// <param name="webMessageAsJson">The message to post as JSON.</param>
    public void PostWebMessageAsJson(string webMessageAsJson)
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.PostWebMessageAsJson(PWSTR.From(webMessageAsJson)).ThrowOnError();
    }

    /// <summary>
    /// Posts a message to the web content as a string.
    /// </summary>
    /// <param name="webMessageAsString">The message to post as a string.</param>
    public void PostWebMessageAsString(string webMessageAsString)
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.PostWebMessageAsString(PWSTR.From(webMessageAsString)).ThrowOnError();
    }

    /// <summary>
    /// Opens the DevTools window.
    /// </summary>
    public void OpenDevToolsWindow()
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.OpenDevToolsWindow().ThrowOnError();
    }

    /// <summary>
    /// Adds JavaScript to execute when a new document is created.
    /// </summary>
    /// <param name="javaScript">The JavaScript code to add.</param>
    /// <returns>A task that completes with the script ID that can be used to remove the script.</returns>
    public Task<string?> AddScriptToExecuteOnDocumentCreatedAsync(string javaScript)
    {
        if (_coreWebView2.IsDisposed)
        {
            return Task.FromResult<string?>(null);
        }

        var tcs = new TaskCompletionSource<string?>();
        _coreWebView2.Object.AddScriptToExecuteOnDocumentCreated(
            PWSTR.From(javaScript),
            new CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler((hr, id) =>
            {
                if (hr.IsError)
                {
                    tcs.TrySetException(new InvalidOperationException($"AddScriptToExecuteOnDocumentCreated failed: {hr}"));
                    return;
                }

                try
                {
                    tcs.TrySetResult(id.ToString());
                }
                finally
                {
                    Marshal.FreeCoTaskMem(id.Value);
                }
            })).ThrowOnError();

        return tcs.Task;
    }

    /// <summary>
    /// Removes a script that was added with AddScriptToExecuteOnDocumentCreatedAsync.
    /// </summary>
    /// <param name="id">The script ID returned by AddScriptToExecuteOnDocumentCreatedAsync.</param>
    public void RemoveScriptToExecuteOnDocumentCreated(string id)
    {
        if (_coreWebView2.IsDisposed) return;
        _coreWebView2.Object.RemoveScriptToExecuteOnDocumentCreated(PWSTR.From(id)).ThrowOnError();
    }
}
