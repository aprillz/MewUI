# Aprillz.MewUI.Win32.WebView2

Win32 WebView2 control for Aprillz.MewUI framework.

## Requirements

**Microsoft Edge WebView2 Runtime** (Evergreen)
- Automatically installed with Windows 11
- For Windows 10: https://developer.microsoft.com/microsoft-edge/webview2/

## Supported Platforms

| Architecture | Status |
|--------------|--------|
| Windows x64  | Supported |
| Windows x86  | Supported |
| Windows ARM64 | Supported |

## Supported .NET Versions

- .NET 8.0
- .NET 10.0

## NativeAOT Compatibility

This package is AOT-compatible (`IsAotCompatible=true`).

## Usage

```csharp
var webView = new WebView2();
webView.Source = new Uri("https://example.com");

// JavaScript execution
string result = await webView.ExecuteScriptAsync("document.title");

// Web message handling
webView.WebMessageReceived += (sender, e) => {
    Console.WriteLine($"Message: {e.WebMessageAsJson}");
};
```

## License

MIT
