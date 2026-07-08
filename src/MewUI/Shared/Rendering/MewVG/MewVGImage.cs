using Aprillz.MewUI.Resources;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGImage : IImage
{
    private readonly IPixelBufferSource? _source;
    // Cached GPU-tier view of _source. Non-null iff source exposes a live GPU texture
    // (FBO color attachment, MTLTexture, etc.). The GPU contract (GetTextureHandle,
    // RetainGpuHandle/ReleaseGpuHandle, ConfigureGpuTextureWrap) lives on
    // IGpuTextureSource - IPixelBufferSource is CPU-only, so we resolve once at
    // construction instead of pattern-matching on every frame.
    private readonly IGpuTextureSource? _gpuSource;
    private readonly Action<MewVGImage>? _disposeRequested;
    private readonly bool _sourceIsPremultiplied;
    private int _sourceVersion = -1;
    private readonly Dictionary<ImageKey, ImageEntry> _images = new();
    private bool _disposed;
    private bool _deferred;
    // Currently-retained zero-copy GPU handle (Metal MTLTexture today; could extend to GL).
    // Held until ReleaseImagesImmediate so the wrapped texture outlives any in-flight draw
    // command buffer that referenced it via NoDelete; without this, a scratch surface disposed
    // mid-frame can free the texture while NVG-Metal still has a NoDelete pointer queued
    // for setFragmentTexture, which dereferences the freed object on commit (SIGSEGV).
    private nint _retainedGpuHandle;
    // Optional callback invoked by ReleaseImagesImmediate after all NVG image-ids and the
    // retained GPU handle have been released. Used by zero-copy scratch paths to defer
    // returning a pooled render surface until the in-flight NVG draws referencing it
    // have flushed - without this the pool can hand the same RT to the next filter node in
    // the same eval, which then MPS-overwrites the ColorTexture while a queued draw still
    // points at it via NoDelete (visible as cross-filter / cross-frame content bleed).
    private Action? _postReleaseCallback;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    /// <inheritdoc cref="IImage.TrySetPostReleaseCallback"/>
    public bool TrySetPostReleaseCallback(Action callback)
    {
        // Last write wins: zero-copy scratch paths set this exactly once per
        // MewVGImage instance before disposing, so the simple assignment is sufficient.
        _postReleaseCallback = callback;
        return true;
    }

    private readonly record struct ImageEntry(int ImageId, int Version);
    private readonly record struct ImageKey(NanoVG Vg, NVGimageFlags Flags);

    public MewVGImage(int widthPx, int heightPx, byte[] bgra, Action<MewVGImage>? disposeRequested = null)
    {
        PixelWidth = widthPx;
        PixelHeight = heightPx;
        _disposeRequested = disposeRequested;
        ArgumentNullException.ThrowIfNull(bgra);
        _source = new StaticPixelBufferSource(widthPx, heightPx, bgra);
        _gpuSource = _source as IGpuTextureSource;
        _sourceVersion = 0;
    }

    public MewVGImage(IPixelBufferSource source, Action<MewVGImage>? disposeRequested = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        PixelWidth = source.PixelWidth;
        PixelHeight = source.PixelHeight;
        _disposeRequested = disposeRequested;
        _source = source;
        _gpuSource = source as IGpuTextureSource;
        _sourceVersion = source.Version;
        _sourceIsPremultiplied = source.IsPremultiplied;
    }

    public int GetOrCreateImageId(NanoVG vg)
        => GetOrCreateImageId(vg, NVGimageFlags.None);

    public int GetOrCreateImageId(NanoVG vg, NVGimageFlags flags)
    {
        if (_disposed)
        {
            return 0;
        }

        // Tell NVG the texels are already premultiplied - without this, the shader would
        // multiply RGB by alpha a second time at sample, dimming every semi-transparent
        // pixel (visible as a black halo on alpha-blended edges).
        if (_sourceIsPremultiplied)
        {
            flags |= NVGimageFlags.Premultiplied;
        }

        int version = _source?.Version ?? 0;
        if (_sourceVersion != version)
        {
            _sourceVersion = version;
            // Drop cached images for all flags on version change.
            if (_images.Count != 0)
            {
                while (true)
                {
                    bool found = false;
                    foreach (var pair in _images)
                    {
                        if (ReferenceEquals(pair.Key.Vg, vg))
                        {
                            if (pair.Value.ImageId != 0)
                                vg.DeleteImage(pair.Value.ImageId);
                            _images.Remove(pair.Key);
                            found = true;
                            break;
                        }
                    }
                    if (!found) break;
                }
            }
        }

        var imageKey = new ImageKey(vg, flags);
        if (_images.TryGetValue(imageKey, out var entry) && entry.ImageId != 0 && entry.Version == version)
        {
            return entry.ImageId;
        }

        if (entry.ImageId != 0)
        {
            vg.DeleteImage(entry.ImageId);
        }

        // Zero-copy fast path: if the source exposes a live GPU texture (FBO color
        // attachment for GL, MTLTexture for Metal), wrap it directly. Skips the
        // readback + RGBA conversion + re-upload round-trip that's the dominant cost
        // when a scene has many filtered elements (each blur would otherwise hit
        // a glReadPixels / getBytes sync barrier per filter - 100 filters ≈ 1 s of stalls).
        // NoDelete tells NVG the texture is externally owned (we keep it alive in the
        // pixel surface), so DeleteImage only drops NVG's bookkeeping record.
        nint mtlTex = _gpuSource?.GetTextureHandle() ?? 0;
        if (mtlTex != 0)
        {
            // Take an explicit retain on the source's GPU texture before NVG starts referencing
            // it via NoDelete. The source (typically a scratch render surface) may be
            // disposed before the consumer's command buffer commits - the retain keeps the
            // texture alive until ReleaseImagesImmediate runs, which the offscreen provider
            // drains AFTER the frame's command buffer has been submitted.
            //
            // We only retain when the underlying handle changes (source version bump caused a
            // new texture allocation, or first wrap on this MewVGImage instance). Re-retaining
            // the same handle on every call would leak refcounts.
            if (_retainedGpuHandle != mtlTex)
            {
                if (_retainedGpuHandle != 0)
                {
                    _gpuSource!.ReleaseGpuHandle(_retainedGpuHandle);
                    _retainedGpuHandle = 0;
                }
                if (_gpuSource!.RetainGpuHandle(mtlTex))
                {
                    _retainedGpuHandle = mtlTex;
                }
            }

            // Backend-agnostic external-texture wrapper. Returns 0 on backends that
            // don't support it (GL backend → falls through to GL handle path below).
            // No FlipY: MTLTexture's pixel(0,0) is top-left in Metal's convention, the
            // same as NVG's logical origin. The GL path needs FlipY because GL FBOs
            // store rendered content bottom-up in texture memory; Metal stores top-down,
            // so sampling lines up directly.
            int handleId = vg.CreateImageFromNativeHandle(mtlTex, PixelWidth, PixelHeight,
                flags | NVGimageFlags.NoDelete);
            if (handleId != 0)
            {
                _images[imageKey] = new ImageEntry(handleId, version);
                return handleId;
            }
        }

        nint glHandle = _gpuSource?.GetTextureHandle() ?? 0;
        if (glHandle != 0)
        {
            // Same retain discipline as the Metal path - extend the texture's lifetime past
            // _source.Dispose so the consumer's NVG flush still finds the FBO color
            // attachment alive. OpenGLPixelRenderSurface tracks an explicit refcount and
            // defers actual glDeleteTextures until the last release.
            if (_retainedGpuHandle != glHandle)
            {
                if (_retainedGpuHandle != 0)
                {
                    _gpuSource!.ReleaseGpuHandle(_retainedGpuHandle);
                    _retainedGpuHandle = 0;
                }
                if (_gpuSource!.RetainGpuHandle(glHandle))
                {
                    _retainedGpuHandle = glHandle;
                }
            }

            // FlipY: GL texture sampling is bottom-up in normalized texcoords, but the FBO
            // was rendered with NVG's top-left origin convention. Without FlipY, the wrapped
            // image appears upside-down.
            // NoDelete: the texture is owned by the pixel surface (FBO color attachment). NVG
            // must release only its own bookkeeping record on DeleteImage, not the texture.
            //
            // Wrap mode: NVG's CreateImageFromHandle stores the Repeat flags but does NOT
            // touch the external texture's wrap state (NoDelete = "don't mutate"). At sample
            // time the shader uses texture2D() which honors the texture's own
            // GL_TEXTURE_WRAP_S/T. Backends whose RT texture defaults to CLAMP_TO_EDGE need
            // to upgrade the wrap mode based on the requested flags here - the source
            // exposes this via ConfigureGpuTextureWrap (no-op on backends that don't need it).
            bool repeatX = (flags & NVGimageFlags.RepeatX) != 0;
            bool repeatY = (flags & NVGimageFlags.RepeatY) != 0;
            _gpuSource!.ConfigureGpuTextureWrap(glHandle, repeatX, repeatY);

            int handleId = vg.CreateImageFromHandle((int)glHandle, PixelWidth, PixelHeight,
                flags | NVGimageFlags.FlipY | NVGimageFlags.NoDelete);
            if (handleId != 0)
            {
                _images[imageKey] = new ImageEntry(handleId, version);
                return handleId;
            }
            // CreateImageFromHandle failed (e.g. backend mismatch) - fall through to CPU
            // upload path below.
        }

        // Source pixels are BGRA (the MewUI-wide convention). Hand them straight to NVG via
        // the BGRA upload path - GL uses GL_BGRA + UnsignedInt_8_8_8_8_Rev, Metal uses
        // BGRA8Unorm. Backends without native BGRA support inherit the base class fallback
        // that does a one-time CPU swap. The previous code unconditionally swapped into a
        // managed _rgbaCache buffer (8MB+ alloc per image, full memory pass per upload).
        if (_source == null)
        {
            return 0;
        }

        using var l = _source.Lock();
        int imageId = vg.CreateImageBGRA(PixelWidth, PixelHeight, flags, l.Buffer);
        _images[imageKey] = new ImageEntry(imageId, version);
        return imageId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_deferred && _images.Count > 0 && _disposeRequested is { } hook)
        {
            _deferred = true;
            hook(this);
            return;
        }

        ReleaseImagesImmediate();
    }

    /// <summary>
    /// Snapshot of (vg, flags) pairs currently in <see cref="_images"/>. Used by the
    /// offscreen provider's deferred-disposal queue to split this image's pending NVG
    /// handle deletions into per-NVG buckets - each NVG drains its own bucket from its
    /// own EndFrame on the thread that owns it, avoiding the cross-thread NVG-instance
    /// state mutation that corrupts NanoVG's image-id table when an unrelated thread
    /// calls <c>vg.DeleteImage</c> while the NVG is mid-frame elsewhere.
    /// </summary>
    internal IReadOnlyList<(NanoVG Vg, NVGimageFlags Flags)> SnapshotPendingEntries()
    {
        if (_images.Count == 0) return Array.Empty<(NanoVG, NVGimageFlags)>();
        var result = new List<(NanoVG, NVGimageFlags)>(_images.Count);
        foreach (var key in _images.Keys)
        {
            result.Add((key.Vg, key.Flags));
        }
        return result;
    }

    /// <summary>
    /// Releases the NVG image-id for a single (vg, flags) entry - invoked by the per-NVG
    /// drain on the thread that owns <paramref name="vg"/> at <c>EndFrame</c> time. Once
    /// every entry has been released the image's GPU retain (if any) is dropped and the
    /// post-release callback fires.
    /// </summary>
    internal void ReleasePendingEntry(NanoVG vg, NVGimageFlags flags)
    {
        if (_disposed) return;

        var key = new ImageKey(vg, flags);
        if (_images.TryGetValue(key, out var entry))
        {
            if (entry.ImageId != 0)
            {
                vg.DeleteImage(entry.ImageId);
            }
            _images.Remove(key);
        }

        if (_images.Count == 0)
        {
            FinalizeRelease();
        }
    }

    /// <summary>
    /// Whole-image release used for inline disposal (no NVG entries to drain) and for
    /// shutdown flush. Calls <c>vg.DeleteImage</c> for every entry - caller is responsible
    /// for ensuring NVG instances aren't being used concurrently.
    /// </summary>
    internal void ReleaseImagesImmediate()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var pair in _images)
        {
            int imageId = pair.Value.ImageId;
            if (imageId != 0)
            {
                pair.Key.Vg.DeleteImage(imageId);
            }
        }

        _images.Clear();
        FinalizeRelease();
    }

    /// <summary>
    /// Final-stage cleanup that runs once every NVG entry has been released (either via
    /// per-NVG drain or inline immediate release). Marks the image disposed, releases the
    /// retained zero-copy GPU handle, and fires the post-release callback (typically the
    /// scratch surface pool return).
    /// </summary>
    private void FinalizeRelease()
    {
        if (_disposed) return;
        _disposed = true;

        // Now that NVG has dropped its NoDelete bookkeeping for the wrapped textures,
        // release the explicit retain we took during the zero-copy wrap. Safe even when the
        // source itself is disposed; ReleaseGpuHandle on the pixel surface only forwards to
        // objc_release, which is independent of the wrapper's lifecycle.
        if (_retainedGpuHandle != 0)
        {
            _gpuSource?.ReleaseGpuHandle(_retainedGpuHandle);
            _retainedGpuHandle = 0;
        }

        // Fire the post-release callback (e.g. scratch surface pool return) AFTER NVG handles
        // and the retained texture are released; at this point every NVG that had an
        // image-id for our texture has run through DeleteImage on its own thread, so the
        // pixel surface's color attachment is no longer referenced by any NVG draw queue.
        if (_postReleaseCallback is { } cb)
        {
            _postReleaseCallback = null;
            cb();
        }
    }
}
