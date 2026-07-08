using System.Runtime.CompilerServices;

using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Native.Direct2D;

#pragma warning disable CS0649 // Assigned by native code (COM vtable)

internal unsafe struct ID2D1Factory
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1RenderTarget
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1Layer
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1Geometry
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1Bitmap
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1DCRenderTarget
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1GeometrySink
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1DeviceContext
{
    public void** lpVtbl;
}

internal static unsafe class D2D1VTable
{
    private const int CreateHwndRenderTargetIndex = 14;
    private const int CreateDcRenderTargetIndex = 16;
    private const int BindDCIndex = 57; // First method after ID2D1RenderTarget
    private const int DeviceContextPushLayerIndex = 86; // ID2D1DeviceContext::PushLayer (D2D1_LAYER_PARAMETERS1)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateHwndRenderTarget(
        ID2D1Factory* factory,
        ref D2D1_RENDER_TARGET_PROPERTIES rtProps,
        ref D2D1_HWND_RENDER_TARGET_PROPERTIES hwndProps,
        out nint renderTarget)
    {
        nint rt = 0;
        fixed (D2D1_RENDER_TARGET_PROPERTIES* pRt = &rtProps)
        fixed (D2D1_HWND_RENDER_TARGET_PROPERTIES* pHwnd = &hwndProps)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_RENDER_TARGET_PROPERTIES*, D2D1_HWND_RENDER_TARGET_PROPERTIES*, nint*, int>)(factory->lpVtbl[CreateHwndRenderTargetIndex]);
            int hr = fn(factory, pRt, pHwnd, &rt);
            renderTarget = rt;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateRoundedRectangleGeometry(
        ID2D1Factory* factory,
        in D2D1_ROUNDED_RECT rect,
        out nint geometry)
    {
        nint g = 0;
        fixed (D2D1_ROUNDED_RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_ROUNDED_RECT*, nint*, int>)(factory->lpVtbl[6]);
            int hr = fn(factory, pRect, &g);
            geometry = g;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateDcRenderTarget(
        ID2D1Factory* factory,
        ref D2D1_RENDER_TARGET_PROPERTIES rtProps,
        out nint dcRenderTarget)
    {
        nint rt = 0;
        fixed (D2D1_RENDER_TARGET_PROPERTIES* pRt = &rtProps)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_RENDER_TARGET_PROPERTIES*, nint*, int>)(factory->lpVtbl[CreateDcRenderTargetIndex]);
            int hr = fn(factory, pRt, &rt);
            dcRenderTarget = rt;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BindDC(ID2D1DCRenderTarget* dcRt, nint hdc, ref RECT rect)
    {
        fixed (RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1DCRenderTarget*, nint, RECT*, int>)(dcRt->lpVtbl[BindDCIndex]);
            return fn(dcRt, hdc, pRect);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginDraw(ID2D1RenderTarget* rt)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, void>)(rt->lpVtbl[48]);
        fn(rt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EndDraw(ID2D1RenderTarget* rt)
    {
        ulong tag1 = 0, tag2 = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, ulong*, ulong*, int>)(rt->lpVtbl[49]);
        return fn(rt, &tag1, &tag2);
    }

    /// <summary>
    /// ID2D1RenderTarget::Flush - submits all batched drawing commands to the GPU. Required
    /// before <see cref="CopyFromBitmap"/> or <c>MapBitmap</c> on a bitmap that this DC has
    /// pending writes to: without it, the readback may capture pre-flush state. Calls within
    /// an active BeginDraw block are legal - this is the documented escape hatch for
    /// mid-frame readback.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Flush(ID2D1RenderTarget* rt)
    {
        ulong tag1 = 0, tag2 = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, ulong*, ulong*, int>)(rt->lpVtbl[42]);
        return fn(rt, &tag1, &tag2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear(ID2D1RenderTarget* rt, in D2D1_COLOR_F color)
    {
        fixed (D2D1_COLOR_F* p = &color)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_COLOR_F*, void>)(rt->lpVtbl[47]);
            fn(rt, p);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDpi(ID2D1RenderTarget* rt, float dpiX, float dpiY)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, float, float, void>)(rt->lpVtbl[51]);
        fn(rt, dpiX, dpiY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetDpi(ID2D1RenderTarget* rt, out float dpiX, out float dpiY)
    {
        float x, y;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, float*, float*, void>)(rt->lpVtbl[52]);
        fn(rt, &x, &y);
        dpiX = x;
        dpiY = y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateSolidColorBrush(ID2D1RenderTarget* rt, in D2D1_COLOR_F color, out nint brush)
    {
        nint b = 0;
        fixed (D2D1_COLOR_F* pColor = &color)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_COLOR_F*, nint, nint*, int>)(rt->lpVtbl[8]);
            int hr = fn(rt, pColor, 0, &b);
            brush = b;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateLayer(ID2D1RenderTarget* rt, out nint layer)
    {
        nint l = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_SIZE_F*, nint*, int>)(rt->lpVtbl[13]);
        int hr = fn(rt, null, &l);
        layer = l;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PushLayer(ID2D1RenderTarget* rt, in D2D1_LAYER_PARAMETERS parameters, nint layer)
    {
        fixed (D2D1_LAYER_PARAMETERS* pParams = &parameters)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_LAYER_PARAMETERS*, nint, void>)(rt->lpVtbl[40]);
            fn(rt, pParams, layer);
        }
    }

    /// <summary>
    /// ID2D1DeviceContext::PushLayer (vtable index 86). Uses D2D1_LAYER_PARAMETERS1 with D2D1_LAYER_OPTIONS1.
    /// The layer parameter can be 0 (NULL) for D2D 1.1 automatic layer management.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PushLayer(ID2D1DeviceContext* dc, in D2D1_LAYER_PARAMETERS1 parameters, nint layer)
    {
        fixed (D2D1_LAYER_PARAMETERS1* pParams = &parameters)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, D2D1_LAYER_PARAMETERS1*, nint, void>)(dc->lpVtbl[DeviceContextPushLayerIndex]);
            fn(dc, pParams, layer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PopLayer(ID2D1RenderTarget* rt)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, void>)(rt->lpVtbl[41]);
        fn(rt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawLine(ID2D1RenderTarget* rt, D2D1_POINT_2F p0, D2D1_POINT_2F p1, nint brush, float strokeWidth)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_POINT_2F, D2D1_POINT_2F, nint, float, nint, void>)(rt->lpVtbl[15]);
        fn(rt, p0, p1, brush, strokeWidth, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawLine(ID2D1RenderTarget* rt, D2D1_POINT_2F p0, D2D1_POINT_2F p1, nint brush, float strokeWidth, nint strokeStyle)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_POINT_2F, D2D1_POINT_2F, nint, float, nint, void>)(rt->lpVtbl[15]);
        fn(rt, p0, p1, brush, strokeWidth, strokeStyle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawRectangle(ID2D1RenderTarget* rt, in D2D1_RECT_F rect, nint brush, float strokeWidth)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, nint, float, nint, void>)(rt->lpVtbl[16]);
            fn(rt, pRect, brush, strokeWidth, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawRectangle(ID2D1RenderTarget* rt, in D2D1_RECT_F rect, nint brush, float strokeWidth, nint strokeStyle)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, nint, float, nint, void>)(rt->lpVtbl[16]);
            fn(rt, pRect, brush, strokeWidth, strokeStyle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillRectangle(ID2D1RenderTarget* rt, in D2D1_RECT_F rect, nint brush)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, nint, void>)(rt->lpVtbl[17]);
            fn(rt, pRect, brush);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawRoundedRectangle(ID2D1RenderTarget* rt, in D2D1_ROUNDED_RECT rect, nint brush, float strokeWidth)
    {
        fixed (D2D1_ROUNDED_RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ROUNDED_RECT*, nint, float, nint, void>)(rt->lpVtbl[18]);
            fn(rt, pRect, brush, strokeWidth, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawRoundedRectangle(ID2D1RenderTarget* rt, in D2D1_ROUNDED_RECT rect, nint brush, float strokeWidth, nint strokeStyle)
    {
        fixed (D2D1_ROUNDED_RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ROUNDED_RECT*, nint, float, nint, void>)(rt->lpVtbl[18]);
            fn(rt, pRect, brush, strokeWidth, strokeStyle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillRoundedRectangle(ID2D1RenderTarget* rt, in D2D1_ROUNDED_RECT rect, nint brush)
    {
        fixed (D2D1_ROUNDED_RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ROUNDED_RECT*, nint, void>)(rt->lpVtbl[19]);
            fn(rt, pRect, brush);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawEllipse(ID2D1RenderTarget* rt, in D2D1_ELLIPSE ellipse, nint brush, float strokeWidth)
    {
        fixed (D2D1_ELLIPSE* pEllipse = &ellipse)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ELLIPSE*, nint, float, nint, void>)(rt->lpVtbl[20]);
            fn(rt, pEllipse, brush, strokeWidth, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawEllipse(ID2D1RenderTarget* rt, in D2D1_ELLIPSE ellipse, nint brush, float strokeWidth, nint strokeStyle)
    {
        fixed (D2D1_ELLIPSE* pEllipse = &ellipse)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ELLIPSE*, nint, float, nint, void>)(rt->lpVtbl[20]);
            fn(rt, pEllipse, brush, strokeWidth, strokeStyle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillEllipse(ID2D1RenderTarget* rt, in D2D1_ELLIPSE ellipse, nint brush)
    {
        fixed (D2D1_ELLIPSE* pEllipse = &ellipse)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ELLIPSE*, nint, void>)(rt->lpVtbl[21]);
            fn(rt, pEllipse, brush);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PushAxisAlignedClip(ID2D1RenderTarget* rt, in D2D1_RECT_F rect)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, D2D1_ANTIALIAS_MODE, void>)(rt->lpVtbl[45]);
            fn(rt, pRect, D2D1_ANTIALIAS_MODE.PER_PRIMITIVE);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PopAxisAlignedClip(ID2D1RenderTarget* rt)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, void>)(rt->lpVtbl[46]);
        fn(rt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetTextAntialiasMode(ID2D1RenderTarget* rt, D2D1_TEXT_ANTIALIAS_MODE mode)
    {
        // Layout per d2d1.h: SetTextAntialiasMode comes after Set/GetAntialiasMode, before Set/GetTextRenderingParams.
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_TEXT_ANTIALIAS_MODE, void>)(rt->lpVtbl[34]);
        fn(rt, mode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawText(ID2D1RenderTarget* rt, ReadOnlySpan<char> text, nint textFormat, in D2D1_RECT_F layoutRect, nint brush)
    {
        if (text.IsEmpty)
        {
            return;
        }

        fixed (char* pText = text)
        fixed (D2D1_RECT_F* pRect = &layoutRect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, char*, uint, nint, D2D1_RECT_F*, nint, D2D1_DRAW_TEXT_OPTIONS, uint, void>)(rt->lpVtbl[27]);
            fn(rt, pText, (uint)text.Length, textFormat, pRect, brush, D2D1_DRAW_TEXT_OPTIONS.NONE, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawText(ID2D1RenderTarget* rt, ReadOnlySpan<char> text, nint textFormat, in D2D1_RECT_F layoutRect, nint brush, D2D1_DRAW_TEXT_OPTIONS options)
    {
        if (text.IsEmpty)
        {
            return;
        }

        fixed (char* pText = text)
        fixed (D2D1_RECT_F* pRect = &layoutRect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, char*, uint, nint, D2D1_RECT_F*, nint, D2D1_DRAW_TEXT_OPTIONS, uint, void>)(rt->lpVtbl[27]);
            fn(rt, pText, (uint)text.Length, textFormat, pRect, brush, options, 0);
        }
    }

    /// <summary>
    /// ID2D1RenderTarget::DrawTextLayout (vtable index 28).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawTextLayout(ID2D1RenderTarget* rt, D2D1_POINT_2F origin, nint textLayout, nint brush, D2D1_DRAW_TEXT_OPTIONS options)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_POINT_2F, nint, nint, D2D1_DRAW_TEXT_OPTIONS, void>)(rt->lpVtbl[28]);
        fn(rt, origin, textLayout, brush, options);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RECT GetClientRect(nint hwnd)
    {
        User32.GetClientRect(hwnd, out var rc);
        return rc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateBitmap(
        ID2D1RenderTarget* rt,
        D2D1_SIZE_U size,
        nint srcData,
        uint pitch,
        in D2D1_BITMAP_PROPERTIES props,
        out nint bitmap)
    {
        nint bmp = 0;
        fixed (D2D1_BITMAP_PROPERTIES* pProps = &props)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_SIZE_U, nint, uint, D2D1_BITMAP_PROPERTIES*, nint*, int>)(rt->lpVtbl[4]);
            int hr = fn(rt, size, srcData, pitch, pProps, &bmp);
            bitmap = bmp;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateSharedBitmap(
        ID2D1RenderTarget* rt,
        in Guid riid,
        nint data,
        in D2D1_BITMAP_PROPERTIES props,
        out nint bitmap)
    {
        nint bmp = 0;
        fixed (Guid* pIid = &riid)
        fixed (D2D1_BITMAP_PROPERTIES* pProps = &props)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, Guid*, nint, D2D1_BITMAP_PROPERTIES*, nint*, int>)(rt->lpVtbl[6]);
            int hr = fn(rt, pIid, data, pProps, &bmp);
            bitmap = bmp;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawBitmap(
        ID2D1RenderTarget* rt,
        nint bitmap,
        in D2D1_RECT_F destRect,
        float opacity,
        D2D1_BITMAP_INTERPOLATION_MODE interpolationMode,
        in D2D1_RECT_F srcRect)
    {
        fixed (D2D1_RECT_F* pDest = &destRect)
        fixed (D2D1_RECT_F* pSrc = &srcRect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, nint, D2D1_RECT_F*, float, D2D1_BITMAP_INTERPOLATION_MODE, D2D1_RECT_F*, void>)(rt->lpVtbl[26]);
            fn(rt, bitmap, pDest, opacity, interpolationMode, pSrc);
        }
    }

    // ID2D1RenderTarget vtbl[7]: CreateBitmapBrush
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateBitmapBrush(
        ID2D1RenderTarget* rt,
        nint bitmap,
        in D2D1_BITMAP_BRUSH_PROPERTIES bitmapBrushProps,
        in D2D1_BRUSH_PROPERTIES brushProps,
        out nint bitmapBrush)
    {
        nint b = 0;
        fixed (D2D1_BITMAP_BRUSH_PROPERTIES* pBmp = &bitmapBrushProps)
        fixed (D2D1_BRUSH_PROPERTIES* pBrush = &brushProps)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, nint, D2D1_BITMAP_BRUSH_PROPERTIES*, D2D1_BRUSH_PROPERTIES*, nint*, int>)(rt->lpVtbl[7]);
            int hr = fn(rt, bitmap, pBmp, pBrush, &b);
            bitmapBrush = b;
            return hr;
        }
    }

    // ID2D1RenderTarget vtbl[9]: CreateGradientStopCollection
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateGradientStopCollection(
        ID2D1RenderTarget* rt,
        ReadOnlySpan<D2D1_GRADIENT_STOP> stops,
        D2D1_GAMMA colorInterpolationGamma,
        D2D1_EXTEND_MODE extendMode,
        out nint gradientStopCollection)
    {
        nint g = 0;
        fixed (D2D1_GRADIENT_STOP* pStops = stops)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_GRADIENT_STOP*, uint, D2D1_GAMMA, D2D1_EXTEND_MODE, nint*, int>)(rt->lpVtbl[9]);
            int hr = fn(rt, pStops, (uint)stops.Length, colorInterpolationGamma, extendMode, &g);
            gradientStopCollection = g;
            return hr;
        }
    }

    // ID2D1RenderTarget vtbl[10]: CreateLinearGradientBrush
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateLinearGradientBrush(
        ID2D1RenderTarget* rt,
        in D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES props,
        in D2D1_BRUSH_PROPERTIES brushProps,
        nint gradientStopCollection,
        out nint linearGradientBrush)
    {
        nint b = 0;
        fixed (D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES* pLin = &props)
        fixed (D2D1_BRUSH_PROPERTIES* pBrush = &brushProps)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES*, D2D1_BRUSH_PROPERTIES*, nint, nint*, int>)(rt->lpVtbl[10]);
            int hr = fn(rt, pLin, pBrush, gradientStopCollection, &b);
            linearGradientBrush = b;
            return hr;
        }
    }

    // ID2D1RenderTarget vtbl[11]: CreateRadialGradientBrush
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateRadialGradientBrush(
        ID2D1RenderTarget* rt,
        in D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES props,
        in D2D1_BRUSH_PROPERTIES brushProps,
        nint gradientStopCollection,
        out nint radialGradientBrush)
    {
        nint b = 0;
        fixed (D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES* pRad = &props)
        fixed (D2D1_BRUSH_PROPERTIES* pBrush = &brushProps)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES*, D2D1_BRUSH_PROPERTIES*, nint, nint*, int>)(rt->lpVtbl[11]);
            int hr = fn(rt, pRad, pBrush, gradientStopCollection, &b);
            radialGradientBrush = b;
            return hr;
        }
    }

    // ID2D1Factory vtbl[11]: CreateStrokeStyle
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateStrokeStyle(
        ID2D1Factory* factory,
        in D2D1_STROKE_STYLE_PROPERTIES properties,
        ReadOnlySpan<float> dashes,
        out nint strokeStyle)
    {
        nint s = 0;
        fixed (D2D1_STROKE_STYLE_PROPERTIES* pProps = &properties)
        {
            if (dashes.IsEmpty)
            {
                var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_STROKE_STYLE_PROPERTIES*, float*, uint, nint*, int>)(factory->lpVtbl[11]);
                int hr = fn(factory, pProps, null, 0, &s);
                strokeStyle = s;
                return hr;
            }
            else
            {
                fixed (float* pDashes = dashes)
                {
                    var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_STROKE_STYLE_PROPERTIES*, float*, uint, nint*, int>)(factory->lpVtbl[11]);
                    int hr = fn(factory, pProps, pDashes, (uint)dashes.Length, &s);
                    strokeStyle = s;
                    return hr;
                }
            }
        }
    }

    // ----------------------------------------------------------------------
    // ID2D1DeviceContext (extends ID2D1RenderTarget) - for the built-in effect
    // pipeline used by the Direct2D image-filter executor.
    // Vtable layout (after IUnknown/Resource/RenderTarget):
    //   [57] CreateBitmap (D2D1_BITMAP_PROPERTIES1)
    //   [62] CreateBitmapFromDxgiSurface
    //   [63] CreateEffect
    //   [74] SetTarget
    //   [83] DrawImage
    //   [86] PushLayer (LAYER_PARAMETERS1)  ← already used
    // ----------------------------------------------------------------------

    /// <summary>ID2D1DeviceContext::CreateBitmap (vtbl 57). Allocates a D2D bitmap, optionally
    /// initialized from <paramref name="sourceData"/> (BGRA premultiplied, <paramref name="pitch"/>
    /// bytes per row).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateBitmap1(
        ID2D1DeviceContext* dc,
        D2D1_SIZE_U size,
        ReadOnlySpan<byte> sourceData,
        uint pitch,
        in D2D1_BITMAP_PROPERTIES1 properties,
        out nint bitmap)
    {
        nint b = 0;
        fixed (D2D1_BITMAP_PROPERTIES1* pProps = &properties)
        {
            if (sourceData.IsEmpty)
            {
                var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, D2D1_SIZE_U, void*, uint, D2D1_BITMAP_PROPERTIES1*, nint*, int>)(dc->lpVtbl[57]);
                int hr = fn(dc, size, null, 0, pProps, &b);
                bitmap = b;
                return hr;
            }
            else
            {
                fixed (byte* pData = sourceData)
                {
                    var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, D2D1_SIZE_U, void*, uint, D2D1_BITMAP_PROPERTIES1*, nint*, int>)(dc->lpVtbl[57]);
                    int hr = fn(dc, size, pData, pitch, pProps, &b);
                    bitmap = b;
                    return hr;
                }
            }
        }
    }

    /// <summary>ID2D1DeviceContext::CreateEffect (vtbl 63).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateEffect(ID2D1DeviceContext* dc, in Guid clsid, out nint effect)
    {
        nint e = 0;
        fixed (Guid* pClsid = &clsid)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Guid*, nint*, int>)(dc->lpVtbl[63]);
            int hr = fn(dc, pClsid, &e);
            effect = e;
            return hr;
        }
    }

    /// <summary>ID2D1DeviceContext::CreateBitmapFromDxgiSurface (vtbl 62). Wraps a DXGI
    /// surface as a targetable bitmap on the same D2D device.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateBitmapFromDxgiSurface(
        ID2D1DeviceContext* dc,
        nint dxgiSurface,
        in D2D1_BITMAP_PROPERTIES1 properties,
        out nint bitmap)
    {
        nint b = 0;
        fixed (D2D1_BITMAP_PROPERTIES1* pProps = &properties)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, nint, D2D1_BITMAP_PROPERTIES1*, nint*, int>)(dc->lpVtbl[62]);
            int hr = fn(dc, dxgiSurface, pProps, &b);
            bitmap = b;
            return hr;
        }
    }

    /// <summary>ID2D1DeviceContext::SetTarget (vtbl 74). Switches the active render target -
    /// effects render into whatever the device context currently targets.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetTarget(ID2D1DeviceContext* dc, nint imageTarget)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, nint, void>)(dc->lpVtbl[74]);
        fn(dc, imageTarget);
    }

    /// <summary>ID2D1DeviceContext::GetTarget (vtbl 75). Used to save/restore the previous
    /// target around an effects pass.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetTarget(ID2D1DeviceContext* dc, out nint imageTarget)
    {
        nint t = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, nint*, void>)(dc->lpVtbl[75]);
        fn(dc, &t);
        imageTarget = t;
    }

    /// <summary>ID2D1DeviceContext::DrawImage (vtbl 83). Renders <paramref name="image"/>
    /// (a bitmap or an effect output) into the current target.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawImage(
        ID2D1DeviceContext* dc,
        nint image,
        D2D1_INTERPOLATION_MODE interpolationMode = D2D1_INTERPOLATION_MODE.LINEAR,
        D2D1_COMPOSITE_MODE compositeMode = D2D1_COMPOSITE_MODE.SOURCE_OVER)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, nint, D2D1_POINT_2F*, D2D1_RECT_F*, D2D1_INTERPOLATION_MODE, D2D1_COMPOSITE_MODE, void>)(dc->lpVtbl[83]);
        fn(dc, image, null, null, interpolationMode, compositeMode);
    }

    /// <summary>ID2D1DeviceContext::DrawBitmap (vtbl 85). The DC overload - supports
    /// <see cref="D2D1_INTERPOLATION_MODE.HIGH_QUALITY_CUBIC"/> and other GPU-side
    /// down-sampling modes that the legacy ID2D1RenderTarget::DrawBitmap (vtbl 26) lacks.
    /// Use this when the active target is a DeviceContext and ImageScaleQuality demands
    /// better quality than LINEAR - avoids the manual CPU mip pyramid path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawBitmap(
        ID2D1DeviceContext* dc,
        nint bitmap,
        in D2D1_RECT_F destinationRectangle,
        float opacity,
        D2D1_INTERPOLATION_MODE interpolationMode,
        in D2D1_RECT_F sourceRectangle)
    {
        fixed (D2D1_RECT_F* dst = &destinationRectangle)
        fixed (D2D1_RECT_F* src = &sourceRectangle)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, nint, D2D1_RECT_F*, float, D2D1_INTERPOLATION_MODE, D2D1_RECT_F*, void*, void>)(dc->lpVtbl[85]);
            fn(dc, bitmap, dst, opacity, interpolationMode, src, null);
        }
    }

    // ----------------------------------------------------------------------
    // ID2D1Bitmap1 (inherits ID2D1Bitmap → ID2D1Resource → IUnknown)
    //   [0-2]  IUnknown
    //   [3]    GetFactory (Resource)
    //   [4-7]  GetSize / GetPixelSize / GetPixelFormat / GetDpi (Bitmap)
    //   [8]    CopyFromBitmap (Bitmap)
    //   [9]    CopyFromRenderTarget (Bitmap)
    //   [10]   CopyFromMemory (Bitmap)
    //   [11]   GetColorContext (Bitmap1)
    //   [12]   GetOptions (Bitmap1)
    //   [13]   GetSurface (Bitmap1)
    //   [14]   Map (Bitmap1)
    //   [15]   Unmap (Bitmap1)
    // ----------------------------------------------------------------------

    /// <summary>ID2D1Bitmap::CopyFromBitmap (vtbl 8). Copies a region from a source bitmap
    /// to <paramref name="dstBitmap"/>. The destination point and source rectangle are null,
    /// so the full bitmap is copied. Both bitmaps must be on the same device.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CopyFromBitmap(nint dstBitmap, nint srcBitmap)
    {
        var vtbl = *(void***)dstBitmap;
        var fn = (delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2U*, nint, D2D1_RECT_U*, int>)(vtbl[8]);
        return fn(dstBitmap, null, srcBitmap, null);
    }

    /// <summary>ID2D1Bitmap::CopyFromMemory (vtbl 10). Uploads CPU-side pixel data into a
    /// GPU bitmap. <paramref name="srcData"/> is the source buffer (must contain at least
    /// <c>height * pitch</c> bytes covering the destination rect). The destination rect is
    /// passed as null so the entire bitmap is uploaded. Used by the GPU pixel surface's
    /// writeback path so CPU executors can Lock+modify pixels and have the changes
    /// propagate back to the GPU surface for downstream effects.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CopyFromMemory(nint dstBitmap, ReadOnlySpan<byte> srcData, uint pitch)
    {
        var vtbl = *(void***)dstBitmap;
        var fn = (delegate* unmanaged[Stdcall]<nint, D2D1_RECT_U*, void*, uint, int>)(vtbl[10]);
        fixed (byte* p = srcData)
        {
            return fn(dstBitmap, null, p, pitch);
        }
    }

    /// <summary>ID2D1Bitmap1::GetSurface (vtbl 13). Returns an AddRef'ed
    /// <c>IDXGISurface*</c> for GPU-backed bitmaps.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBitmapSurface(nint bitmap, out nint dxgiSurface)
    {
        nint localSurface = 0;
        var vtbl = *(void***)bitmap;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(vtbl[13]);
        int hr = fn(bitmap, &localSurface);
        dxgiSurface = localSurface;
        return hr;
    }

    /// <summary>ID2D1Bitmap1::Map (vtbl 14). Bitmap must have been created with
    /// <see cref="D2D1_BITMAP_OPTIONS.CPU_READ"/>. <paramref name="mapped"/> receives a
    /// pointer into staging memory plus row pitch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MapBitmap(nint bitmap, D2D1_MAP_OPTIONS options, out D2D1_MAPPED_RECT mapped)
    {
        D2D1_MAPPED_RECT m = default;
        var vtbl = *(void***)bitmap;
        var fn = (delegate* unmanaged[Stdcall]<nint, D2D1_MAP_OPTIONS, D2D1_MAPPED_RECT*, int>)(vtbl[14]);
        int hr = fn(bitmap, options, &m);
        mapped = m;
        return hr;
    }

    /// <summary>ID2D1Bitmap1::Unmap (vtbl 15). Releases the staging mapping.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UnmapBitmap(nint bitmap)
    {
        var vtbl = *(void***)bitmap;
        var fn = (delegate* unmanaged[Stdcall]<nint, int>)(vtbl[15]);
        return fn(bitmap);
    }

    // ----------------------------------------------------------------------
    // ID2D1Effect inherits ID2D1Properties (which inherits IUnknown).
    //
    // ID2D1Properties layout (d2d1_1.h):
    //   [0-2]  IUnknown
    //   [3]    GetPropertyCount
    //   [4]    GetPropertyName
    //   [5]    GetPropertyNameLength
    //   [6]    GetType
    //   [7]    GetPropertyIndex
    //   [8]    SetValueByName
    //   [9]    SetValue (UINT32 index, D2D1_PROPERTY_TYPE type, BYTE* data, UINT32 size)
    //   [10]   GetValueByName
    //   [11]   GetValue
    //   [12]   GetValueSize
    //   [13]   GetSubProperties
    //
    // ID2D1Effect extension:
    //   [14]   SetInput
    //   [15]   SetInputCount
    //   [16]   GetInput
    //   [17]   GetInputCount
    //   [18]   GetOutput
    //
    // (The SetValue/GetValue untyped overloads exposed in C++ are inline helpers, not vtable
    //  entries - including them in the count is the easy mis-indexing trap.)
    // ----------------------------------------------------------------------

    /// <summary>ID2D1Properties::SetValue (vtbl 9) - by index, typed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetEffectValue(nint effect, uint index, D2D1_PROPERTY_TYPE type, ReadOnlySpan<byte> data)
    {
        fixed (byte* pData = data)
        {
            var vtbl = *(void***)effect;
            var fn = (delegate* unmanaged[Stdcall]<nint, uint, D2D1_PROPERTY_TYPE, void*, uint, int>)(vtbl[9]);
            return fn(effect, index, type, pData, (uint)data.Length);
        }
    }

    /// <summary>Convenience: set a float-typed property (e.g. Gaussian blur stdDeviation).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetEffectValueFloat(nint effect, uint index, float value)
    {
        return SetEffectValue(effect, index, D2D1_PROPERTY_TYPE.FLOAT,
            new ReadOnlySpan<byte>(&value, sizeof(float)));
    }

    /// <summary>Convenience: set an enum-typed property.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetEffectValueEnum(nint effect, uint index, uint value)
    {
        return SetEffectValue(effect, index, D2D1_PROPERTY_TYPE.ENUM,
            new ReadOnlySpan<byte>(&value, sizeof(uint)));
    }

    /// <summary>ID2D1Effect::SetInput (vtbl 14).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetEffectInput(nint effect, uint index, nint image, bool invalidate = true)
    {
        var vtbl = *(void***)effect;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, nint, int, void>)(vtbl[14]);
        fn(effect, index, image, invalidate ? 1 : 0);
    }

    /// <summary>ID2D1Effect::GetOutput (vtbl 18). Returns an ID2D1Image that, when drawn,
    /// produces the effect's output. Caller releases.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetEffectOutput(nint effect, out nint image)
    {
        nint img = 0;
        var vtbl = *(void***)effect;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, void>)(vtbl[18]);
        fn(effect, &img);
        image = img;
    }

    /// <summary>ID2D1Effect::SetInputCount (vtbl 15). ID2D1Effect inherits ID2D1Properties
    /// (vtbl 3..13: 11 property methods); the Effect-specific slots start at 14:
    /// SetInput=14, SetInputCount=15, GetInput=16, GetInputCount=17, GetOutput=18.
    /// Wrong index here = ExecutionEngineException on call (mis-typed function pointer).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetEffectInputCount(nint effect, uint inputCount)
    {
        var vtbl = *(void***)effect;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, int>)(vtbl[15]);
        return fn(effect, inputCount);
    }

    /// <summary>Convenience: set a 4×5 color matrix property (e.g. ColorMatrix's COLOR_MATRIX).
    /// D2D1_MATRIX_5X4_F is laid out as 5 columns × 4 rows = 20 floats, row-major. Direct2D
    /// expects [R'_R, R'_G, R'_B, R'_A, R'_offset, G'_R, ...] which matches the
    /// <c>ColorMatrixFilter.Matrix</c> layout (row 0 → R', row 1 → G', etc.).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetEffectValueMatrix5x4(nint effect, uint index, ReadOnlySpan<float> matrix20)
    {
        if (matrix20.Length != 20) throw new ArgumentException("matrix must have 20 entries");
        fixed (float* p = matrix20)
        {
            return SetEffectValue(effect, index, D2D1_PROPERTY_TYPE.MATRIX_5X4,
                new ReadOnlySpan<byte>(p, 20 * sizeof(float)));
        }
    }

    /// <summary>Convenience: set a 3×2 affine transform matrix property (e.g. AffineTransform's
    /// TRANSFORM_MATRIX). 6 floats, row-major same as <c>D2D1_MATRIX_3X2_F</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetEffectValueMatrix3x2(nint effect, uint index, in D2D1_MATRIX_3X2_F matrix)
    {
        fixed (D2D1_MATRIX_3X2_F* p = &matrix)
        {
            return SetEffectValue(effect, index, D2D1_PROPERTY_TYPE.MATRIX_3X2,
                new ReadOnlySpan<byte>(p, sizeof(D2D1_MATRIX_3X2_F)));
        }
    }

    // ID2D1Factory1 vtbl[17]: CreateDevice - builds an ID2D1Device for the GPU pipeline.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateDevice(ID2D1Factory* factory, nint dxgiDevice, out nint d2dDevice)
    {
        nint d = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, nint, nint*, int>)(factory->lpVtbl[17]);
        int hr = fn(factory, dxgiDevice, &d);
        d2dDevice = d;
        return hr;
    }

    // ID2D1Device vtbl[4] (after IUnknown[3] + Resource[1]): CreateDeviceContext
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateDeviceContext(nint d2dDevice, uint options, out nint deviceContext)
    {
        nint dc = 0;
        var vtbl = *(void***)d2dDevice;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(vtbl[4]);
        int hr = fn(d2dDevice, options, &dc);
        deviceContext = dc;
        return hr;
    }

    // ID2D1Factory1 vtbl[18]: CreateStrokeStyle (with D2D1_STROKE_STYLE_PROPERTIES1)
    // Requires factory created with IID_ID2D1Factory1.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateStrokeStyle1(
        ID2D1Factory* factory,
        in D2D1_STROKE_STYLE_PROPERTIES1 properties,
        ReadOnlySpan<float> dashes,
        out nint strokeStyle)
    {
        nint s = 0;
        fixed (D2D1_STROKE_STYLE_PROPERTIES1* pProps = &properties)
        {
            if (dashes.IsEmpty)
            {
                var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_STROKE_STYLE_PROPERTIES1*, float*, uint, nint*, int>)(factory->lpVtbl[18]);
                int hr = fn(factory, pProps, null, 0, &s);
                strokeStyle = s;
                return hr;
            }
            else
            {
                fixed (float* pDashes = dashes)
                {
                    var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_STROKE_STYLE_PROPERTIES1*, float*, uint, nint*, int>)(factory->lpVtbl[18]);
                    int hr = fn(factory, pProps, pDashes, (uint)dashes.Length, &s);
                    strokeStyle = s;
                    return hr;
                }
            }
        }
    }

    // ID2D1RenderTarget: DrawGeometry with stroke style
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawGeometry(ID2D1RenderTarget* rt, nint geometry, nint brush, float strokeWidth, nint strokeStyle)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, nint, nint, float, nint, void>)(rt->lpVtbl[22]);
        fn(rt, geometry, brush, strokeWidth, strokeStyle);
    }

    // ID2D1RenderTarget vtbl[30]: SetTransform
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetTransform(ID2D1RenderTarget* rt, in D2D1_MATRIX_3X2_F matrix)
    {
        fixed (D2D1_MATRIX_3X2_F* pMatrix = &matrix)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_MATRIX_3X2_F*, void>)(rt->lpVtbl[30]);
            fn(rt, pMatrix);
        }
    }

    // ID2D1RenderTarget vtbl[31]: GetTransform
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static D2D1_MATRIX_3X2_F GetTransform(ID2D1RenderTarget* rt)
    {
        D2D1_MATRIX_3X2_F matrix = default;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_MATRIX_3X2_F*, void>)(rt->lpVtbl[31]);
        fn(rt, &matrix);
        return matrix;
    }

    // ID2D1RenderTarget vtbl[50]: GetPixelFormat. Used by callers that need the bound
    // surface's alpha mode (e.g. text antialias-mode selection - ClearType requires
    // ALPHA_MODE.IGNORE). Return-by-value struct uses the COM hidden-pointer convention,
    // matching <see cref="GetTransform"/>.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static D2D1_PIXEL_FORMAT GetPixelFormat(ID2D1RenderTarget* rt)
    {
        D2D1_PIXEL_FORMAT pf = default;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_PIXEL_FORMAT*, void>)(rt->lpVtbl[50]);
        fn(rt, &pf);
        return pf;
    }

    // ID2D1RenderTarget vtbl[22]: DrawGeometry
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawGeometry(ID2D1RenderTarget* rt, nint geometry, nint brush, float strokeWidth)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, nint, nint, float, nint, void>)(rt->lpVtbl[22]);
        fn(rt, geometry, brush, strokeWidth, 0);
    }

    // ID2D1RenderTarget vtbl[23]: FillGeometry
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillGeometry(ID2D1RenderTarget* rt, nint geometry, nint brush)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, nint, nint, nint, void>)(rt->lpVtbl[23]);
        fn(rt, geometry, brush, 0);
    }

    // ID2D1Factory vtbl[10]: CreatePathGeometry
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreatePathGeometry(ID2D1Factory* factory, out nint pathGeometry)
    {
        nint g = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, nint*, int>)(factory->lpVtbl[10]);
        int hr = fn(factory, &g);
        pathGeometry = g;
        return hr;
    }

    // ID2D1PathGeometry vtbl[17]: Open
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OpenPathGeometry(ID2D1Geometry* pathGeometry, out nint geometrySink)
    {
        nint sink = 0;
        //var pg = (ID2D1Geometry**)pathGeometry;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1Geometry*, nint*, int>)(pathGeometry->lpVtbl[17]);
        int hr = fn(pathGeometry, &sink);
        geometrySink = sink;
        return hr;
    }

    // ID2D1SimplifiedGeometrySink vtbl[3]: SetFillMode
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFillMode(ID2D1GeometrySink* geometrySink, D2D1_FILL_MODE fillMode)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, D2D1_FILL_MODE, void>)(geometrySink->lpVtbl[3]);
        fn(geometrySink, fillMode);
    }

    // ID2D1SimplifiedGeometrySink vtbl[5]: BeginFigure
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginFigure(ID2D1GeometrySink* geometrySink, D2D1_POINT_2F startPoint, D2D1_FIGURE_BEGIN figureBegin)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, D2D1_POINT_2F, D2D1_FIGURE_BEGIN, void>)(geometrySink->lpVtbl[5]);
        fn(geometrySink, startPoint, figureBegin);
    }

    // ID2D1SimplifiedGeometrySink vtbl[8]: EndFigure
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EndFigure(ID2D1GeometrySink* geometrySink, D2D1_FIGURE_END figureEnd)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, D2D1_FIGURE_END, void>)(geometrySink->lpVtbl[8]);
        fn(geometrySink, figureEnd);
    }

    // ID2D1SimplifiedGeometrySink vtbl[9]: Close
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CloseGeometrySink(ID2D1GeometrySink* geometrySink)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, int>)(geometrySink->lpVtbl[9]);
        return fn(geometrySink);
    }

    // ID2D1GeometrySink vtbl[10]: AddLine
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddLine(ID2D1GeometrySink* geometrySink, D2D1_POINT_2F point)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, D2D1_POINT_2F, void>)(geometrySink->lpVtbl[10]);
        fn(geometrySink, point);
    }

    // ID2D1GeometrySink vtbl[11]: AddBezier
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddBezier(ID2D1GeometrySink* geometrySink, in D2D1_BEZIER_SEGMENT bezier)
    {
        fixed (D2D1_BEZIER_SEGMENT* pBezier = &bezier)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, D2D1_BEZIER_SEGMENT*, void>)(geometrySink->lpVtbl[11]);
            fn(geometrySink, pBezier);
        }
    }
}

#pragma warning restore CS0649
