using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Common button configurations for <see cref="NativeMessageBox"/>.
/// </summary>
public enum NativeMessageBoxButtons : uint
{
    /// <summary>An OK button.</summary>
    Ok = 0x00000000,
    /// <summary>OK and Cancel buttons.</summary>
    OkCancel = 0x00000001,
    /// <summary>Yes and No buttons.</summary>
    YesNo = 0x00000004,
    /// <summary>Yes, No and Cancel buttons.</summary>
    YesNoCancel = 0x00000003
}

/// <summary>
/// Common icon configurations for <see cref="NativeMessageBox"/>.
/// </summary>
public enum NativeMessageBoxIcon : uint
{
    /// <summary>No icon.</summary>
    None = 0x00000000,
    /// <summary>An information icon.</summary>
    Information = 0x00000040,
    /// <summary>A warning icon.</summary>
    Warning = 0x00000030,
    /// <summary>An error icon.</summary>
    Error = 0x00000010,
    /// <summary>A question icon.</summary>
    Question = 0x00000020
}

/// <summary>
/// Native-preferred platform message box (synchronous only), with managed fallback while the application runs.
/// </summary>
public static class NativeMessageBox
{
    private static Window? ResolveOwnerWindow(nint owner)
    {
        if (!Application.IsRunning)
        {
            return null;
        }

        var windows = Application.Current.AllWindows;
        if (owner != 0)
        {
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i].Handle == owner)
                {
                    return windows[i];
                }
            }
            return null;
        }

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (w.IsActive && w.Handle != 0)
                return w;
        }

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (w.Handle != 0)
                return w;
        }

        return null;
    }

    /// <summary>
    /// Shows a message box owned by the active window and returns true for OK or Yes, false for No or
    /// Cancel, or null when the dialog was dismissed without a choice; throws
    /// <see cref="PlatformNotSupportedException"/> when no native dialog is available and no application
    /// is running for the managed fallback.
    /// </summary>
    public static bool? Show(string text, string caption = "Aprillz.MewUI", NativeMessageBoxButtons buttons = NativeMessageBoxButtons.Ok, NativeMessageBoxIcon icon = NativeMessageBoxIcon.None)
        => Show(0, text, caption, buttons, icon);

    /// <summary>
    /// Shows a message box owned by the window with the given native handle (0 uses the active window) and
    /// returns true for OK or Yes, false for No or Cancel, or null when the dialog was dismissed without a
    /// choice; throws <see cref="PlatformNotSupportedException"/> when no native dialog is available and no
    /// application is running for the managed fallback.
    /// </summary>
    public static bool? Show(nint owner, string text, string caption = "Aprillz.MewUI", NativeMessageBoxButtons buttons = NativeMessageBoxButtons.Ok, NativeMessageBoxIcon icon = NativeMessageBoxIcon.None)
    {
        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        var ownerWindow = ResolveOwnerWindow(owner);
        if (owner == 0)
        {
            owner = ownerWindow?.Handle ?? 0;
        }

        Exception? nativeFailure = null;
        try
        {
            if (host.MessageBox.IsNativeDialogAvailable())
            {
                return host.MessageBox.Show(owner, text ?? string.Empty, caption ?? string.Empty, buttons, icon);
            }
        }
        catch (Exception ex)
        {
            nativeFailure = ex;
            DiagLog.Write($"[messagebox] Native dialog failed; falling back to managed. {ex.GetType().Name}");
        }

        if (!Application.IsRunning)
        {
            throw new PlatformNotSupportedException(
                "No native message box is available and managed fallback requires a running MewUI application.",
                nativeFailure);
        }

        return MessageBox.Prompt(new MessageBoxOptions
        {
            Message = text ?? string.Empty,
            Title = caption ?? string.Empty,
            Icon = ToManagedIcon(icon),
            Buttons = ToManagedButtons(buttons),
            Owner = ownerWindow,
        });
    }

    internal static PromptIconKind ToManagedIcon(NativeMessageBoxIcon icon) => icon switch
    {
        NativeMessageBoxIcon.None => PromptIconKind.None,
        NativeMessageBoxIcon.Information => PromptIconKind.Info,
        NativeMessageBoxIcon.Warning => PromptIconKind.Warning,
        NativeMessageBoxIcon.Error => PromptIconKind.Error,
        NativeMessageBoxIcon.Question => PromptIconKind.Question,
        _ => PromptIconKind.None,
    };

    internal static IReadOnlyList<MessageButton> ToManagedButtons(NativeMessageBoxButtons buttons) => buttons switch
    {
        NativeMessageBoxButtons.Ok => MessageBoxWindow.ButtonsOk,
        NativeMessageBoxButtons.OkCancel => MessageBoxWindow.ButtonsOkCancel,
        NativeMessageBoxButtons.YesNo => MessageBoxWindow.ButtonsYesNo,
        NativeMessageBoxButtons.YesNoCancel => MessageBoxWindow.ButtonsYesNoCancel,
        _ => MessageBoxWindow.ButtonsOk,
    };
}

