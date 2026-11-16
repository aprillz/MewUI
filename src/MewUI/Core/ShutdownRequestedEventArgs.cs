namespace Aprillz.MewUI;

/// <summary>
/// Provides data for <see cref="Application.ShutdownRequested"/>.
/// </summary>
public sealed class ShutdownRequestedEventArgs
{
    /// <summary>
    /// Gets or sets whether the application keeps running instead of quitting.
    /// </summary>
    public bool Cancel { get; set; }
}
