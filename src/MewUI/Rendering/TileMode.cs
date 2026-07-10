namespace Aprillz.MewUI.Rendering;

/// <summary>
/// How an image brush extends its image beyond a single tile.
/// </summary>
public enum TileMode
{
    /// <summary>Do not tile - fill outside the image's destination rect is transparent.</summary>
    None,

    /// <summary>Repeat the tile on both axes.</summary>
    Tile,

    /// <summary>Repeat on X axis only; Y axis is clamped/transparent.</summary>
    TileX,

    /// <summary>Repeat on Y axis only; X axis is clamped/transparent.</summary>
    TileY,
}
