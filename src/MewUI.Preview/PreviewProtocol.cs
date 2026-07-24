using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// mew-preview v1 wire contract: 4-byte little-endian total length, 4-byte message type id,
/// 4-byte JSON length, UTF-8 JSON body, then an optional binary payload (Frame pixels).
/// Unknown message type ids are ignored for minor-version forward compatibility.
/// </summary>
internal static class PreviewProtocol
{
    internal const int PROTOCOL_MAJOR = 1;
    internal const int PROTOCOL_MINOR = 2;

    // Sanity cap for inbound messages; Frame (outbound) is bounded by the negotiated viewport.
    internal const int MAX_MESSAGE_BYTES = 64 * 1024 * 1024;

    internal const int HELLO = 1;
    internal const int SESSION_STARTED = 2;
    internal const int CLIENT_INFO = 3;
    internal const int PREVIEW_TARGETS = 4;
    internal const int SELECT_TARGET = 5;
    internal const int VIEWPORT_CHANGED = 6;
    internal const int FRAME = 7;
    internal const int FRAME_ACK = 8;
    internal const int STATUS = 9;
    internal const int REFRESH_TARGET = 10;
    internal const int SESSION_REJECTED = 11;
    internal const int SET_THEME = 12;
    internal const int POINTER_MOVED = 13;
    internal const int POINTER_PRESSED = 14;
    internal const int POINTER_RELEASED = 15;
    internal const int SCROLL = 16;
    internal const int KEY = 17;
    internal const int TEXT_INPUT = 18;
}

internal sealed class HelloMessage
{
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public string Token { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = [];
}

internal sealed class SessionStartedMessage
{
    public string SessionId { get; set; } = string.Empty;
    public string FrameworkVersion { get; set; } = string.Empty;
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public string[] Capabilities { get; set; } = [];
}

internal sealed class SessionRejectedMessage
{
    public string Reason { get; set; } = string.Empty;
}

internal sealed class ClientInfoMessage
{
    public double Dpi { get; set; }
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
}

internal sealed class PreviewTargetInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public int? SourceLine { get; set; }
    public bool Available { get; set; } = true;
    public string? Reason { get; set; }
}

internal sealed class PreviewTargetsMessage
{
    public PreviewTargetInfo[] Targets { get; set; } = [];
    public string ActiveId { get; set; } = string.Empty;
}

internal sealed class SelectTargetMessage
{
    public string Id { get; set; } = string.Empty;
}

internal sealed class ViewportChangedMessage
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double Dpi { get; set; }
}

internal sealed class FrameMessage
{
    public long Seq { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
    public string Format { get; set; } = "bgra8888";
    public double DpiScale { get; set; }
}

internal sealed class FrameAckMessage
{
    public long Seq { get; set; }
}

internal sealed class StatusMessage
{
    public string Message { get; set; } = string.Empty;
    public bool IsBuilding { get; set; }
    public bool HasError { get; set; }
    public string? UpdateKind { get; set; }
    public string? ExceptionDetail { get; set; }
    public string? ThemeMode { get; set; }
}

internal sealed class SetThemeMessage
{
    // "light" | "dark" | "system"
    public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Pointer state in window DIPs. Button uses the W3C convention (0 left, 1 middle, 2 right);
/// Buttons is the W3C bitmask (1 left, 2 right, 4 middle) after the event; Modifiers matches
/// the framework's ModifierKeys flags.
/// </summary>
internal sealed class PointerMessage
{
    public double X { get; set; }
    public double Y { get; set; }
    public int Button { get; set; }
    public int Buttons { get; set; }
    public int ClickCount { get; set; } = 1;
    public int Modifiers { get; set; }
}

internal sealed class ScrollMessage
{
    public double X { get; set; }
    public double Y { get; set; }
    // Notches: +Y = scroll-up intent, +X = scroll-left intent (framework wheel convention).
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }
    public int Modifiers { get; set; }
}

internal sealed class KeyMessage
{
    // W3C KeyboardEvent.code (physical key), e.g. "KeyA", "ArrowLeft".
    public string Code { get; set; } = string.Empty;
    public bool IsDown { get; set; }
    public int Modifiers { get; set; }
}

internal sealed class TextInputMessage
{
    public string Text { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HelloMessage))]
[JsonSerializable(typeof(SessionStartedMessage))]
[JsonSerializable(typeof(SessionRejectedMessage))]
[JsonSerializable(typeof(ClientInfoMessage))]
[JsonSerializable(typeof(PreviewTargetsMessage))]
[JsonSerializable(typeof(SelectTargetMessage))]
[JsonSerializable(typeof(ViewportChangedMessage))]
[JsonSerializable(typeof(FrameMessage))]
[JsonSerializable(typeof(FrameAckMessage))]
[JsonSerializable(typeof(StatusMessage))]
[JsonSerializable(typeof(SetThemeMessage))]
[JsonSerializable(typeof(PointerMessage))]
[JsonSerializable(typeof(ScrollMessage))]
[JsonSerializable(typeof(KeyMessage))]
[JsonSerializable(typeof(TextInputMessage))]
internal sealed partial class PreviewJsonContext : JsonSerializerContext
{
}
