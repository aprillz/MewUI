using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// libdrm KMS entrypoints (legacy mode-setting API) for the framebuffer platform.
internal static unsafe partial class LibDrm
{
    private const string LIBRARY_NAME = "libdrm.so.2";

    public const int DRM_MODE_CONNECTED = 1;
    public const uint DRM_MODE_TYPE_PREFERRED = 1 << 3;
    public const uint DRM_MODE_PAGE_FLIP_EVENT = 0x01;
    public const uint DRM_MODE_PAGE_FLIP_ASYNC = 0x02;
    public const int DRM_EVENT_CONTEXT_VERSION = 2;

    [LibraryImport(LIBRARY_NAME)]
    public static partial DrmModeRes* drmModeGetResources(int fd);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void drmModeFreeResources(DrmModeRes* resources);

    [LibraryImport(LIBRARY_NAME)]
    public static partial DrmModeConnector* drmModeGetConnector(int fd, uint connectorId);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void drmModeFreeConnector(DrmModeConnector* connector);

    [LibraryImport(LIBRARY_NAME)]
    public static partial DrmModeEncoder* drmModeGetEncoder(int fd, uint encoderId);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void drmModeFreeEncoder(DrmModeEncoder* encoder);

    [LibraryImport(LIBRARY_NAME)]
    public static partial DrmModeCrtc* drmModeGetCrtc(int fd, uint crtcId);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void drmModeFreeCrtc(DrmModeCrtc* crtc);

    [LibraryImport(LIBRARY_NAME, SetLastError = true)]
    public static partial int drmModeSetCrtc(int fd, uint crtcId, uint bufferId, uint x, uint y, uint* connectors, int count, DrmModeModeInfo* mode);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int drmModeAddFB(int fd, uint width, uint height, byte depth, byte bpp, uint pitch, uint boHandle, out uint bufferId);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int drmModeAddFB2(int fd, uint width, uint height, uint pixelFormat, uint* boHandles, uint* pitches, uint* offsets, out uint bufferId, uint flags);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int drmModeRmFB(int fd, uint bufferId);

    [LibraryImport(LIBRARY_NAME, SetLastError = true)]
    public static partial int drmModePageFlip(int fd, uint crtcId, uint fbId, uint flags, nint userData);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int drmHandleEvent(int fd, DrmEventContext* context);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int drmModeSetCursor(int fd, uint crtcId, uint boHandle, uint width, uint height);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int drmModeMoveCursor(int fd, uint crtcId, int x, int y);

    /// <summary>Kernel connector name (HDMI-A-1, DSI-1, ...) built the way the kernel and modetest print it.</summary>
    public static string GetConnectorName(uint connectorType, uint connectorTypeId)
    {
        string prefix = connectorType switch
        {
            1 => "VGA",
            2 => "DVI-I",
            3 => "DVI-D",
            4 => "DVI-A",
            5 => "Composite",
            6 => "SVIDEO",
            7 => "LVDS",
            8 => "Component",
            9 => "DIN",
            10 => "DP",
            11 => "HDMI-A",
            12 => "HDMI-B",
            13 => "TV",
            14 => "eDP",
            15 => "Virtual",
            16 => "DSI",
            17 => "DPI",
            18 => "Writeback",
            19 => "SPI",
            20 => "USB",
            _ => "Unknown",
        };
        return $"{prefix}-{connectorTypeId}";
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DrmModeRes
{
    public int CountFbs;
    public uint* Fbs;
    public int CountCrtcs;
    public uint* Crtcs;
    public int CountConnectors;
    public uint* Connectors;
    public int CountEncoders;
    public uint* Encoders;
    public uint MinWidth;
    public uint MaxWidth;
    public uint MinHeight;
    public uint MaxHeight;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DrmModeModeInfo
{
    public uint Clock;
    public ushort HDisplay;
    public ushort HSyncStart;
    public ushort HSyncEnd;
    public ushort HTotal;
    public ushort HSkew;
    public ushort VDisplay;
    public ushort VSyncStart;
    public ushort VSyncEnd;
    public ushort VTotal;
    public ushort VScan;
    public uint VRefresh;
    public uint Flags;
    public uint Type;
    public fixed byte Name[32];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DrmModeConnector
{
    public uint ConnectorId;
    public uint EncoderId;
    public uint ConnectorType;
    public uint ConnectorTypeId;
    public int Connection;
    public uint MmWidth;
    public uint MmHeight;
    public int SubPixel;
    public int CountModes;
    public DrmModeModeInfo* Modes;
    public int CountProps;
    public uint* Props;
    public ulong* PropValues;
    public int CountEncoders;
    public uint* Encoders;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DrmModeEncoder
{
    public uint EncoderId;
    public uint EncoderType;
    public uint CrtcId;
    public uint PossibleCrtcs;
    public uint PossibleClones;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DrmModeCrtc
{
    public uint CrtcId;
    public uint BufferId;
    public uint X;
    public uint Y;
    public uint Width;
    public uint Height;
    public int ModeValid;
    public DrmModeModeInfo Mode;
    public int GammaSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DrmEventContext
{
    public int Version;
    public nint VBlankHandler;
    public delegate* unmanaged<int, uint, uint, uint, nint, void> PageFlipHandler;
    public nint PageFlipHandler2;
    public nint SequenceHandler;
}
