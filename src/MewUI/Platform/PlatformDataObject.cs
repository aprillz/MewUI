namespace Aprillz.MewUI.Platform;

/// <summary>
/// Data offered by another application: the format list is fixed when the drag enters, and each value is read from the
/// source on first request, then kept until the drag ends.
/// </summary>
internal abstract class PlatformDataObject : IDataObject
{
    private readonly List<string> _formats = new();
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private bool _closed;

    public IReadOnlyList<string> Formats => _formats;

    public bool Contains(string format)
        => !string.IsNullOrWhiteSpace(format) && _formats.Contains(format, StringComparer.Ordinal);

    public bool TryGetData<T>(string format, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? value)
    {
        if (GetData(format) is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetData<T>(DataFormat<T> format, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? value) where T : class
        => TryGetData(format.Name, out value);

    public object? GetData(string format)
    {
        if (!Contains(format))
        {
            return null;
        }

        if (_values.TryGetValue(format, out var cached))
        {
            return cached;
        }

        if (_closed)
        {
            return null;
        }

        var value = Read(format);
        _values[format] = value;
        return value;
    }

    /// <summary>Lists <paramref name="format"/> once; the first listing keeps its place.</summary>
    protected void AddFormat(string format)
    {
        if (!string.IsNullOrWhiteSpace(format) && !_formats.Contains(format, StringComparer.Ordinal))
        {
            _formats.Add(format);
        }
    }

    /// <summary>Reads a listed format from the source: a standard format as its documented type, any other as bytes.</summary>
    protected abstract object? Read(string format);

    /// <summary>Ends reading from the source; values already read stay available.</summary>
    public void Close()
    {
        _closed = true;
        OnClosed();
    }

    /// <summary>Releases what the source handed over for this drag.</summary>
    protected virtual void OnClosed()
    {
    }
}
