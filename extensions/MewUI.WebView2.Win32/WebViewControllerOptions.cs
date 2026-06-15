using System.Runtime.InteropServices.Marshalling;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies options used to create a WebView2 controller.
/// </summary>
public sealed class WebViewControllerOptions
{
    private readonly WebViewControllerOptionsCom comOptions = new();

    internal ICoreWebView2ControllerOptions ComObject => comOptions;

    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string? ProfileName
    {
        get => comOptions.ProfileName;
        set => comOptions.ProfileName = value;
    }

    /// <summary>
    /// Gets or sets whether to use an InPrivate profile.
    /// </summary>
    public bool IsInPrivateModeEnabled
    {
        get => comOptions.IsInPrivateModeEnabled;
        set => comOptions.IsInPrivateModeEnabled = value;
    }
}

[GeneratedComClass]
internal sealed partial class WebViewControllerOptionsCom : ICoreWebView2ControllerOptions
{
    public string? ProfileName { get; set; }

    public bool IsInPrivateModeEnabled { get; set; }

    HRESULT ICoreWebView2ControllerOptions.get_ProfileName(out PWSTR value)
    {
        value = PWSTR.From(ProfileName);
        return default;
    }

    HRESULT ICoreWebView2ControllerOptions.put_ProfileName(PWSTR value)
    {
        ProfileName = value.ToString();
        return default;
    }

    HRESULT ICoreWebView2ControllerOptions.get_IsInPrivateModeEnabled(ref BOOL value)
    {
        value = IsInPrivateModeEnabled ? 1 : 0;
        return default;
    }

    HRESULT ICoreWebView2ControllerOptions.put_IsInPrivateModeEnabled(BOOL value)
    {
        IsInPrivateModeEnabled = value != 0;
        return default;
    }
}
