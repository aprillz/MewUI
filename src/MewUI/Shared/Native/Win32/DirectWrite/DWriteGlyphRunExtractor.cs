using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Native.Com;

namespace Aprillz.MewUI.Native.DirectWrite;

/// <summary>
/// Copies the transient glyph-run data exposed by IDWriteTextLayout::Draw into managed memory.
/// This is the feasibility boundary for the new text engine; no COM handle escapes the call.
/// </summary>
internal static unsafe class DWriteGlyphRunExtractor
{
    private const int S_OK = 0;
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int DrawIndex = 58;

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDWritePixelSnapping = new("EAF3A2DA-ECF4-4D24-B644-B34F6842024B");
    private static readonly Guid IID_IDWriteTextRenderer = new("EF8A8135-5CC6-45FE-8825-C5A0724EB819");
    private static readonly void** RendererVTable = CreateRendererVTable();

    internal sealed record GlyphRun(
        uint TextPosition,
        uint TextLength,
        float BaselineOriginX,
        float BaselineOriginY,
        float FontEmSize,
        int FaceIndex,
        uint BidiLevel,
        bool IsSideways,
        nint FontFace,
        ushort[] GlyphIndices,
        float[] Advances,
        DWRITE_GLYPH_OFFSET[] Offsets,
        ushort[] ClusterMap) : IDisposable
    {
        private nint _ownedFontFace = FontFace;
        internal bool HasOwnedFontFace => Volatile.Read(ref _ownedFontFace) != 0;

        public void Dispose()
        {
            nint face = Interlocked.Exchange(ref _ownedFontFace, 0);
            if (face != 0) ComHelpers.Release(face);
            GC.SuppressFinalize(this);
        }

        ~GlyphRun()
        {
            nint face = Interlocked.Exchange(ref _ownedFontFace, 0);
            if (face != 0) ComHelpers.Release(face);
        }
    }

