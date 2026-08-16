namespace Aprillz.MewUI.Diagnostics;

internal ref struct StackTextFormatter
{
    private Span<char> _buffer;
    private int _length;

    public StackTextFormatter(Span<char> buffer)
    {
        _buffer = buffer;
        _length = 0;
    }

    public readonly ReadOnlySpan<char> WrittenSpan => _buffer[.._length];

    public void Append(char value)
    {
        if (_length < _buffer.Length)
        {
            _buffer[_length++] = value;
        }
    }

    public void Append(ReadOnlySpan<char> value)
    {
        int count = Math.Min(value.Length, _buffer.Length - _length);
        if (count <= 0)
        {
            return;
        }

        value[..count].CopyTo(_buffer[_length..]);
        _length += count;
    }

    public void Append(bool value)
        => Append(value ? "True" : "False");

    public void Append(int value)
        => AppendFormattable(value);

    public void Append(long value)
        => AppendFormattable(value);

    public void Append(double value, ReadOnlySpan<char> format)
    {
        if (value.TryFormat(_buffer[_length..], out int written, format))
        {
            _length += written;
        }
    }

    public void AppendBytes(long bytes)
    {
        if (bytes < 1024)
        {
            Append(bytes);
            Append(" B");
        }
        else if (bytes < 1024 * 1024)
        {
            Append(bytes / 1024.0, "0.0");
            Append(" KB");
        }
        else
        {
            Append(bytes / (1024.0 * 1024.0), "0.0");
            Append(" MB");
        }
    }

    private void AppendFormattable<T>(T value)
        where T : ISpanFormattable
    {
        if (value.TryFormat(_buffer[_length..], out int written, default, null))
        {
            _length += written;
        }
    }
}
