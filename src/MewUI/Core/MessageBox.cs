using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Common button configurations for <see cref="NativeMessageBox"/>.
/// </summary>
public enum NativeMessageBoxButtons : uint
{
    Ok = 0x00000000,
    OkCancel = 0x00000001,
    YesNo = 0x00000004,
    YesNoCancel = 0x00000003
}

/// <summary>
/// Common icon configurations for <see cref="NativeMessageBox"/>.
/// </summary>
public enum NativeMessageBoxIcon : uint
{
    None = 0x00000000,
    Information = 0x00000040,
    Warning = 0x00000030,
    Error = 0x00000010,
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

    public static bool? Show(string text, string caption = "Aprillz.MewUI", NativeMessageBoxButtons buttons = NativeMessageBoxButtons.Ok, NativeMessageBoxIcon icon = NativeMessageBoxIcon.None)
        => Show(0, text, caption, buttons, icon);

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
