namespace Aprillz.MewUI.Controls;

partial class WebView2
{

    private sealed class WebViewInfo : IDisposable
    {
        public static int SharedCount;

        public bool Initialized;

        public ComObject<ICoreWebView2Environment3>? Environment;

        public HRESULT? WebViewInitializationResult;

        public HRESULT? WebViewEnvironmentInitializationResult;

        public string? WebViewVersion;

        public string? ErrorMessage;

        public static WebViewInfo Shared { get; } = new() { IsShared = true };

        public bool IsShared { get; private set; }

        public string? BrowserExecutableFolder { get; private set; }

        public string? UserDataFolder { get; private set; }

        public WebViewEnvironmentOptions? Options { get; private set; }

        public static void ThrowIfInitialized(WebViewInfo? info, [CallerMemberName] string? name = null)
        {
            if (info != null && info.Initialized)
            {
                throw new InvalidOperationException($"{name} cannot be set after WebView2 Environment is created.");
            }
        }

        public void EnsureEnvironment(string? browserExecutableFolder, string? userDataFolder, WebViewEnvironmentOptions? options)
        {
            if (Initialized)
            {
                return;
            }

            var hr = WebView2Utilities.Initialize(Assembly.GetEntryAssembly(), false);
            WebViewInitializationResult = hr;
            if (hr.IsError)
            {
                ErrorMessage =
                    $"WebView2 could not be initialized. Make sure it's installed properly, and WebView2Loader.dll is reachable (error: {WebViewInitializationResult}).";
                Initialized = true;
                return;
            }

            WebViewVersion = WebView2Utilities.GetAvailableCoreWebView2BrowserVersionString(browserExecutableFolder);
            hr = global::WebView2.Functions.CreateCoreWebView2EnvironmentWithOptions(
                PWSTR.From(browserExecutableFolder),
                PWSTR.From(userDataFolder),
                options?.ComObject!,
                new CoreWebView2CreateCoreWebView2EnvironmentCompletedHandler((result, env) =>
                {
                    WebViewEnvironmentInitializationResult = result;
                    if (result.IsError)
                    {
                        ErrorMessage = $"WebView2 environment could not be created (error: {result}).";
                        if (result == DirectN.Constants.RPC_E_CHANGED_MODE)
                        {
                            ErrorMessage += " Make sure the thread is initialized as an STA thread.";
                        }
                        return;
                    }

                    if (env is not ICoreWebView2Environment3 env3)
                    {
                        ErrorMessage =
                            $"Current WebView2 version ({WebView2Utilities.GetAvailableCoreWebView2BrowserVersionString(browserExecutableFolder)}) is not supported, please upgrade WebView2.";
                        return;
                    }

                    Environment = new ComObject<ICoreWebView2Environment3>(env3);
                }));
            WebViewEnvironmentInitializationResult = hr;
            if (hr.IsError)
            {
                ErrorMessage = $"WebView2 environment could not be initialized (error: {WebViewEnvironmentInitializationResult}).";
                Initialized = true;
                return;
            }

            Initialized = true;
            BrowserExecutableFolder = browserExecutableFolder;
            UserDataFolder = userDataFolder;
            Options = options;
        }

        public void Dispose()
        {
            WebViewInitializationResult = null;
            WebViewVersion = null;
            ErrorMessage = null;
            Initialized = false;
            Interlocked.Exchange(ref Environment, null)?.Dispose();
        }
    }
    

}
