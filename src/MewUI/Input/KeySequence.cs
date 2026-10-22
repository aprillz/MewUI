using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// An ordered list of one to four key gestures pressed one after another, such as Ctrl+K, Ctrl+I.
/// </summary>
/// <remarks>
/// A single <see cref="KeyGesture"/> converts implicitly, so a sequence of one stroke is written as the gesture itself.
/// </remarks>
public readonly struct KeySequence : IEquatable<KeySequence>
{
    internal const int MAX_LENGTH = 4;

    private readonly StrokeBuffer _strokes;
    private readonly int _length;

    /// <summary>Creates a sequence of one stroke.</summary>
    public KeySequence(KeyGesture first)
    {
        Validate(first, nameof(first));
        _strokes[0] = first;
        _length = 1;
    }

    /// <summary>Creates a sequence of two strokes.</summary>
    public KeySequence(KeyGesture first, KeyGesture second)
    {
        Validate(first, nameof(first));
        Validate(second, nameof(second));
        _strokes[0] = first;
        _strokes[1] = second;
        _length = 2;
    }

    /// <summary>Creates a sequence of one to four strokes, each of which names a key.</summary>
    public KeySequence(params KeyGesture[] strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Length is < 1 or > MAX_LENGTH)
        {
            throw new ArgumentException($"A key sequence has 1 to {MAX_LENGTH} strokes.", nameof(strokes));
        }

        for (int index = 0; index < strokes.Length; index++)
        {
            Validate(strokes[index], nameof(strokes));
            _strokes[index] = strokes[index];
        }

        _length = strokes.Length;
    }

    private KeySequence(in StrokeBuffer strokes, int length)
    {
        _strokes = strokes;
        _length = length;
    }

    /// <summary>Gets the strokes in the order they are pressed.</summary>
    [UnscopedRef]
    public ReadOnlySpan<KeyGesture> Strokes => ((ReadOnlySpan<KeyGesture>)_strokes)[.._length];

    /// <summary>Gets the number of strokes; zero only for the default value.</summary>
    public int Length => _length;

    /// <summary>Gets whether the sequence is one stroke, which is what a <see cref="KeyGesture"/> expresses.</summary>
    public bool IsSingle => _length == 1;

    internal KeyGesture this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_length, nameof(index));
            return _strokes[index];
        }
    }

    /// <summary>Converts a gesture to a sequence of one stroke.</summary>
    public static implicit operator KeySequence(KeyGesture gesture) => new(gesture);

    public static bool operator ==(KeySequence left, KeySequence right) => left.Equals(right);

    public static bool operator !=(KeySequence left, KeySequence right) => !left.Equals(right);

    /// <summary>
    /// Parses the portable form: strokes separated by a comma, each a list of modifiers (Ctrl, Shift, Alt, Meta,
    /// Primary) and a key joined by '+', where the key is a <see cref="Key"/> name or the character it types on a US
    /// keyboard. Example: "Primary+K, Primary+I".
    /// </summary>
    /// <exception cref="FormatException">The text is not a key sequence.</exception>
    public static KeySequence Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!KeyGestureText.TryParseSequence(text, out var sequence))
        {
            throw new FormatException($"'{text}' is not a key sequence.");
        }

        return sequence;
    }

    /// <summary>Parses the portable form accepted by <see cref="Parse"/>; false when the text is not a key sequence.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out KeySequence sequence)
    {
        if (text == null)
        {
            sequence = default;
            return false;
        }

        return KeyGestureText.TryParseSequence(text, out sequence);
    }

    /// <summary>Returns a platform-appropriate display string using <see cref="PlatformConventions.Current"/>.</summary>
    public string ToDisplayString() => PlatformConventions.Current.FormatSequence(Resolve());

    /// <summary>Returns the portable form that <see cref="Parse"/> reads back.</summary>
    public override string ToString() => KeyGestureText.Format(this);

    public bool Equals(KeySequence other)
    {
        if (_length != other._length)
        {
            return false;
        }

        for (int index = 0; index < _length; index++)
        {
            if (_strokes[index] != other._strokes[index])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is KeySequence other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (int index = 0; index < _length; index++)
        {
            hash.Add(_strokes[index]);
        }

        return hash.ToHashCode();
    }

    /// <summary>The same strokes with <see cref="ModifierKeys.Primary"/> resolved to the platform modifier.</summary>
    internal KeySequence Resolve()
    {
        StrokeBuffer resolved = default;
        for (int index = 0; index < _length; index++)
        {
            resolved[index] = _strokes[index].Resolve();
        }

        return new KeySequence(in resolved, _length);
    }

    /// <summary>The first <paramref name="count"/> strokes.</summary>
    internal KeySequence Prefix(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, (uint)_length, nameof(count));
        StrokeBuffer prefix = default;
        for (int index = 0; index < count; index++)
        {
            prefix[index] = _strokes[index];
        }

        return new KeySequence(in prefix, count);
    }

    /// <summary>This sequence followed by one more stroke; the caller keeps it within the length limit.</summary>
    internal KeySequence Append(KeyGesture stroke)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(_length, MAX_LENGTH, nameof(stroke));
        StrokeBuffer extended = _strokes;
        extended[_length] = stroke;
        return new KeySequence(in extended, _length + 1);
    }

    private static void Validate(KeyGesture stroke, string parameterName)
    {
        if (stroke.Key == Key.None)
        {
            throw new ArgumentException("A key gesture must specify a key.", parameterName);
        }
    }

    [InlineArray(MAX_LENGTH)]
    private struct StrokeBuffer
    {
        private KeyGesture _first;
    }
}
