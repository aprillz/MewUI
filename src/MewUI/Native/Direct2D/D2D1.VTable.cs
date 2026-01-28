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

internal unsafe struct ID2D1Bitmap
{
    public void** lpVtbl;
}

internal unsafe struct ID2D1DCRenderTarget
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
            var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_RENDER_TARGET_PROPERTIES*, D2D1_HWND_RENDER_TARGET_PROPERTIES*, nint*, int>)factory->lpVtbl[CreateHwndRenderTargetIndex];
            int hr = fn(factory, pRt, pHwnd, &rt);
            renderTarget = rt;
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
            var fn = (delegate* unmanaged[Stdcall]<ID2D1Factory*, D2D1_RENDER_TARGET_PROPERTIES*, nint*, int>)factory->lpVtbl[CreateDcRenderTargetIndex];
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
            var fn = (delegate* unmanaged[Stdcall]<ID2D1DCRenderTarget*, nint, RECT*, int>)dcRt->lpVtbl[BindDCIndex];
            return fn(dcRt, hdc, pRect);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginDraw(ID2D1RenderTarget* rt)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, void>)rt->lpVtbl[48];
        fn(rt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EndDraw(ID2D1RenderTarget* rt)
    {
        ulong tag1 = 0, tag2 = 0;
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, ulong*, ulong*, int>)rt->lpVtbl[49];
        return fn(rt, &tag1, &tag2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear(ID2D1RenderTarget* rt, in D2D1_COLOR_F color)
    {
        fixed (D2D1_COLOR_F* p = &color)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_COLOR_F*, void>)rt->lpVtbl[47];
            fn(rt, p);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDpi(ID2D1RenderTarget* rt, float dpiX, float dpiY)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, float, float, void>)rt->lpVtbl[51];
        fn(rt, dpiX, dpiY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateSolidColorBrush(ID2D1RenderTarget* rt, in D2D1_COLOR_F color, out nint brush)
    {
        nint b = 0;
        fixed (D2D1_COLOR_F* pColor = &color)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_COLOR_F*, nint, nint*, int>)rt->lpVtbl[8];
            int hr = fn(rt, pColor, 0, &b);
            brush = b;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawLine(ID2D1RenderTarget* rt, D2D1_POINT_2F p0, D2D1_POINT_2F p1, nint brush, float strokeWidth)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_POINT_2F, D2D1_POINT_2F, nint, float, nint, void>)rt->lpVtbl[15];
        fn(rt, p0, p1, brush, strokeWidth, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawRectangle(ID2D1RenderTarget* rt, in D2D1_RECT_F rect, nint brush, float strokeWidth)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, nint, float, nint, void>)rt->lpVtbl[16];
            fn(rt, pRect, brush, strokeWidth, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillRectangle(ID2D1RenderTarget* rt, in D2D1_RECT_F rect, nint brush)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, nint, void>)rt->lpVtbl[17];
            fn(rt, pRect, brush);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawRoundedRectangle(ID2D1RenderTarget* rt, in D2D1_ROUNDED_RECT rect, nint brush, float strokeWidth)
    {
        fixed (D2D1_ROUNDED_RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ROUNDED_RECT*, nint, float, nint, void>)rt->lpVtbl[18];
            fn(rt, pRect, brush, strokeWidth, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillRoundedRectangle(ID2D1RenderTarget* rt, in D2D1_ROUNDED_RECT rect, nint brush)
    {
        fixed (D2D1_ROUNDED_RECT* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ROUNDED_RECT*, nint, void>)rt->lpVtbl[19];
            fn(rt, pRect, brush);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawEllipse(ID2D1RenderTarget* rt, in D2D1_ELLIPSE ellipse, nint brush, float strokeWidth)
    {
        fixed (D2D1_ELLIPSE* pEllipse = &ellipse)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ELLIPSE*, nint, float, nint, void>)rt->lpVtbl[20];
            fn(rt, pEllipse, brush, strokeWidth, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillEllipse(ID2D1RenderTarget* rt, in D2D1_ELLIPSE ellipse, nint brush)
    {
        fixed (D2D1_ELLIPSE* pEllipse = &ellipse)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_ELLIPSE*, nint, void>)rt->lpVtbl[21];
            fn(rt, pEllipse, brush);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PushAxisAlignedClip(ID2D1RenderTarget* rt, in D2D1_RECT_F rect)
    {
        fixed (D2D1_RECT_F* pRect = &rect)
        {
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_RECT_F*, D2D1_ANTIALIAS_MODE, void>)rt->lpVtbl[45];
            fn(rt, pRect, D2D1_ANTIALIAS_MODE.PER_PRIMITIVE);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PopAxisAlignedClip(ID2D1RenderTarget* rt)
    {
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, void>)rt->lpVtbl[46];
        fn(rt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetTextAntialiasMode(ID2D1RenderTarget* rt, D2D1_TEXT_ANTIALIAS_MODE mode)
    {
        // Layout per d2d1.h: SetTextAntialiasMode comes after Set/GetAntialiasMode, before Set/GetTextRenderingParams.
        var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_TEXT_ANTIALIAS_MODE, void>)rt->lpVtbl[34];
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
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, char*, uint, nint, D2D1_RECT_F*, nint, D2D1_DRAW_TEXT_OPTIONS, uint, void>)rt->lpVtbl[27];
            fn(rt, pText, (uint)text.Length, textFormat, pRect, brush, D2D1_DRAW_TEXT_OPTIONS.NONE, 0);
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
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, D2D1_SIZE_U, nint, uint, D2D1_BITMAP_PROPERTIES*, nint*, int>)rt->lpVtbl[4];
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
            var fn = (delegate* unmanaged[Stdcall]<ID2D1RenderTarget*, nint, D2D1_RECT_F*, float, D2D1_BITMAP_INTERPOLATION_MODE, D2D1_RECT_F*, void>)rt->lpVtbl[26];
            fn(rt, bitmap, pDest, opacity, interpolationMode, pSrc);
        }
    }
}

#pragma warning restore CS0649
