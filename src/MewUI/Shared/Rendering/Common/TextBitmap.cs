namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Represents a rasterized text glyph bitmap.
/// Shared across rendering backends (OpenGL, Vulkan, etc.) for FreeType text rendering.
/// </summary>
internal readonly record struct TextBitmap(int WidthPx, int HeightPx, byte[] Data);
