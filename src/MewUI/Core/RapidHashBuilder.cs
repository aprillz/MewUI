using System.Runtime.InteropServices;

namespace Aprillz.MewUI;

/// <summary>
/// Folds a sequence of values into one 64-bit <see cref="RapidHash"/> without allocating: each
/// value is hashed with the running hash as its seed, so the same values in a different order or
/// split differently produce a different result.
/// </summary>
internal ref struct RapidHashBuilder
{
    private ulong _hash;

    public ulong Hash => _hash;

    public void Add(ReadOnlySpan<char> value)
        => _hash = RapidHash.Hash(MemoryMarshal.AsBytes(value), _hash);

    /// <summary>Adds a string, keeping null distinct from empty.</summary>
    public void Add(string? value)
    {
        if (value == null)
        {
            Add(-1);
        }
        else
        {
            Add(value.Length);
            Add(value.AsSpan());
        }
    }

    public void Add(ulong value) => _hash = RapidHash.Combine(_hash, value);

    public void Add(int value) => Add((ulong)(uint)value);

    public void Add(uint value) => Add((ulong)value);

    public void Add(double value) => Add(BitConverter.DoubleToUInt64Bits(value));

    public void Add(bool value) => Add(value ? 1UL : 0UL);
}
