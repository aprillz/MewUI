namespace Aprillz.MewUI.Platform;

/// <summary>
/// A data format together with the type of its value.
/// </summary>
/// <typeparam name="T">The type of the value stored under the format.</typeparam>
public sealed class DataFormat<T> : IEquatable<DataFormat<T>> where T : class
{
    internal DataFormat(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the format identifier used by <see cref="IDataObject.Formats"/>.
    /// </summary>
    public string Name { get; }

    public bool Equals(DataFormat<T>? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DataFormat<T> other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => Name;
}

/// <summary>
/// Typed data formats: the standard formats every platform reads the same way, and access to a platform's own formats.
/// </summary>
public static class DataFormats
{
    /// <summary>
    /// File system items as absolute paths.
    /// </summary>
    public static DataFormat<IReadOnlyList<string>> StorageItems { get; } = new(StandardDataFormats.StorageItems);

    /// <summary>
    /// Absolute URIs as the source sent them, including items that are not local files.
    /// </summary>
    public static DataFormat<IReadOnlyList<string>> Uris { get; } = new(StandardDataFormats.Uris);

    /// <summary>
    /// Plain text.
    /// </summary>
    public static DataFormat<string> Text { get; } = new(StandardDataFormats.Text);

    /// <summary>
    /// A format another application offered, under the platform's own name for it; the value is the bytes as sent.
    /// </summary>
    public static DataFormat<byte[]> FromPlatformName(string name) => new(name);

    /// <summary>
    /// A format this application defines for its own data, such as a drag between its elements.
    /// </summary>
    public static DataFormat<T> Create<T>(string name) where T : class => new(name);
}
