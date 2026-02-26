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

internal static unsafe class D2D1VTable
{
    private const int CreateHwndRenderTargetIndex = 14;
    private const int CreateDcRenderTargetIndex = 16;
    private const int BindDCIndex = 57; // First method after ID2D1RenderTarget

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
