using System.Diagnostics.CodeAnalysis;

namespace Aprillz.MewUI.Platform;

/// <summary>
/// Represents drag-and-drop or clipboard data in a format-agnostic way.
/// </summary>
public interface IDataObject
{
    /// <summary>
    /// Gets the available format identifiers.
    /// </summary>
    IReadOnlyList<string> Formats { get; }

    /// <summary>
    /// Returns true when the data object contains the specified format.
    /// </summary>
    bool Contains(string format);

    /// <summary>
    /// Attempts to retrieve strongly typed data for the specified format.
    /// </summary>
    bool TryGetData<T>(string format, [NotNullWhen(true)] out T? value);

    /// <summary>
    /// Returns the raw data for the specified format, or null when not present.
    /// </summary>
    object? GetData(string format);
}

/// <summary>
/// Well-known cross-platform data format identifiers.
/// </summary>
public static class StandardDataFormats
{
    /// <summary>
    /// File system items represented as <see cref="IReadOnlyList{T}"/> of absolute paths.
    /// </summary>
    public const string StorageItems = nameof(StorageItems);

    /// <summary>
    /// Plain text represented as <see cref="string"/>.
    /// </summary>
    public const string Text = nameof(Text);
}

/// <summary>
/// In-memory <see cref="IDataObject"/> implementation. Stores arbitrary .NET references by format key.
/// Used by framework-internal drag-and-drop (Phase 1); no OS clipboard format conversion is performed.
/// </summary>
public sealed class DataObject : IDataObject
{
    private readonly Dictionary<string, object> _data;

    public DataObject()
    {
        _data = new Dictionary<string, object>(StringComparer.Ordinal);
        Formats = new FormatsView(_data);
    }

    public DataObject(IDictionary<string, object> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = new Dictionary<string, object>(data, StringComparer.Ordinal);
        Formats = new FormatsView(_data);
    }

    public IReadOnlyList<string> Formats { get; }

    public bool Contains(string format)
        => !string.IsNullOrWhiteSpace(format) && _data.ContainsKey(format);

    public bool TryGetData<T>(string format, [NotNullWhen(true)] out T? value)
    {
        if (!string.IsNullOrWhiteSpace(format) &&
            _data.TryGetValue(format, out var raw) &&
            raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public object? GetData(string format)
        => !string.IsNullOrWhiteSpace(format) && _data.TryGetValue(format, out var raw) ? raw : null;

    /// <summary>
    /// Sets a payload value for the specified format. Overwrites any existing value.
    /// </summary>
    public void SetData(string format, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(value);
        _data[format] = value;
    }

    /// <summary>
    /// Sets plain text under <see cref="StandardDataFormats.Text"/>.
    /// </summary>
    public void SetText(string text) => SetData(StandardDataFormats.Text, text ?? string.Empty);

    /// <summary>
    /// Sets a list of storage item paths under <see cref="StandardDataFormats.StorageItems"/>.
    /// </summary>
    public void SetStorageItems(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        SetData(StandardDataFormats.StorageItems, paths);
    }

    private sealed class FormatsView : IReadOnlyList<string>
    {
        private readonly Dictionary<string, object> _data;

        public FormatsView(Dictionary<string, object> data) => _data = data;

        public string this[int index]
        {
            get
            {
                int i = 0;
                foreach (var key in _data.Keys)
                {
                    if (i == index) return key;
                    i++;
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public int Count => _data.Count;

        public IEnumerator<string> GetEnumerator() => _data.Keys.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
