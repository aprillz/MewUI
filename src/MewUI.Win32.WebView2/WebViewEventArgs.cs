namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies the web error status values for navigation.
/// </summary>
public enum WebViewErrorStatus
{
    /// <summary>
    /// An unknown error occurred.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The SSL certificate common name does not match the web address.
    /// </summary>
    CertificateCommonNameIsIncorrect = 1,

    /// <summary>
    /// The SSL certificate has expired.
    /// </summary>
    CertificateExpired = 2,

    /// <summary>
    /// The SSL client certificate contains errors.
    /// </summary>
    ClientCertificateContainsErrors = 3,

    /// <summary>
    /// The SSL certificate has been revoked.
    /// </summary>
    CertificateRevoked = 4,

    /// <summary>
    /// The SSL certificate is not valid.
    /// </summary>
    CertificateIsInvalid = 5,

    /// <summary>
    /// The host is unreachable.
    /// </summary>
    ServerUnreachable = 6,

    /// <summary>
    /// The connection has timed out.
    /// </summary>
    Timeout = 7,

    /// <summary>
    /// The server returned an invalid or unrecognized response.
    /// </summary>
    ErrorHttpInvalidServerResponse = 8,

    /// <summary>
    /// The connection was stopped.
    /// </summary>
    ConnectionAborted = 9,

    /// <summary>
    /// The connection was reset.
    /// </summary>
    ConnectionReset = 10,

    /// <summary>
    /// The Internet connection has been lost.
    /// </summary>
    Disconnected = 11,

    /// <summary>
    /// A connection to the destination was not established.
    /// </summary>
    CannotConnect = 12,

    /// <summary>
    /// The provided host name was not able to be resolved.
    /// </summary>
    HostNameNotResolved = 13,

    /// <summary>
    /// The operation was canceled.
    /// </summary>
    OperationCanceled = 14,

    /// <summary>
    /// The request redirect failed.
    /// </summary>
    RedirectFailed = 15,

    /// <summary>
    /// An unexpected error occurred.
    /// </summary>
    UnexpectedError = 16,

    /// <summary>
    /// User is prompted with a login, waiting on user action.
    /// </summary>
    ValidAuthenticationCredentialsRequired = 17,

    /// <summary>
    /// User lacks proper authentication credentials for a proxy server.
    /// </summary>
    ValidProxyAuthenticationRequired = 18,
}

