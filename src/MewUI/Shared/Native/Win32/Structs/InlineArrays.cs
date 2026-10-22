using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Native.Structs;

/// <summary>Inline array of 32 bytes for fixed-size fields of native structs.</summary>
[InlineArray(32)]
public struct Byte32
{
    /// <summary>The first element; the remaining 31 follow it inline.</summary>
    public byte Element0;
}

/// <summary>Inline array of 32 UTF-16 code units for fixed-size fields of native structs.</summary>
[InlineArray(32)]
public struct Char32
{
    /// <summary>The first element; the remaining 31 follow it inline.</summary>
    public char Element0;
}

