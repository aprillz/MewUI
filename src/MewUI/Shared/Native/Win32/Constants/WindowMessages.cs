namespace Aprillz.MewUI.Native.Constants;

/// <summary>
/// Windows message constants (WM_*)
/// </summary>
internal static class WindowMessages
{
    public const uint WM_NULL = 0x0000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_ENABLE = 0x000A;
    public const uint WM_SETREDRAW = 0x000B;
    public const uint WM_SETTEXT = 0x000C;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_QUERYOPEN = 0x0013;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SYSCOLORCHANGE = 0x0015;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_SHOWWINDOW = 0x0018;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_WININICHANGE = 0x001A;
    public const uint WM_DEVMODECHANGE = 0x001B;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_FONTCHANGE = 0x001D;
    public const uint WM_TIMECHANGE = 0x001E;
    public const uint WM_CANCELMODE = 0x001F;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_CHILDACTIVATE = 0x0022;
    public const uint WM_QUEUESYNC = 0x0023;
    public const uint WM_GETMINMAXINFO = 0x0024;

    public const uint WM_PAINTICON = 0x0026;
    public const uint WM_ICONERASEBKGND = 0x0027;
    public const uint WM_NEXTDLGCTL = 0x0028;
    public const uint WM_SPOOLERSTATUS = 0x002A;
    public const uint WM_DRAWITEM = 0x002B;
    public const uint WM_MEASUREITEM = 0x002C;
    public const uint WM_DELETEITEM = 0x002D;
    public const uint WM_VKEYTOITEM = 0x002E;
    public const uint WM_CHARTOITEM = 0x002F;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_GETFONT = 0x0031;
    public const uint WM_SETHOTKEY = 0x0032;
    public const uint WM_GETHOTKEY = 0x0033;
    public const uint WM_QUERYDRAGICON = 0x0037;
    public const uint WM_COMPAREITEM = 0x0039;
    public const uint WM_GETOBJECT = 0x003D;
    public const uint WM_COMPACTING = 0x0041;
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_WINDOWPOSCHANGED = 0x0047;
    public const uint WM_POWER = 0x0048;
    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_CANCELJOURNAL = 0x004B;
    public const uint WM_NOTIFY = 0x004E;
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint WM_INPUTLANGCHANGE = 0x0051;
    public const uint WM_TCARD = 0x0052;
    public const uint WM_HELP = 0x0053;
    public const uint WM_USERCHANGED = 0x0054;
    public const uint WM_NOTIFYFORMAT = 0x0055;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_STYLECHANGING = 0x007C;
    public const uint WM_STYLECHANGED = 0x007D;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_GETICON = 0x007F;
    public const uint WM_SETICON = 0x0080;

    public const uint WM_NCCREATE = 0x0081;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_NCPAINT = 0x0085;
    public const uint WM_NCACTIVATE = 0x0086;
    public const uint WM_GETDLGCODE = 0x0087;
    public const uint WM_SYNCPAINT = 0x0088;
    public const uint WM_NCMOUSEMOVE = 0x00A0;
    public const uint WM_NCLBUTTONDOWN = 0x00A1;
    public const uint WM_NCLBUTTONUP = 0x00A2;
    public const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    public const uint WM_NCRBUTTONDOWN = 0x00A4;
    public const uint WM_NCRBUTTONUP = 0x00A5;
    public const uint WM_NCRBUTTONDBLCLK = 0x00A6;
    public const uint WM_NCMBUTTONDOWN = 0x00A7;
    public const uint WM_NCMBUTTONUP = 0x00A8;
    public const uint WM_NCMBUTTONDBLCLK = 0x00A9;

    // Keyboard
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_DEADCHAR = 0x0103;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_SYSCHAR = 0x0106;
    public const uint WM_SYSDEADCHAR = 0x0107;
    public const uint WM_UNICHAR = 0x0109;
    public const uint WM_IME_STARTCOMPOSITION = 0x010D;
    public const uint WM_IME_ENDCOMPOSITION = 0x010E;
    public const uint WM_IME_COMPOSITION = 0x010F;

