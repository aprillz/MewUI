using System.Runtime.InteropServices.Marshalling;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies options used to create a WebView2 environment.
/// </summary>
public sealed class WebViewEnvironmentOptions
{
    private readonly WebViewEnvironmentOptionsCom _comOptions = new();

    internal ICoreWebView2EnvironmentOptions ComObject => _comOptions;

    /// <summary>
    /// Gets or sets additional command-line switches to pass to the browser process.
    /// </summary>
    public string? AdditionalBrowserArguments
    {
        get => _comOptions.AdditionalBrowserArguments;
        set => _comOptions.AdditionalBrowserArguments = value;
    }

    /// <summary>
    /// Gets or sets the language that the browser will use.
    /// </summary>
    public string? Language
    {
        get => _comOptions.Language;
        set => _comOptions.Language = value;
    }

    /// <summary>
    /// Gets or sets the minimum version of the WebView2 Runtime required.
    /// </summary>
    public string? TargetCompatibleBrowserVersion
    {
        get => _comOptions.TargetCompatibleBrowserVersion;
        set => _comOptions.TargetCompatibleBrowserVersion = value;
    }

    /// <summary>
    /// Gets or sets whether to allow single sign-on using the OS primary account.
    /// </summary>
    public bool AllowSingleSignOnUsingOSPrimaryAccount
    {
        get => _comOptions.AllowSingleSignOnUsingOSPrimaryAccount;
        set => _comOptions.AllowSingleSignOnUsingOSPrimaryAccount = value;
    }
}

[GeneratedComClass]
internal sealed partial class WebViewEnvironmentOptionsCom : ICoreWebView2EnvironmentOptions
{
    public string? AdditionalBrowserArguments { get; set; }

    public string? Language { get; set; }

    public string? TargetCompatibleBrowserVersion { get; set; }

    public bool AllowSingleSignOnUsingOSPrimaryAccount { get; set; }

    HRESULT ICoreWebView2EnvironmentOptions.get_AdditionalBrowserArguments(out PWSTR value)
    {
        value = PWSTR.From(AdditionalBrowserArguments);
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.put_AdditionalBrowserArguments(PWSTR value)
    {
        AdditionalBrowserArguments = value.ToString();
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.get_Language(out PWSTR value)
    {
        value = PWSTR.From(Language);
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.put_Language(PWSTR value)
    {
        Language = value.ToString();
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.get_TargetCompatibleBrowserVersion(out PWSTR value)
    {
        value = PWSTR.From(TargetCompatibleBrowserVersion);
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.put_TargetCompatibleBrowserVersion(PWSTR value)
    {
        TargetCompatibleBrowserVersion = value.ToString();
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.get_AllowSingleSignOnUsingOSPrimaryAccount(ref BOOL value)
    {
        value = AllowSingleSignOnUsingOSPrimaryAccount ? 1 : 0;
        return default;
    }

    HRESULT ICoreWebView2EnvironmentOptions.put_AllowSingleSignOnUsingOSPrimaryAccount(BOOL value)
    {
        AllowSingleSignOnUsingOSPrimaryAccount = value != 0;
        return default;
    }
}