/// <summary>
/// Provides data for the <see cref="WebView2.NavigationCompleted"/> event.
/// </summary>
public class WebViewNavigationCompletedEventArgs : EventArgs
{
    internal WebViewNavigationCompletedEventArgs(ICoreWebView2NavigationCompletedEventArgs args)
    {
        BOOL isSuccess = default;
        args.get_IsSuccess(ref isSuccess);
        IsSuccess = isSuccess != 0;

        COREWEBVIEW2_WEB_ERROR_STATUS webErrorStatus = default;
        args.get_WebErrorStatus(ref webErrorStatus);
        WebErrorStatus = (WebViewErrorStatus)webErrorStatus;

        if (args is ICoreWebView2NavigationCompletedEventArgs2 args2)
        {
            int httpStatusCode = 0;
            args2.get_HttpStatusCode(ref httpStatusCode);
            HttpStatusCode = httpStatusCode;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the navigation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the HTTP status code of the navigation.
    /// </summary>
    public int HttpStatusCode { get; }

    /// <summary>
    /// Gets the web error status if the navigation failed.
    /// </summary>
    public WebViewErrorStatus WebErrorStatus { get; }
}

/// <summary>
/// Provides data for the <see cref="WebView2.NavigationStarting"/> event.
/// </summary>
public class WebViewNavigationStartingEventArgs : EventArgs
{
    internal WebViewNavigationStartingEventArgs(ICoreWebView2NavigationStartingEventArgs args)
    {
        args.get_Uri(out var uri);
        try
        {
            Uri = uri.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem(uri.Value);
        }

        BOOL isUserInitiated = default;
        args.get_IsUserInitiated(ref isUserInitiated);
        IsUserInitiated = isUserInitiated != 0;

        BOOL isRedirected = default;
        args.get_IsRedirected(ref isRedirected);
        IsRedirected = isRedirected != 0;

        if (args is ICoreWebView2NavigationStartingEventArgs2 args2)
        {
            args2.get_AdditionalAllowedFrameAncestors(out var ancestors);
            try
            {
                AdditionalAllowedFrameAncestors = ancestors.ToString();
            }
            finally
            {
                Marshal.FreeCoTaskMem(ancestors.Value);
            }
        }
    }

    /// <summary>
    /// Gets the URI of the navigation.
    /// </summary>
    public string? Uri { get; }

    /// <summary>
    /// Gets a value indicating whether the navigation was initiated by the user.
    /// </summary>
    public bool IsUserInitiated { get; }

    /// <summary>
    /// Gets a value indicating whether the navigation is a redirect.
    /// </summary>
    public bool IsRedirected { get; }

    /// <summary>
    /// Gets the additional allowed frame ancestors.
    /// </summary>
    public string? AdditionalAllowedFrameAncestors { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to cancel the navigation.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Provides data for the <see cref="WebView2.NewWindowRequested"/> event.
/// </summary>
public class WebViewNewWindowRequestedEventArgs : EventArgs
{
    private readonly ICoreWebView2NewWindowRequestedEventArgs _args;
    private CoreWebView2Deferral? _deferral;

    internal WebViewNewWindowRequestedEventArgs(ICoreWebView2NewWindowRequestedEventArgs args)
    {
        _args = args;

        args.get_Uri(out var uri);
        try
        {
            Uri = uri.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem(uri.Value);
        }

        BOOL isUserInitiated = default;
        args.get_IsUserInitiated(ref isUserInitiated);
        IsUserInitiated = isUserInitiated != 0;
    }

    /// <summary>
    /// Gets the URI of the new window request.
    /// </summary>
    public string? Uri { get; }

    /// <summary>
    /// Gets a value indicating whether the request was initiated by the user.
    /// </summary>
    public bool IsUserInitiated { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the new window request is handled.
    /// When set to true, the default new window behavior is suppressed.
    /// </summary>
    public bool Handled
    {
        get;
        set
        {
            field = value;
            _args.put_Handled(value ? 1 : 0);
        }
    }

    /// <summary>
    /// Gets or sets the CoreWebView2 to use for the new window.
    /// Set this to redirect the new window request to an existing CoreWebView2.
    /// </summary>
    public CoreWebView2? NewWindow
    {
        get;
        set
        {
            field = value;
            if (value?.ComObject != null)
            {
                _args.put_NewWindow(value.ComObject);
            }
        }
    }

    /// <summary>
    /// Gets a deferral that allows asynchronous completion of this event.
    /// Use this when you need to perform async operations (like initializing a new WebView2)
    /// before completing the event handling.
    /// </summary>
    /// <returns>A deferral object. Call Complete() when done.</returns>
    /// <exception cref="InvalidOperationException">GetDeferral has already been called.</exception>
    public CoreWebView2Deferral GetDeferral()
    {
        if (_deferral != null)
        {
            throw new InvalidOperationException("GetDeferral can only be called once per event.");
        }

        _args.GetDeferral(out var deferral);
        _deferral = new CoreWebView2Deferral(deferral);
        return _deferral;
    }
}

/// <summary>
/// Provides data for the <see cref="WebView2.CoreWebView2InitializationCompleted"/> event.
/// </summary>
public class WebViewInitializationCompletedEventArgs : EventArgs
{
    internal WebViewInitializationCompletedEventArgs(bool isSuccess, Exception? initializationException)
    {
        IsSuccess = isSuccess;
        InitializationException = initializationException;
    }

    /// <summary>
    /// Gets a value indicating whether the initialization was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the exception that occurred during initialization, if any.
    /// </summary>
    public Exception? InitializationException { get; }
}

/// <summary>
/// Provides data for the <see cref="WebView2.SourceChanged"/> event.
/// </summary>
public class WebViewSourceChangedEventArgs : EventArgs
{
    internal WebViewSourceChangedEventArgs(ICoreWebView2SourceChangedEventArgs args)
    {
        BOOL isNewDocument = default;
        args.get_IsNewDocument(ref isNewDocument);
        IsNewDocument = isNewDocument != 0;
    }

    /// <summary>
    /// Gets a value indicating whether the navigation is to a new document.
    /// </summary>
    public bool IsNewDocument { get; }
}

/// <summary>
/// Provides data for the <see cref="WebView2.ContentLoading"/> event.
/// </summary>
public class WebViewContentLoadingEventArgs : EventArgs
{
    internal WebViewContentLoadingEventArgs(ICoreWebView2ContentLoadingEventArgs args)
    {
        BOOL isErrorPage = default;
        args.get_IsErrorPage(ref isErrorPage);
        IsErrorPage = isErrorPage != 0;

        ulong navigationId = 0;
        args.get_NavigationId(ref navigationId);
        NavigationId = navigationId;
    }

    /// <summary>
    /// Gets a value indicating whether the loaded content is an error page.
    /// </summary>
    public bool IsErrorPage { get; }

    /// <summary>
    /// Gets the navigation ID.
    /// </summary>
    public ulong NavigationId { get; }
}

/// <summary>
/// Provides data for the <see cref="WebView2.WebMessageReceived"/> event.
/// </summary>
public class WebViewWebMessageReceivedEventArgs : EventArgs
{
    internal WebViewWebMessageReceivedEventArgs(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        args.get_Source(out var source);
        try
        {
            Source = source.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem(source.Value);
        }

        args.TryGetWebMessageAsString(out var message);
        try
        {
            WebMessageAsJson = message.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem(message.Value);
        }
    }

    /// <summary>
    /// Gets the source URI of the document that sent the message.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Gets the message as a JSON string.
    /// </summary>
    public string? WebMessageAsJson { get; }
}
