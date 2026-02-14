using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Aprillz.MewUI.Rendering.Gdi.Simd;

/// <summary>
/// Detects and reports SIMD capabilities of the current CPU.
/// </summary>
internal static class SimdCapabilities
{
    /// <summary>
    /// Gets whether SSE2 instructions are supported.
    /// </summary>
    public static bool HasSse2 { get; } = Sse2.IsSupported;

    /// <summary>
    /// Gets whether AVX2 instructions are supported.
    /// </summary>
    public static bool HasAvx2 { get; } = Avx2.IsSupported;

    /// <summary>
    /// Gets whether AVX-512 instructions are supported (future).
    /// </summary>
    public static bool HasAvx512 { get; } = false; // Avx512F.IsSupported - not yet in .NET

    /// <summary>
    /// Gets whether ARM NEON instructions are supported.
    /// </summary>
    public static bool HasNeon { get; } = AdvSimd.IsSupported;

    /// <summary>
    /// Gets the best available SIMD level.
    /// </summary>
    public static SimdLevel BestLevel { get; } = DetectBestLevel();

    private static SimdLevel DetectBestLevel()
    {
        if (HasAvx2)
        {
            return SimdLevel.Avx2;
        }

        if (HasSse2)
        {
            return SimdLevel.Sse2;
        }

        if (HasNeon)
        {
            return SimdLevel.Neon;
        }

        return SimdLevel.Scalar;
    }

    /// <summary>
    /// Gets a description of the current SIMD capabilities.
    /// </summary>
    public static string GetDescription()
    {
        var features = new List<string>();

        if (HasAvx2) features.Add("AVX2");
        if (HasSse2) features.Add("SSE2");
        if (HasNeon) features.Add("NEON");

        if (features.Count == 0)
        {
            return "Scalar (no SIMD)";
        }

        return string.Join(", ", features);
    }
}

/// <summary>
/// SIMD instruction set levels.
/// </summary>
internal enum SimdLevel
{
    /// <summary>
    /// No SIMD, scalar operations only.
    /// </summary>
    Scalar = 0,

    /// <summary>
    /// SSE2 (128-bit vectors, 16 bytes).
    /// </summary>
    Sse2 = 1,

    /// <summary>
    /// AVX2 (256-bit vectors, 32 bytes).
    /// </summary>
    Avx2 = 2,

    /// <summary>
    /// ARM NEON (128-bit vectors, 16 bytes).
    /// </summary>
    Neon = 3,

    /// <summary>
    /// AVX-512 (512-bit vectors, 64 bytes) - future.
    /// </summary>
    Avx512 = 4
}
