using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Windowless.Sample;

internal sealed class MacOSHotkeyProvider : IHotkeyProvider
{
    private const uint EVENT_CLASS_KEYBOARD = 0x6B657962; // 'keyb'
    private const uint EVENT_HOT_KEY_PRESSED = 6;
    private const uint EVENT_PARAM_DIRECT_OBJECT = 0x2D2D2D2D; // '----'
    private const uint TYPE_EVENT_HOT_KEY_ID = 0x686B6964; // 'hkid'
    private const uint SIGNATURE = 0x4D455755; // 'MEWU'
    private const uint CONTROL_KEY = 1u << 12;
    private const uint OPTION_KEY = 1u << 11;
    private const uint SPACE_KEY_CODE = 49;

    private EventHandler? _handler;
    private nint _handlerRef;
    private nint _hotkeyRef;
    private Action? _activated;

    public string Name => "macOS Carbon RegisterEventHotKey";

    public void Start(Action activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        _activated = activated;
        _handler = HandleEvent;
        var eventType = new EventTypeSpec { EventClass = EVENT_CLASS_KEYBOARD, EventKind = EVENT_HOT_KEY_PRESSED };
        // InstallApplicationEventHandler is a header macro over this call, so Carbon exports only InstallEventHandler.
        int status = InstallEventHandler(GetApplicationEventTarget(), _handler, 1, ref eventType, 0, out _handlerRef);
        if (status != 0)
        {
            throw new Win32Exception(status, "InstallEventHandler failed.");
        }

        var hotkeyId = new EventHotKeyId { Signature = SIGNATURE, Id = 1 };
        status = RegisterEventHotKey(SPACE_KEY_CODE, CONTROL_KEY | OPTION_KEY, hotkeyId,
            GetApplicationEventTarget(), 0, out _hotkeyRef);
        if (status != 0)
        {
            Dispose();
            throw new Win32Exception(status, "RegisterEventHotKey failed.");
        }
    }

    private int HandleEvent(nint nextHandler, nint @event, nint userData)
    {
        if (GetEventParameter(@event, EVENT_PARAM_DIRECT_OBJECT, TYPE_EVENT_HOT_KEY_ID, 0,
                (uint)Marshal.SizeOf<EventHotKeyId>(), 0, out var hotkeyId) == 0
            && hotkeyId.Signature == SIGNATURE && hotkeyId.Id == 1)
        {
            _activated?.Invoke();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_hotkeyRef != 0)
        {
            UnregisterEventHotKey(_hotkeyRef);
            _hotkeyRef = 0;
        }
        if (_handlerRef != 0)
        {
            RemoveEventHandler(_handlerRef);
            _handlerRef = 0;
        }
        _handler = null;
        _activated = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandler(nint nextHandler, nint @event, nint userData);

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [DllImport(Carbon)]
    private static extern nint GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(nint target, EventHandler handler, nuint count,
        ref EventTypeSpec eventTypes, nint userData, out nint handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(nint handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId hotkeyId,
        nint target, uint options, out nint hotkeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(nint hotkeyRef);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(nint @event, uint name, uint desiredType, nint actualType,
        uint bufferSize, nint actualSize, out EventHotKeyId data);
}