/// <summary>
/// Managed message box dialogs using <see cref="MessageBoxWindow"/>.
/// </summary>
public static class MessageBox
{
    /// <summary>
    /// Shows a message with an OK button and completes when it closes.
    /// </summary>
    public static async Task NotifyAsync(string message, PromptIconKind icon = PromptIconKind.Info, string? detail = null, Window? owner = null)
    {
        await PromptAsync(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsOk
        });
    }

    /// <summary>
    /// Shows a message with OK and Cancel buttons and returns whether OK was chosen.
    /// </summary>
    public static async Task<bool> ConfirmAsync(string message, PromptIconKind icon = PromptIconKind.Question, string? detail = null, Window? owner = null)
    {
        var r = await PromptAsync(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsOkCancel
        });
        return r == true;
    }

    /// <summary>
    /// Shows a question with Yes and No buttons and returns whether Yes was chosen.
    /// </summary>
    public static async Task<bool> AskYesNoAsync(string message, PromptIconKind icon = PromptIconKind.Question, string? detail = null, Window? owner = null)
    {
        var r = await PromptAsync(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsYesNo
        });
        return r == true;
    }

    /// <summary>
    /// Shows a question with Yes, No and Cancel buttons and returns true for Yes, false for No, or null
    /// for Cancel.
    /// </summary>
    public static async Task<bool?> AskYesNoCancelAsync(string message, PromptIconKind icon = PromptIconKind.Question, string? detail = null, Window? owner = null)
    {
        return await PromptAsync(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsYesNoCancel
        });
    }

    /// <summary>
    /// Shows a message box built from the options and returns its dialog result.
    /// </summary>
    public static async Task<bool?> PromptAsync(MessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var owner = options.Owner ?? FindActiveWindow();

        var dlg = new MessageBoxWindow(
            message: options.Message,
            icon: options.Icon,
            buttons: options.Buttons,
            detail: options.Detail,
            checkBoxes: options.CheckBoxes,
            title: options.Title);
        dlg.SetMaxHeightFromOwner(owner);
        await dlg.ShowDialogAsync(owner);
        return dlg.DialogResult;
    }

    // Synchronous overloads. Block via a nested event loop (Window.ShowDialog) so they can be used from
    // synchronous contexts such as Window.Closing. Prefer the *Async versions elsewhere.

    /// <summary>
    /// Shows a message with an OK button, blocking in a nested event loop until it closes.
    /// </summary>
    public static void Notify(string message, PromptIconKind icon = PromptIconKind.Info, string? detail = null, Window? owner = null)
    {
        Prompt(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsOk
        });
    }

    /// <summary>
    /// Shows a message with OK and Cancel buttons, blocking in a nested event loop until it closes, and
    /// returns whether OK was chosen.
    /// </summary>
    public static bool Confirm(string message, PromptIconKind icon = PromptIconKind.Question, string? detail = null, Window? owner = null)
    {
        return Prompt(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsOkCancel
        }) == true;
    }

    /// <summary>
    /// Shows a question with Yes and No buttons, blocking in a nested event loop until it closes, and
    /// returns whether Yes was chosen.
    /// </summary>
    public static bool AskYesNo(string message, PromptIconKind icon = PromptIconKind.Question, string? detail = null, Window? owner = null)
    {
        return Prompt(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsYesNo
        }) == true;
    }

    /// <summary>
    /// Shows a question with Yes, No and Cancel buttons, blocking in a nested event loop until it closes,
    /// and returns true for Yes, false for No, or null for Cancel.
    /// </summary>
    public static bool? AskYesNoCancel(string message, PromptIconKind icon = PromptIconKind.Question, string? detail = null, Window? owner = null)
    {
        return Prompt(new MessageBoxOptions
        {
            Message = message,
            Icon = icon,
            Detail = detail,
            Owner = owner,
            Buttons = MessageBoxWindow.ButtonsYesNoCancel
        });
    }

    /// <summary>
    /// Shows a message box built from the options, blocking in a nested event loop until it closes, and
    /// returns its dialog result.
    /// </summary>
    public static bool? Prompt(MessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var owner = options.Owner ?? FindActiveWindow();

        var dlg = new MessageBoxWindow(
            message: options.Message,
            icon: options.Icon,
            buttons: options.Buttons,
            detail: options.Detail,
            checkBoxes: options.CheckBoxes,
            title: options.Title);
        dlg.SetMaxHeightFromOwner(owner);
        dlg.ShowDialog(owner);
        return dlg.DialogResult;
    }

    private static Window? FindActiveWindow()
    {
        if (!Application.IsRunning) return null;
        var windows = Application.Current.AllWindows;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].IsActive) return windows[i];
        }
        return windows.Count > 0 ? windows[0] : null;
    }
}
