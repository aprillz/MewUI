using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Native COM implementation of <c>ID2D1SimplifiedGeometrySink</c> (the base interface
/// of <c>IDWriteGeometrySink</c>) that forwards <c>BeginFigure</c> / <c>AddLines</c> /
/// <c>AddBeziers</c> / <c>EndFigure</c> callbacks from <c>IDWriteFontFace::GetGlyphRunOutline</c>
/// directly into a <see cref="PathGeometry"/>. Replaces the GDI <c>GetGlyphOutlineW</c>
/// path that suffered from chained-midpoint quadratic parsing, hinted-grid snapping,
/// and DPI mismatches.
/// </summary>
internal static unsafe class DWriteGeometrySink
{
    // ID2D1SimplifiedGeometrySink IID - needed for QueryInterface response.
    private static readonly Guid _iidSimplifiedSink = new("2cd9069e-12e2-11dc-9fed-001143a055f9");
    private static readonly Guid _iidUnknown = new("00000000-0000-0000-c000-000000000046");

    // Single shared vtable for every Sink instance (stateless dispatch - `self` is the
    // first parameter and carries the instance state via the GCHandle slot).
    private static readonly IntPtr* _vtable = BuildVtable();

    /// <summary>Creates a native <c>ID2D1SimplifiedGeometrySink*</c> bound to <paramref name="path"/>.
    /// Outline coords are converted via <c>baseline + (x, -y)</c> so the call site supplies
    /// a baseline origin that already reflects DPI scaling. Caller MUST <see cref="Destroy"/>
    /// after the DWrite call returns.</summary>
    public static nint Create(PathGeometry path, double baselineX, double baselineY)
    {
        var sink = new Sink(path, baselineX, baselineY);
        var handle = GCHandle.Alloc(sink);

        // Layout: [0..7]=vtable_ptr  [8..15]=GCHandle  [16..23]=refcount(int aligned to 8)
        var instance = (Instance*)Marshal.AllocHGlobal(sizeof(Instance));
        instance->Vtable = _vtable;
        instance->Handle = GCHandle.ToIntPtr(handle);
        instance->RefCount = 1;
        return (nint)instance;
    }

    public static void Destroy(nint instance)
    {
        if (instance == 0) return;
        var inst = (Instance*)instance;
        if (inst->Handle != 0)
        {
            GCHandle.FromIntPtr(inst->Handle).Free();
            inst->Handle = 0;
        }
        Marshal.FreeHGlobal(instance);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Instance
    {
        public IntPtr* Vtable;
        public IntPtr Handle;   // GCHandle to managed Sink
        public int RefCount;
    }

    private sealed class Sink
    {
        public readonly PathGeometry Path;
        public readonly double BaselineX;
        public readonly double BaselineY;
        public bool InFigure;

        public Sink(PathGeometry path, double baselineX, double baselineY)
        {
            Path = path;
            BaselineX = baselineX;
            BaselineY = baselineY;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_POINT_2F { public float x; public float y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_BEZIER_SEGMENT
    {
        public D2D1_POINT_2F point1;
        public D2D1_POINT_2F point2;
        public D2D1_POINT_2F point3;
    }

    private static IntPtr* BuildVtable()
    {
        // 10 slots:
        //   [0] QueryInterface, [1] AddRef, [2] Release
        //   [3] SetFillMode, [4] SetSegmentFlags
        //   [5] BeginFigure, [6] AddLines, [7] AddBeziers
        //   [8] EndFigure, [9] Close
        var vtbl = (IntPtr*)Marshal.AllocHGlobal(sizeof(IntPtr) * 10);
        vtbl[0] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        vtbl[1] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        vtbl[2] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        vtbl[3] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, int, void>)&SetFillMode;
        vtbl[4] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, int, void>)&SetSegmentFlags;
        vtbl[5] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2F, int, void>)&BeginFigure;
        vtbl[6] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2F*, uint, void>)&AddLines;
        vtbl[7] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, D2D1_BEZIER_SEGMENT*, uint, void>)&AddBeziers;
        vtbl[8] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, int, void>)&EndFigure;
        vtbl[9] = (IntPtr)(delegate* unmanaged[Stdcall]<nint, int>)&Close;
        return vtbl;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Sink GetSink(nint self) =>
        (Sink)GCHandle.FromIntPtr(((Instance*)self)->Handle).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int QueryInterface(nint self, Guid* riid, nint* ppvObject)
    {
        if (ppvObject == null) return unchecked((int)0x80004003); // E_POINTER
        if (*riid == _iidUnknown || *riid == _iidSimplifiedSink)
        {
            *ppvObject = self;
            // Manually bump refcount - UnmanagedCallersOnly methods can't be called
            // from managed code, so we duplicate the AddRef body here.
            Interlocked.Increment(ref ((Instance*)self)->RefCount);
            return 0;
        }
        *ppvObject = 0;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint AddRef(nint self)
    {
        var inst = (Instance*)self;
        return (uint)Interlocked.Increment(ref inst->RefCount);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint Release(nint self)
    {
        var inst = (Instance*)self;
        int n = Interlocked.Decrement(ref inst->RefCount);
        // We never let DWrite free us - Destroy is called explicitly by the C# owner -
        // so just report the reduced count here.
        return (uint)Math.Max(0, n);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void SetFillMode(nint self, int fillMode) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void SetSegmentFlags(nint self, int flags) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void BeginFigure(nint self, D2D1_POINT_2F startPoint, int figureBegin)
    {
        var sink = GetSink(self);
        // DWrite outline coords are Y-down (positive y = below baseline) in the same
        // direction as SVG/Direct2D screen coords - direct addition, no flip.
        sink.Path.MoveTo(sink.BaselineX + startPoint.x, sink.BaselineY + startPoint.y);
        sink.InFigure = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void AddLines(nint self, D2D1_POINT_2F* points, uint count)
    {
        var sink = GetSink(self);
        for (uint i = 0; i < count; i++)
        {
            sink.Path.LineTo(sink.BaselineX + points[i].x, sink.BaselineY + points[i].y);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void AddBeziers(nint self, D2D1_BEZIER_SEGMENT* beziers, uint count)
    {
        var sink = GetSink(self);
        for (uint i = 0; i < count; i++)
        {
            var b = beziers[i];
            sink.Path.BezierTo(
                sink.BaselineX + b.point1.x, sink.BaselineY + b.point1.y,
                sink.BaselineX + b.point2.x, sink.BaselineY + b.point2.y,
                sink.BaselineX + b.point3.x, sink.BaselineY + b.point3.y);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void EndFigure(nint self, int figureEnd)
    {
        var sink = GetSink(self);
        if (sink.InFigure)
        {
            // figureEnd = 1 (D2D1_FIGURE_END_CLOSED) → close. 0 (OPEN) → leave open.
            if (figureEnd == 1)
            {
                sink.Path.Close();
            }
            sink.InFigure = false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Close(nint self) => 0;
}
