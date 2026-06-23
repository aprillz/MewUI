namespace Aprillz.MewUI.Controls;

/// <summary>
/// Controls whether <see cref="Image"/> applies the orientation carried by its source.
/// </summary>
public enum ImageOrientationMode
{
    /// <summary>Apply the source's orientation so the image displays upright (the default).</summary>
    FromImage,

    /// <summary>Ignore orientation and display the raw decoded pixels as-is.</summary>
    Ignore,
}
