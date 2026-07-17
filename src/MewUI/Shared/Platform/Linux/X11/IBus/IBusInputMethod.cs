using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.DBus;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11.IBus;

/// <summary>
/// IBus input method implementation via D-Bus.
/// </summary>
internal sealed class IBusInputMethod : IX11InputMethod
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_IME_DEBUG", "[IBus.IM]");

    // IBus capability flags
    private const uint IBUS_CAP_PREEDIT_TEXT = 1 << 0;
    private const uint IBUS_CAP_FOCUS = 1 << 3;

    // IBus key state modifiers (match X11 but with IBus release flag)
    private const uint IBUS_RELEASE_MASK = 1 << 30;

    private IBusConnection? _ibus;
    private bool _disposed;
    private bool _committedDuringDrain;

    public bool IsAvailable => _ibus?.IsConnected == true;
    public bool SupportsPreedit => true;

    public event Action<string>? CommitText;
    public event Action<X11PreeditState>? PreeditChanged;

    private IBusInputMethod(IBusConnection ibus)
    {
        _ibus = ibus;
    }

    /// <summary>
    /// Tries to create an IBus input method. Returns null if IBus is unavailable.
    /// </summary>
    public static IBusInputMethod? TryCreate()
    {
        var ibus = IBusConnection.TryConnect();
        if (ibus == null) return null;

        var im = new IBusInputMethod(ibus);

        // Tell IBus we want preedit and focus events
        ibus.SetCapabilities(IBUS_CAP_PREEDIT_TEXT | IBUS_CAP_FOCUS);

        Logger.Write("Created IBus input method");
        return im;
    }

    public void OnFocusIn()
    {
        _ibus?.FocusIn();
    }

    public void OnFocusOut()
    {
        _ibus?.FocusOut();
    }

    public void Reset()
    {
        _ibus?.Reset();
    }

    public void UpdateCursorRect(Rect rectInWindowPx)
    {
        _ibus?.SetCursorLocation(
            (int)rectInWindowPx.X,
            (int)rectInWindowPx.Y,
            (int)rectInWindowPx.Width,
            (int)rectInWindowPx.Height);
    }

    public X11ImeProcessResult ProcessKeyEvent(ref XEvent ev, bool isKeyDown)
    {
        if (_ibus == null || !_ibus.IsConnected)
            return new X11ImeProcessResult(Handled: false, ForwardKeyToApp: true, CommittedText: null);

        _committedDuringDrain = false;

        // Process pending signals before handling the key
        DrainSignals();

        // Extract key info from XEvent
        uint keyval = (uint)NativeX11.XLookupKeysym(ref ev.xkey, 0).ToInt64();
        uint keycode = ev.xkey.keycode - 8; // X11 keycode to hardware keycode (IBus expects evdev codes)
        uint state = ev.xkey.state;
        if (!isKeyDown)
            state |= IBUS_RELEASE_MASK;

        var (handled, forward) = _ibus.ProcessKeyEvent(keyval, keycode, state);

        Logger.Write($"ProcessKeyEvent keyval=0x{keyval:X} keycode={keycode} state=0x{state:X} -> handled={handled}");

        // Drain signals that arrived during ProcessKeyEvent (CommitText, etc.)
        // CommitText signals are delivered immediately via CommitText event
        // so that they are applied BEFORE subsequent preedit updates.
        DrainSignals();

        // If IBus didn't handle the key and no text was committed during draining,
        // extract text via XLookupString as fallback.
        string? committed = null;
        if (!handled && isKeyDown && !_committedDuringDrain)
        {
            committed = XimInputMethod.LookupStringWithoutIc(ref ev.xkey);
        }

        return new X11ImeProcessResult(
            Handled: handled,
            ForwardKeyToApp: !handled,
            CommittedText: committed);
    }

    /// <summary>
    /// Processes pending D-Bus signals (CommitText, UpdatePreeditText, etc).
    /// </summary>
    private void DrainSignals()
    {
        if (_ibus?.Connection == null) return;

        // Process up to 10 signals per drain to avoid infinite loop
        for (int i = 0; i < 10; i++)
        {
            var msg = _ibus.Connection.Poll();
            if (msg == null) break;

            if (msg.Type != DBusConstants.Signal) continue;

            ProcessSignal(msg);
        }
    }

    private void ProcessSignal(DBusMessage msg)
    {
        if (msg.Interface != "org.freedesktop.IBus.InputContext") return;

        switch (msg.Member)
        {
            case "CommitText":
                HandleCommitText(msg);
                break;
            case "UpdatePreeditText":
                HandleUpdatePreeditText(msg);
                break;
            case "HidePreeditText":
                PreeditChanged?.Invoke(new X11PreeditState
                {
                    Text = string.Empty,
                    IsEnd = true,
                });
                break;
            case "ForwardKeyEvent":
                // IBus wants us to handle this key normally
                // This is handled by returning ForwardKeyToApp=true
                break;
        }
    }

    private void HandleCommitText(DBusMessage msg)
    {
        if (msg.Body.Length == 0) return;

        try
        {
            Console.Error.WriteLine($"[IBus.CommitText] sig='{msg.Signature}' bodyLen={msg.Body.Length}");
            var r = new DBusReader(msg.Body, 0);
            string? text = r.ReadIBusText();
            if (!string.IsNullOrEmpty(text))
            {
                Logger.Write($"CommitText: '{text}'");

                // Deliver committed text immediately so it is applied BEFORE any
                // subsequent UpdatePreeditText signal.  Korean input sends CommitText
                // ("가") followed by UpdatePreeditText ("ㄴ") in the same drain cycle.
                // If we only store the text and deliver it after ProcessKeyEvent returns,
                // the preedit update arrives first and removes the not-yet-committed
                // composition, causing characters to appear one behind.
                _committedDuringDrain = true;
                CommitText?.Invoke(text);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"CommitText parse error: {ex.Message}");
        }
    }

    private void HandleUpdatePreeditText(DBusMessage msg)
    {
        if (msg.Body.Length == 0) return;

        try
        {
            Console.Error.WriteLine($"[IBus.UpdatePreedit] sig='{msg.Signature}' bodyLen={msg.Body.Length}");
            var r = new DBusReader(msg.Body, 0);
            string? text = r.ReadIBusText();
            uint cursorPos = r.ReadUInt32();
            bool visible = r.ReadBool();

            Logger.Write($"UpdatePreeditText: '{text}' cursor={cursorPos} visible={visible}");

            if (!visible || string.IsNullOrEmpty(text))
            {
                PreeditChanged?.Invoke(new X11PreeditState
                {
                    Text = string.Empty,
                    IsEnd = true,
                });
            }
            else
            {
                PreeditChanged?.Invoke(new X11PreeditState
                {
                    Text = text,
                    CursorPos = (int)cursorPos,
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"UpdatePreeditText parse error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _ibus?.Dispose();
        _ibus = null;
    }
}
