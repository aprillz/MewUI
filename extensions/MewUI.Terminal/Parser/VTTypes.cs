// Ported from Windows Terminal src/terminal/adapter/DispatchTypes.hpp (MIT): VTID, VTIDBuilder and the parameter types.

namespace Aprillz.MewUI.Terminal.Parser;

/// <summary>
/// A sequence identifier: the intermediate and final characters of an escape sequence packed into an integer, first
/// character in the lowest byte. "?h" is <c>'?' | 'h' &lt;&lt; 8</c>.
/// </summary>
internal readonly struct VTID : IEquatable<VTID>
{
    private readonly ulong _value;

    public VTID(ulong value)
    {
        _value = value & 0x00FFFFFFFFFFFFFF;
    }

    public ulong Value => _value;

    /// <summary>Packs a short ASCII string the way the parser builds identifiers.</summary>
    public static VTID FromString(string text)
    {
        ulong value = 0;
        for (int index = text.Length - 1; index >= 0; index--)
        {
            value = (value << 8) + text[index];
        }

        return new VTID(value);
    }

    /// <summary>The character at <paramref name="offset"/>.</summary>
    public char this[int offset] => (char)(SubSequence(offset)._value & 0xFF);

    public VTID SubSequence(int offset) => new(_value >> (8 * offset));

    public static implicit operator ulong(VTID id) => id._value;

    public static implicit operator VTID(ulong value) => new(value);

    public bool Equals(VTID other) => _value == other._value;

    public override bool Equals(object? obj) => obj is VTID other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public override string ToString()
    {
        var builder = new System.Text.StringBuilder();
        for (ulong value = _value; value != 0; value >>= 8)
        {
            builder.Append((char)(value & 0xFF));
        }

        return builder.ToString();
    }
}

/// <summary>Accumulates intermediates and the final character into a <see cref="VTID"/>.</summary>
internal struct VTIDBuilder
{
    private ulong _idAccumulator;
    private int _idShift;

    public void Clear()
    {
        _idAccumulator = 0;
        _idShift = 0;
    }

    public void AddIntermediate(char intermediate)
    {
        // Without room for another intermediate plus the final and a terminator, the id becomes all zero
        // intermediates, which matches nothing.
        if (_idShift + 16 >= 64)
        {
            _idAccumulator = 0;
        }
        else
        {
            _idAccumulator += (ulong)intermediate << _idShift;
            _idShift += 8;
        }
    }

    public readonly VTID Finalize(char final) => new(_idAccumulator + ((ulong)final << _idShift));
}

/// <summary>
/// One numeric parameter; negative means omitted. Converted to an int it reads as a count (omitted or 0 is 1); use
/// <see cref="ValueOr"/> for selective parameters, whose default is 0.
/// </summary>
internal readonly struct VTParameter
{
    // The value plus one, so that the default struct is an omitted parameter.
    private readonly int _encoded;

    public VTParameter(int value)
    {
        _encoded = value < 0 ? 0 : value + 1;
    }

    public static VTParameter Omitted => default;

    private int RawValue => _encoded - 1;

    public bool HasValue => RawValue >= 0;

    public int Value => RawValue;

    public int ValueOr(int defaultValue) => RawValue < 0 ? defaultValue : RawValue;

    /// <summary>The parameter as a selective value: omitted reads as 0.</summary>
    public int Selective => ValueOr(0);

    public static implicit operator int(VTParameter parameter) => parameter.RawValue <= 0 ? 1 : parameter.RawValue;

    public static implicit operator VTParameter(int value) => new(value);

    public override string ToString() => HasValue ? RawValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(omitted)";
}

/// <summary>The sub-parameters of one parameter (the <c>:</c>-separated values in SGR).</summary>
internal readonly ref struct VTSubParameters
{
    private readonly ReadOnlySpan<VTParameter> _subParams;

    public VTSubParameters(ReadOnlySpan<VTParameter> subParams)
    {
        _subParams = subParams;
    }

    public VTParameter At(int index) => index < _subParams.Length ? _subParams[index] : VTParameter.Omitted;

    public VTSubParameters Subspan(int offset, int count) => new(_subParams.Slice(offset, count));

    public bool IsEmpty => _subParams.IsEmpty;

    public int Size => _subParams.Length;

    public ReadOnlySpan<VTParameter> AsSpan() => _subParams;
}

/// <summary>
/// The parameters of a sequence, with their sub-parameters. An empty list counts as one omitted parameter.
/// </summary>
internal readonly ref struct VTParameters
{
    private readonly ReadOnlySpan<VTParameter> _params;
    private readonly ReadOnlySpan<VTParameter> _subParams;
    private readonly ReadOnlySpan<(byte Start, byte End)> _subParamRanges;

    public VTParameters(ReadOnlySpan<VTParameter> parameters)
    {
        _params = parameters;
        _subParams = default;
        _subParamRanges = default;
    }

    public VTParameters(ReadOnlySpan<VTParameter> parameters, ReadOnlySpan<VTParameter> subParams, ReadOnlySpan<(byte Start, byte End)> subParamRanges)
    {
        _params = parameters;
        _subParams = subParams;
        _subParamRanges = subParamRanges;
    }

    public VTParameter At(int index) => index < _params.Length ? _params[index] : VTParameter.Omitted;

    public bool IsEmpty => _params.IsEmpty;

    /// <summary>At least 1: no parameters means one omitted parameter.</summary>
    public int Size => Math.Max(_params.Length, 1);

    /// <summary>The parameters from <paramref name="offset"/> on, keeping the sub-parameter indexes valid.</summary>
    public VTParameters Subspan(int offset)
    {
        var newParams = _params[Math.Min(offset, _params.Length)..];
        var newRanges = _subParamRanges[Math.Min(offset, _subParamRanges.Length)..];
        return new VTParameters(newParams, _subParams, newRanges);
    }

    public VTSubParameters SubParamsFor(int index)
    {
        if (index < _subParamRanges.Length)
        {
            var range = _subParamRanges[index];
            return new VTSubParameters(_subParams.Slice(range.Start, range.End - range.Start));
        }

        return default;
    }

    public bool HasSubParams => !_subParams.IsEmpty;

    public bool HasSubParamsFor(int index)
    {
        if (index < _subParamRanges.Length)
        {
            var range = _subParamRanges[index];
            return range.End > range.Start;
        }

        return false;
    }

    /// <summary>The parameters, or one omitted parameter when there are none.</summary>
    public ReadOnlySpan<VTParameter> Values => _params.IsEmpty ? _defaultParameters : _params;

    private static readonly VTParameter[] _defaultParameters = [VTParameter.Omitted];
}