    public const uint WM_INITDIALOG = 0x0110;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_HSCROLL = 0x0114;
    public const uint WM_VSCROLL = 0x0115;
    public const uint WM_INITMENU = 0x0116;
    public const uint WM_INITMENUPOPUP = 0x0117;
    public const uint WM_GESTURE = 0x0119;
    public const uint WM_GESTURENOTIFY = 0x011A;
    public const uint WM_MENUSELECT = 0x011F;
    public const uint WM_MENUCHAR = 0x0120;
    public const uint WM_ENTERIDLE = 0x0121;
    public const uint WM_MENURBUTTONUP = 0x0122;
    public const uint WM_MENUDRAG = 0x0123;
    public const uint WM_MENUGETOBJECT = 0x0124;
    public const uint WM_UNINITMENUPOPUP = 0x0125;
    public const uint WM_MENUCOMMAND = 0x0126;

    public const uint WM_CTLCOLORMSGBOX = 0x0132;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint WM_CTLCOLORLISTBOX = 0x0134;
    public const uint WM_CTLCOLORBTN = 0x0135;
    public const uint WM_CTLCOLORDLG = 0x0136;
    public const uint WM_CTLCOLORSCROLLBAR = 0x0137;
    public const uint WM_CTLCOLORSTATIC = 0x0138;

    // Mouse
    public const uint WM_MOUSEFIRST = 0x0200;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_RBUTTONDBLCLK = 0x0206;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MBUTTONDBLCLK = 0x0209;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP = 0x020C;
    public const uint WM_XBUTTONDBLCLK = 0x020D;
    public const uint WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_MOUSELAST = 0x020E;

    public const uint WM_PARENTNOTIFY = 0x0210;
    public const uint WM_ENTERMENULOOP = 0x0211;
    public const uint WM_EXITMENULOOP = 0x0212;
    public const uint WM_NEXTMENU = 0x0213;
    public const uint WM_SIZING = 0x0214;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOVING = 0x0216;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_DEVICECHANGE = 0x0219;

    public const uint WM_ENTERSIZEMOVE = 0x0231;
    public const uint WM_EXITSIZEMOVE = 0x0232;
    public const uint WM_DROPFILES = 0x0233;

    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_MOUSEHOVER = 0x02A1;

    public const uint WM_CUT = 0x0300;
    public const uint WM_COPY = 0x0301;
    public const uint WM_PASTE = 0x0302;
    public const uint WM_CLEAR = 0x0303;
    public const uint WM_UNDO = 0x0304;
    public const uint WM_RENDERFORMAT = 0x0305;
    public const uint WM_RENDERALLFORMATS = 0x0306;
    public const uint WM_DESTROYCLIPBOARD = 0x0307;
    public const uint WM_DRAWCLIPBOARD = 0x0308;
    public const uint WM_PAINTCLIPBOARD = 0x0309;
    public const uint WM_VSCROLLCLIPBOARD = 0x030A;
    public const uint WM_SIZECLIPBOARD = 0x030B;
    public const uint WM_ASKCBFORMATNAME = 0x030C;
    public const uint WM_CHANGECBCHAIN = 0x030D;
    public const uint WM_HSCROLLCLIPBOARD = 0x030E;
    public const uint WM_QUERYNEWPALETTE = 0x030F;
    public const uint WM_PALETTEISCHANGING = 0x0310;
    public const uint WM_PALETTECHANGED = 0x0311;
    public const uint WM_HOTKEY = 0x0312;

    public const uint WM_PRINT = 0x0317;
    public const uint WM_PRINTCLIENT = 0x0318;
    public const uint WM_APPCOMMAND = 0x0319;
    public const uint WM_THEMECHANGED = 0x031A;

    // DPI
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_DPICHANGED_BEFOREPARENT = 0x02E2;
    public const uint WM_DPICHANGED_AFTERPARENT = 0x02E3;
    public const uint WM_GETDPISCALEDSIZE = 0x02E4;

    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;
}