    /// <summary>
    /// Copies the runs of <paramref name="textLayout"/>. DirectWrite sets each baseline on the pixel grid of
    /// <paramref name="pixelsPerDip"/>, so it must be the scale the runs are drawn at; 1 keeps them on whole
    /// layout units.
    /// </summary>
    public static IReadOnlyList<GlyphRun> Capture(nint textLayout, float pixelsPerDip, bool retainFontFaces = false)
    {
        if (textLayout == 0)
        {
            throw new ArgumentException("A valid IDWriteTextLayout is required.", nameof(textLayout));
        }

        var state = new CaptureState(retainFontFaces);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            var renderer = new Renderer
            {
                VTable = RendererVTable,
                ReferenceCount = 1,
                StateHandle = GCHandle.ToIntPtr(stateHandle),
                PixelsPerDip = pixelsPerDip > 0 ? pixelsPerDip : 1
            };

            var vtable = *(void***)textLayout;
            var draw = (delegate* unmanaged[Stdcall]<nint, nint, nint, float, float, int>)vtable[DrawIndex];
            int hr = draw(textLayout, 0, (nint)(&renderer), 0, 0);
            if (state.Error is Exception callbackError)
            {
                DisposeRuns(state.Runs);
                throw new InvalidOperationException(
                    "DirectWrite failed while copying a glyph run.",
                    callbackError);
            }
            if (hr < 0)
            {
                DisposeRuns(state.Runs);
                Marshal.ThrowExceptionForHR(hr);
            }

            return state.Runs.ToArray();
        }
        finally
        {
            stateHandle.Free();
        }
    }

    private static void DisposeRuns(List<GlyphRun> runs)
    {
        foreach (var run in runs)
        {
            run.Dispose();
        }
        runs.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Renderer
    {
        public void** VTable;
        public int ReferenceCount;
        public nint StateHandle;
        public float PixelsPerDip;
    }

    private sealed class CaptureState(bool retainFontFaces)
    {
        public List<GlyphRun> Runs { get; } = [];
        public Dictionary<nint, int> FaceIndices { get; } = [];
        public bool RetainFontFaces { get; } = retainFontFaces;
        public Exception? Error { get; set; }

        public int GetFaceIndex(nint fontFace)
        {
            if (!FaceIndices.TryGetValue(fontFace, out int index))
            {
                index = FaceIndices.Count;
                FaceIndices.Add(fontFace, index);
            }

            return index;
        }
    }

    private static void** CreateRendererVTable()
    {
        const int methodCount = 10;
        var table = (void**)RuntimeHelpers.AllocateTypeAssociatedMemory(
            typeof(DWriteGlyphRunExtractor), methodCount * sizeof(nint));
        table[0] = (delegate* unmanaged[Stdcall]<Renderer*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged[Stdcall]<Renderer*, uint>)&AddRef;
        table[2] = (delegate* unmanaged[Stdcall]<Renderer*, uint>)&Release;
        table[3] = (delegate* unmanaged[Stdcall]<Renderer*, nint, int*, int>)&IsPixelSnappingDisabled;
        table[4] = (delegate* unmanaged[Stdcall]<Renderer*, nint, DWRITE_MATRIX*, int>)&GetCurrentTransform;
        table[5] = (delegate* unmanaged[Stdcall]<Renderer*, nint, float*, int>)&GetPixelsPerDip;
        table[6] = (delegate* unmanaged[Stdcall]<Renderer*, nint, float, float, DWRITE_MEASURING_MODE, DWRITE_GLYPH_RUN*, DWRITE_GLYPH_RUN_DESCRIPTION*, nint, int>)&DrawGlyphRun;
        table[7] = (delegate* unmanaged[Stdcall]<Renderer*, nint, float, float, void*, nint, int>)&IgnoreDecoration;
        table[8] = (delegate* unmanaged[Stdcall]<Renderer*, nint, float, float, void*, nint, int>)&IgnoreDecoration;
        table[9] = (delegate* unmanaged[Stdcall]<Renderer*, nint, float, float, nint, int, int, nint, int>)&IgnoreInlineObject;
        return table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Renderer* self, Guid* iid, void** result)
    {
        if (result == null)
        {
            return E_NOINTERFACE;
        }

        *result = null;
        if (iid != null && (*iid == IID_IUnknown || *iid == IID_IDWritePixelSnapping || *iid == IID_IDWriteTextRenderer))
        {
            *result = self;
            _ = AddRefCore(self);
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(Renderer* self) => AddRefCore(self);

    private static uint AddRefCore(Renderer* self)
        => self == null ? 0 : (uint)Interlocked.Increment(ref self->ReferenceCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(Renderer* self)
        => self == null ? 0 : (uint)Math.Max(0, Interlocked.Decrement(ref self->ReferenceCount));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int IsPixelSnappingDisabled(Renderer* self, nint clientContext, int* disabled)
    {
        if (disabled != null)
        {
            *disabled = 0;
        }

        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetCurrentTransform(Renderer* self, nint clientContext, DWRITE_MATRIX* transform)
    {
        if (transform != null)
        {
            *transform = new DWRITE_MATRIX { m11 = 1, m22 = 1 };
        }

        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetPixelsPerDip(Renderer* self, nint clientContext, float* pixelsPerDip)
    {
        if (pixelsPerDip != null)
        {
            *pixelsPerDip = self == null ? 1 : self->PixelsPerDip;
        }

        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DrawGlyphRun(
        Renderer* self,
        nint clientContext,
        float baselineOriginX,
        float baselineOriginY,
        DWRITE_MEASURING_MODE measuringMode,
        DWRITE_GLYPH_RUN* glyphRun,
        DWRITE_GLYPH_RUN_DESCRIPTION* description,
        nint drawingEffect)
    {
        if (self == null || glyphRun == null)
        {
            return S_OK;
        }

        CaptureState? state = null;
        try
        {
            state = (CaptureState)GCHandle.FromIntPtr(self->StateHandle).Target!;
            return DrawGlyphRunCore(
                state,
                baselineOriginX,
                baselineOriginY,
                glyphRun,
                description);
        }
        catch (Exception exception)
        {
            if (state is not null)
            {
                state.Error ??= exception;
            }
            return E_FAIL;
        }
    }

    private static int DrawGlyphRunCore(
        CaptureState state,
        float baselineOriginX,
        float baselineOriginY,
        DWRITE_GLYPH_RUN* glyphRun,
        DWRITE_GLYPH_RUN_DESCRIPTION* description)
    {
        int glyphCount = checked((int)glyphRun->glyphCount);
        int textLength = description == null ? 0 : checked((int)description->textLength);

        var indices = new ushort[glyphCount];
        var advances = new float[glyphCount];
        var offsets = new DWRITE_GLYPH_OFFSET[glyphCount];
        var clusters = new ushort[textLength];

        if (glyphCount > 0)
        {
            new ReadOnlySpan<ushort>(glyphRun->glyphIndices, glyphCount).CopyTo(indices);
            new ReadOnlySpan<float>(glyphRun->glyphAdvances, glyphCount).CopyTo(advances);
            if (glyphRun->glyphOffsets != null)
            {
                new ReadOnlySpan<DWRITE_GLYPH_OFFSET>(glyphRun->glyphOffsets, glyphCount).CopyTo(offsets);
            }
        }

        if (textLength > 0 && description->clusterMap != null)
        {
            new ReadOnlySpan<ushort>(description->clusterMap, textLength).CopyTo(clusters);
        }

        nint retainedFace = 0;
        try
        {
            if (state.RetainFontFaces && glyphRun->fontFace != 0)
            {
                ComHelpers.AddRef(glyphRun->fontFace);
                retainedFace = glyphRun->fontFace;
            }

            var run = new GlyphRun(
                description == null ? 0 : description->textPosition,
                description == null ? 0 : description->textLength,
                baselineOriginX,
                baselineOriginY,
                glyphRun->fontEmSize,
                state.GetFaceIndex(glyphRun->fontFace),
                glyphRun->bidiLevel,
                glyphRun->isSideways != 0,
                retainedFace,
                indices,
                advances,
                offsets,
                clusters);
            retainedFace = 0;
            state.Runs.Add(run);
        }
        finally
        {
            if (retainedFace != 0)
            {
                ComHelpers.Release(retainedFace);
            }
        }
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int IgnoreDecoration(Renderer* self, nint clientContext, float x, float y, void* decoration, nint drawingEffect)
        => S_OK;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int IgnoreInlineObject(Renderer* self, nint clientContext, float x, float y, nint inlineObject, int isSideways, int isRightToLeft, nint drawingEffect)
        => S_OK;
}
