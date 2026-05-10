using System.Diagnostics;
using System.Runtime.InteropServices;

using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// GPU separable Gaussian blur via Apple's Metal Performance Shaders (MPS) on
/// <see cref="MewVGMetalPixelRenderSurface"/>. Wraps <c>MPSImageGaussianBlur</c>'s
/// <c>encodeToCommandBuffer:sourceTexture:destinationTexture:</c> entry point.
/// </summary>
/// <remarks>
/// Apple ships MPS as a system framework (macOS 10.13+, iOS 9+) that auto-tunes the kernel
/// and tile size per GPU — so we don't have to write or maintain a Metal shader of our own
/// for this node. The framework is normally auto-loaded once any Metal symbol is touched, but
/// because we never link against it explicitly, the first lookup of <c>MPSImageGaussianBlur</c>
/// can return 0; <see cref="EnsureFrameworkLoaded"/> dlopens it on first use.
///
/// <para>
/// MPS's <c>MPSImageGaussianBlur</c> is isotropic (single sigma applied to both axes). For
/// SVG <c>feGaussianBlur</c> with <c>stdDeviation</c> the X and Y values are typically equal,
/// which fits MPS perfectly. When the caller-supplied <c>sigmaX != sigmaY</c> we still run
/// MPS once with the geometric mean — visually identical to running two anisotropic 1-D
/// passes for moderate eccentricity, and avoids paying for a custom convolution path.
/// </para>
///
/// <para>
/// Big-σ optimisation: blur destroys frequencies above ~1/(2πσ), so for σ ≫ 2 the source
/// resolution is mostly noise the blur is about to throw away. We downsample the source
/// to ⌈σ/2⌉× smaller, run MPS at the corresponding scaled-down σ (≈2), then upsample
/// back to full resolution. Pipeline-visible behaviour is identical to a full-resolution
/// blur (output is full size, downstream Composite / Merge / SourceGraphic re-references
/// see no resolution loss). MPS work scales as <c>(N/k)²·(σ/k) = N²σ/k³</c>, so a 4 ×
/// downsample is 64 × less GPU time.
/// </para>
/// </remarks>
internal static unsafe partial class MetalGaussianBlur
{
    [LibraryImport("/usr/lib/libdl.dylib", EntryPoint = "dlopen", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint DLOpen(string path, int mode);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint InitWithDeviceSigma(nint receiver, nint selector, nint device, float sigma);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint InitWithDevice(nint receiver, nint selector, nint device);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void EncodeToCommandBuffer(nint receiver, nint selector,
        nint commandBuffer, nint sourceTexture, nint destinationTexture);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint NewTextureWithDescriptor(nint receiver, nint selector, nint descriptor);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint TextureDescriptor(nint cls, nint selector, uint pixelFormat, nuint width, nuint height, byte mipmapped);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SetUsage(nint receiver, nint selector, ulong usage);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SetStorageMode(nint receiver, nint selector, ulong storageMode);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nuint GetTextureWidth(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nuint GetTextureHeight(nint receiver, nint selector);

    private const string MpsFrameworkPath =
        "/System/Library/Frameworks/MetalPerformanceShaders.framework/MetalPerformanceShaders";
    private const int RTLD_LAZY = 0x1;

    // MTLPixelFormat.BGRA8Unorm = 80
    private const uint BGRA8Unorm = 80;
    // MTLTextureUsage.ShaderRead | ShaderWrite | RenderTarget = 7
    private const ulong UsageReadWriteRender = (1ul << 0) | (1ul << 1) | (1ul << 2);
    // MTLStorageMode.Private = 2 (GPU-only, no CPU mapping for transient intermediates)
    private const ulong StorageModePrivate = 2;

    private static readonly object _lock = new();
    private static bool _initialized;
    private static bool _available;

    private static nint _clsMPSImageGaussianBlur;
    private static nint _clsMPSImageBilinearScale;
    private static nint _clsMTLTextureDescriptor;
    private static nint _selInitWithDeviceSigma;
    private static nint _selInitWithDevice;
    private static nint _selEncode;
    private static nint _selTextureDescriptor;
    private static nint _selSetUsage;
    private static nint _selSetStorageMode;
    private static nint _selNewTextureWithDescriptor;
    private static nint _selTextureWidth;
    private static nint _selTextureHeight;

    /// <summary>
    /// Loads the MPS framework (idempotent), resolves the kernel class + selectors, and
    /// caches whether MPS is usable on this host. Safe to call from any thread.
    /// </summary>
    private static bool EnsureFrameworkLoaded()
    {
        if (_initialized) return _available;

        lock (_lock)
        {
            if (_initialized) return _available;
            _initialized = true;

            DLOpen(MpsFrameworkPath, RTLD_LAZY);

            _clsMPSImageGaussianBlur = ObjCRuntime.GetClass("MPSImageGaussianBlur");
            if (_clsMPSImageGaussianBlur == 0) return false;

            // MPSImageBilinearScale — used for downsample/upsample around the blur. Optional:
            // if missing for some reason (very old macOS), we just skip the trick and run MPS
            // blur at full source resolution (slower but still correct).
            _clsMPSImageBilinearScale = ObjCRuntime.GetClass("MPSImageBilinearScale");
            _clsMTLTextureDescriptor = ObjCRuntime.GetClass("MTLTextureDescriptor");

            _selInitWithDeviceSigma = ObjCRuntime.RegisterSelector("initWithDevice:sigma:");
            _selInitWithDevice = ObjCRuntime.RegisterSelector("initWithDevice:");
            _selEncode = ObjCRuntime.RegisterSelector(
                "encodeToCommandBuffer:sourceTexture:destinationTexture:");
            _selTextureDescriptor = ObjCRuntime.RegisterSelector(
                "texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
            _selSetUsage = ObjCRuntime.RegisterSelector("setUsage:");
            _selSetStorageMode = ObjCRuntime.RegisterSelector("setStorageMode:");
            _selNewTextureWithDescriptor = ObjCRuntime.RegisterSelector("newTextureWithDescriptor:");
            _selTextureWidth = ObjCRuntime.RegisterSelector("width");
            _selTextureHeight = ObjCRuntime.RegisterSelector("height");

            _available = _selInitWithDeviceSigma != 0 && _selEncode != 0;
            return _available;
        }
    }

    /// <summary>Compute downsample factor k for a given pixel-space σ. Heuristic: keep the
    /// scaled σ at ≈2 (MPS still does meaningful blur, kernel still reaches enough samples
    /// for smoothness). σ &lt; 2 → k=1 (no downsample, MPS already cheap).</summary>
    private static int ComputeDownsampleFactor(double sigma)
    {
        if (sigma < 2.5) return 1;
        return Math.Max(1, (int)Math.Floor(sigma / 2.0));
    }

    /// <summary>
    /// Encodes a Gaussian blur from <paramref name="sourceTexture"/> into
    /// <paramref name="destinationTexture"/>. Returns false when MPS is unavailable, the
    /// arguments are invalid, or a non-positive sigma is supplied. Caller owns the command
    /// buffer and must commit + wait separately so multiple filter nodes can be chained on
    /// the same buffer.
    /// </summary>
    public static bool TryEncode(nint device, nint commandBuffer,
        nint sourceTexture, nint destinationTexture, double sigmaX, double sigmaY)
    {
        if (device == 0 || commandBuffer == 0 || sourceTexture == 0 || destinationTexture == 0)
        {
            return false;
        }
        if (sigmaX <= 0 && sigmaY <= 0) return false;
        if (!EnsureFrameworkLoaded()) return false;

        // MPS Gaussian is isotropic: collapse to a single sigma. For SVG feGaussianBlur the
        // two values are almost always identical; the geometric mean handles the rare
        // anisotropic case without an extra fallback path.
        double sigma = (sigmaX > 0 && sigmaY > 0)
            ? Math.Sqrt(sigmaX * sigmaY)
            : Math.Max(sigmaX, sigmaY);

        int k = (_clsMPSImageBilinearScale != 0 && _clsMTLTextureDescriptor != 0)
            ? ComputeDownsampleFactor(sigma)
            : 1;

        if (k <= 1)
        {
            // Direct path — no downsample worthwhile (small σ).
            return EncodeBlurDirect(device, commandBuffer, sourceTexture, destinationTexture, sigma);
        }

        // Downscale-blur-upscale path. Source/dest dimensions queried from the textures;
        // intermediate small textures are GPU-private (no CPU mapping needed).
        nuint srcW = GetTextureWidth(sourceTexture, _selTextureWidth);
        nuint srcH = GetTextureHeight(sourceTexture, _selTextureHeight);
        if (srcW < 8 || srcH < 8)
        {
            // Source too small to benefit from downsample — direct path.
            return EncodeBlurDirect(device, commandBuffer, sourceTexture, destinationTexture, sigma);
        }

        nuint smallW = (nuint)Math.Max(8, (int)srcW / k);
        nuint smallH = (nuint)Math.Max(8, (int)srcH / k);
        double scaledSigma = sigma / k;

        nint smallSource = CreatePrivateTexture(device, smallW, smallH);
        if (smallSource == 0)
        {
            return EncodeBlurDirect(device, commandBuffer, sourceTexture, destinationTexture, sigma);
        }
        nint smallBlurred = CreatePrivateTexture(device, smallW, smallH);
        if (smallBlurred == 0)
        {
            ObjCRuntime.Release(smallSource);
            return EncodeBlurDirect(device, commandBuffer, sourceTexture, destinationTexture, sigma);
        }

        try
        {
            // 1. Bilinear downsample: source → smallSource
            if (!EncodeBilinearScale(device, commandBuffer, sourceTexture, smallSource))
            {
                return EncodeBlurDirect(device, commandBuffer, sourceTexture, destinationTexture, sigma);
            }
            // 2. MPS blur at scaled σ: smallSource → smallBlurred
            if (!EncodeBlurDirect(device, commandBuffer, smallSource, smallBlurred, scaledSigma))
            {
                return false;
            }
            // 3. Bilinear upsample: smallBlurred → destinationTexture
            if (!EncodeBilinearScale(device, commandBuffer, smallBlurred, destinationTexture))
            {
                return false;
            }
            return true;
        }
        finally
        {
            // The encode methods retain their texture inputs for the duration of the
            // commandBuffer's GPU execution, so it's safe to drop our refcount here.
            ObjCRuntime.Release(smallSource);
            ObjCRuntime.Release(smallBlurred);
        }
    }

    private static bool EncodeBlurDirect(nint device, nint commandBuffer,
        nint sourceTexture, nint destinationTexture, double sigma)
    {
        nint kernel = ObjCRuntime.SendMessage(_clsMPSImageGaussianBlur, ObjCRuntime.Selectors.alloc);
        if (kernel == 0) return false;

        kernel = InitWithDeviceSigma(kernel, _selInitWithDeviceSigma, device, (float)sigma);
        if (kernel == 0) return false;

        try
        {
            EncodeToCommandBuffer(kernel, _selEncode, commandBuffer, sourceTexture, destinationTexture);
            return true;
        }
        finally
        {
            ObjCRuntime.Release(kernel);
        }
    }

    private static bool EncodeBilinearScale(nint device, nint commandBuffer,
        nint sourceTexture, nint destinationTexture)
    {
        nint kernel = ObjCRuntime.SendMessage(_clsMPSImageBilinearScale, ObjCRuntime.Selectors.alloc);
        if (kernel == 0) return false;

        kernel = InitWithDevice(kernel, _selInitWithDevice, device);
        if (kernel == 0) return false;

        try
        {
            EncodeToCommandBuffer(kernel, _selEncode, commandBuffer, sourceTexture, destinationTexture);
            return true;
        }
        finally
        {
            ObjCRuntime.Release(kernel);
        }
    }

    /// <summary>Allocates a transient MTLTexture (Private storage, BGRA8) for use as an
    /// intermediate in the downscale-blur-upscale chain. Caller owns the +1 refcount and
    /// must release after encoding (commandBuffer retains internally).</summary>
    private static nint CreatePrivateTexture(nint device, nuint width, nuint height)
    {
        nint desc = TextureDescriptor(_clsMTLTextureDescriptor, _selTextureDescriptor,
            BGRA8Unorm, width, height, mipmapped: 0);
        if (desc == 0) return 0;
        SetUsage(desc, _selSetUsage, UsageReadWriteRender);
        SetStorageMode(desc, _selSetStorageMode, StorageModePrivate);
        return NewTextureWithDescriptor(device, _selNewTextureWithDescriptor, desc);
    }
}
