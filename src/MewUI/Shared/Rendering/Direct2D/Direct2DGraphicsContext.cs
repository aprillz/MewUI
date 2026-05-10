using System.Diagnostics;
using System.Numerics;

using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;

using static Aprillz.MewUI.Rendering.GradientBrushHelper;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DGraphicsContext : GraphicsContextBase
{
    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    private const int D2DERR_WRONG_RESOURCE_DOMAIN = unchecked((int)0x88990015);

    private readonly Direct2DGraphicsFactory _ownerFactory;
    private readonly nint _hwnd;
    private readonly nint _d2dFactory;
    private readonly nint _dwriteFactory;
    private readonly nint _defaultStrokeStyle;
    private readonly Action? _onRecreateTarget;
    private readonly Func<int>? _onPresentTarget;
    private readonly bool _ownsRenderTarget;
    private readonly DWriteTextFormatCache? _textFormatCache;
    private readonly Func<IRenderTarget, (nint renderTarget, int generation)>? _resolveRenderTarget;
    /// <summary>
    /// True when the active render target's alpha mode is <c>IGNORE</c>, which is the only
    /// case in which D2D ClearType subpixel AA produces correct output. Set per-frame by
    /// <see cref="ConfigureRenderTargetForFrame"/> from the bound RT's pixel format.
    /// Consumed by text AA mode selection and by layer creation
    /// (<see cref="D2D1_LAYER_OPTIONS.INITIALIZE_FOR_CLEARTYPE"/>).
    /// </summary>
    private bool _clearTypeEnabled;
    private nint _deviceContext; // ID2D1DeviceContext* (0 if D2D 1.1 unavailable)

    private int _renderTargetGeneration;
    private readonly Dictionary<uint, nint> _solidBrushes;
    private readonly Stack<(Matrix3x2 transform, float globalAlpha, int clipCount, Rect? clipBoundsWorld, bool textPixelSnap)> _states;
    private readonly Stack<ClipEntry> _clipStack;
    private nint _renderTarget; // ID2D1RenderTarget*

    /// <summary>Internal handle to the underlying ID2D1RenderTarget. Exposed so backend
    /// filter executors can QI to <c>ID2D1DeviceContext</c> and run effects against the
    /// same target without setting up a parallel D2D pipeline.</summary>
    internal nint RenderTargetHandle => _renderTarget;
    private Matrix3x2 _transform = Matrix3x2.Identity;

    private float _globalAlpha = 1f;
    private bool _textPixelSnap = true;
    private Rect? _clipBoundsWorld;

    public Direct2DGraphicsContext(
        Direct2DGraphicsFactory ownerFactory,
        nint hwnd,
        nint d2dFactory,
        nint dwriteFactory,
        nint defaultStrokeStyle,
        Action? onRecreateTarget,
        Func<int>? onPresentTarget,
        bool ownsRenderTarget,
        DWriteTextFormatCache? textFormatCache = null,
        Func<IRenderTarget, (nint renderTarget, int generation)>? resolveRenderTarget = null)
    {
        _ownerFactory = ownerFactory;
        _hwnd = hwnd;
        _d2dFactory = d2dFactory;
        _dwriteFactory = dwriteFactory;
        _defaultStrokeStyle = defaultStrokeStyle;
        _onRecreateTarget = onRecreateTarget;
        _onPresentTarget = onPresentTarget;
        _ownsRenderTarget = ownsRenderTarget;
        _textFormatCache = textFormatCache;
        _resolveRenderTarget = resolveRenderTarget;

        _solidBrushes = RentBrushDict();
        _states = RentStateStack();
        _clipStack = RentClipStack();
    }

    // GPU pixel-surface mode: when the active target is a Direct2DGpuPixelRenderSurface,
    // we render via the factory's shared filter device context (no DC RT, no DIB), with the
    // GPU bitmap set as the device target via dc.SetTarget. Subsequent draws and any effect
    // pass land directly on GPU memory — full zero-copy parity with MewVG's FBO path.
    private nint _gpuPixelSurfaceBitmap; // ID2D1Bitmap1* currently set as target (0 = DIB/normal mode)
    private nint _previousDeviceTarget; // saved for SetTarget restore at EndFrame
    private float _previousDcDpiX; // saved DC dpi for restore at EndFrame (GPU pixel-surface path)
    private float _previousDcDpiY;
    private D2D1_MATRIX_3X2_F _previousDcTransform; // saved DC transform for restore at EndFrame
    private bool _hasPreviousDcTransform;

    protected override void OnBeginFrame(IRenderTarget target)
    {
        _dpiScale = target.DpiScale;

        if (target is Direct2DGpuPixelRenderSurface gpuTarget)
        {
            BeginGpuPixelSurfaceFrame(gpuTarget);
            return;
        }

        if (_resolveRenderTarget != null)
        {
            var (rt, generation) = _resolveRenderTarget(target);
            _renderTarget = rt;
            _renderTargetGeneration = generation;
        }

        // Re-QI device context if render target changed.
        if (_deviceContext != 0)
        {
            ComHelpers.Release(_deviceContext);
            _deviceContext = 0;
        }

        if (_renderTarget != 0 &&
            ComHelpers.QueryInterface(_renderTarget, D2D1.IID_ID2D1DeviceContext, out var dc) >= 0 && dc != 0)
        {
            _deviceContext = dc;
        }

        _globalAlpha = 1f;
        _textPixelSnap = true;
        _transform = Matrix3x2.Identity;
        _clipBoundsWorld = null;

        if (_renderTarget != 0)
        {
            D2D1VTable.BeginDraw((ID2D1RenderTarget*)_renderTarget);
            ConfigureRenderTargetForFrame();
        }
    }

    /// <summary>
    /// Picks the text anti-alias mode (and any other RT-state that depends on the bound
    /// surface's alpha mode) from the actual <c>ID2D1RenderTarget</c> pixel format. ClearType
    /// requires <see cref="D2D1_ALPHA_MODE.IGNORE"/>; on a <c>PREMULTIPLIED</c> target the
    /// subpixel coverage cannot be composed correctly with per-pixel destination alpha and
    /// produces visibly broken (dark / muddy) glyphs, so we degrade to
    /// <see cref="D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE"/>. Querying the RT directly keeps the
    /// decision authoritative regardless of how the target was selected (window swap-chain,
    /// HwndRT, GPU offscreen bitmap, …).
    /// </summary>
    private void ConfigureRenderTargetForFrame()
    {
        if (_renderTarget == 0)
        {
            _clearTypeEnabled = false;
            return;
        }

        var pixelFormat = D2D1VTable.GetPixelFormat((ID2D1RenderTarget*)_renderTarget);
        _clearTypeEnabled = pixelFormat.alphaMode == D2D1_ALPHA_MODE.IGNORE;
        var textAa = _clearTypeEnabled
            ? D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE
            : D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE;
        D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget, textAa);
    }

    // True when this context's BeginGpuPixelSurfaceFrame was the outermost entry on the shared
    // DC and therefore issued the BeginDraw. OnEndFrame uses this to decide whether to
    // issue the matching EndDraw. Nested entries (SvgFilter source layer inside SvgView
    // render) skip both — they only swap the DC's target, leaving the outer BeginDraw
    // active to cover all draws.
    private bool _calledBeginDrawOnSharedDc;

    private void BeginGpuPixelSurfaceFrame(Direct2DGpuPixelRenderSurface gpuTarget)
    {
        // Release any device context held from a previous BeginFrame on this same context
        // instance — keeps refcounting balanced when the context is reused across frames.
        if (_deviceContext != 0)
        {
            ComHelpers.Release(_deviceContext);
            _deviceContext = 0;
        }

        // Use the factory's shared filter device context (NOT a DC RT). The bitmap was
        // created on this DC — they MUST stay paired (cross-DC bitmap usage is undefined).
        nint sharedDc = _ownerFactory.SharedFilterDeviceContext;
        if (sharedDc == 0 || !gpuTarget.IsDeviceCurrent || gpuTarget.Bitmap == 0)
        {
            // GPU path failed to init; this shouldn't happen because the bitmap creation
            // would have thrown. Defensive bail.
            _renderTarget = 0;
            return;
        }

        // _renderTarget and _deviceContext point at the SAME COM object (DeviceContext IS-A
        // RenderTarget) — existing draw methods that take ID2D1RenderTarget* operate on it
        // transparently. _renderTarget is borrowed (matches _ownsRenderTarget=false set at
        // ctor — no Release in OnDispose). _deviceContext is AddRef'd because the existing
        // OnDispose unconditionally Releases it; the AddRef balances that release so the
        // factory-owned shared DC isn't accidentally disposed.
        ComHelpers.AddRef(sharedDc);
        _renderTarget = sharedDc;
        _deviceContext = sharedDc;
        _renderTargetGeneration = 0;

        // Save the DC's previous target so we can restore it on EndFrame. Multiple nested
        // GPU passes (filter source then scratch) chain through the same DC — preserving
        // the parent's target lets nesting work without a per-context push/pop stack.
        D2D1VTable.GetTarget((ID2D1DeviceContext*)sharedDc, out _previousDeviceTarget);
        D2D1VTable.SetTarget((ID2D1DeviceContext*)sharedDc, gpuTarget.Bitmap);
        _gpuPixelSurfaceBitmap = gpuTarget.Bitmap;

        // Sync the DC's DPI to the bound bitmap's DPI. SetTarget does NOT auto-adopt the
        // bitmap's DPI — the DC keeps whatever DPI was last set on it (default 96). If the
        // bitmap's effective scale is e.g. 6× but the DC stays at 96 DPI, user-space
        // drawing coords map 1:1 to pixels, only filling the upper-left 1/6 of the bitmap.
        // We push the bitmap's DPI here so user-space coords scale correctly into pixel
        // coords across the full bitmap. Per-pass: restored from the previous DPI in
        // OnEndFrame so nested frames each see the right DPI for their own bitmap.
        var prevDpiX = 96f;
        var prevDpiY = 96f;
        D2D1VTable.GetDpi((ID2D1RenderTarget*)sharedDc, out prevDpiX, out prevDpiY);
        _previousDcDpiX = prevDpiX;
        _previousDcDpiY = prevDpiY;
        float bitmapDpi = (float)(96.0 * gpuTarget.DpiScale);
        D2D1VTable.SetDpi((ID2D1RenderTarget*)sharedDc, bitmapDpi, bitmapDpi);

        // Save the parent's transform on the DC so we can restore it on EndFrame. This pass
        // is about to push Identity onto the DC for its own clear/draws — without restore,
        // the parent pass continues with our Identity instead of its own translate/scale,
        // and any subsequent draws (cache hits, post-filter content) end up at the wrong
        // pixel positions.
        _previousDcTransform = D2D1VTable.GetTransform((ID2D1RenderTarget*)sharedDc);
        _hasPreviousDcTransform = true;

        _globalAlpha = 1f;
        _textPixelSnap = true;
        _transform = Matrix3x2.Identity;
        _clipBoundsWorld = null;

        // Only the outermost BeginGpuPixelSurfaceFrame issues BeginDraw — D2D rejects nested
        // BeginDraw on the same DC with WRONG_STATE. Inner passes (e.g. SvgFilter source
        // layer rendered while SvgView's outer pass is still drawing into its own GPU
        // pixel surface on the same shared DC) just swap the DC's target via SetTarget;
        // the outer BeginDraw covers their draws too.
        _calledBeginDrawOnSharedDc = _ownerFactory.EnterSharedDcDraw();
        if (_calledBeginDrawOnSharedDc)
        {
            D2D1VTable.BeginDraw((ID2D1RenderTarget*)_renderTarget);
        }
        // Reset DC transform — the shared DC carries state across BeginDraw cycles AND
        // across nested SetTarget swaps. Push Identity now so the upcoming Clear / first
        // draw can't accidentally inherit a stale matrix from a parent pass.
        D2D1VTable.SetTransform((ID2D1RenderTarget*)_renderTarget, D2D1_MATRIX_3X2_F.Identity);
        // GPU bitmaps allocate with undefined contents (and a recycled bitmap from the
        // pool may hold the previous filter's residue). Clear to transparent up front so
        // the SVG element renders against a clean slate.
        D2D1VTable.Clear((ID2D1RenderTarget*)_renderTarget, new D2D1_COLOR_F(0, 0, 0, 0));
        ConfigureRenderTargetForFrame();
    }

    private enum ClipKind
    {
        AxisAligned, Layer
    }

    public override ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    private double _dpiScale;
    public override double DpiScale => _dpiScale;

    public override float GlobalAlpha
    {
        get => _globalAlpha;
        set => _globalAlpha = Math.Clamp(value, 0f, 1f);
    }

    public override bool TextPixelSnap
    {
        get => _textPixelSnap;
        set
        {
            if (_textPixelSnap == value)
            {
                return;
            }

            _textPixelSnap = value;
            if (_renderTarget == 0)
            {
                return;
            }
            // ClearType forces pixel snapping for subpixel RGB alignment.
            // Switch to grayscale so NO_SNAP actually takes effect.
            if (_clearTypeEnabled)
            {
                D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget,
                    value ? D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE : D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE);
            }
        }
    }

    protected override void OnEndFrame()
    {
        try
        {
            TextTracker?.Cleanup();

            if (_renderTarget != 0)
            {
                while (_clipStack.Count > 0)
                {
                    PopClip();
                }

                // GPU pixel-surface path: only the outermost BeginGpuPixelSurfaceFrame issued BeginDraw,
                // so only that one calls EndDraw. Nested inner passes leave EndDraw to the
                // parent. For non-GPU paths (DC RT etc.), each BeginFrame paired its own
                // BeginDraw and must EndDraw here.
                bool shouldEndDraw = true;
                if (_gpuPixelSurfaceBitmap != 0)
                {
                    _ownerFactory.ExitSharedDcDraw();
                    shouldEndDraw = _calledBeginDrawOnSharedDc;
                    _calledBeginDrawOnSharedDc = false;
                }

                if (shouldEndDraw)
                {
                    int hr = D2D1VTable.EndDraw((ID2D1RenderTarget*)_renderTarget);
                    _ = _ownerFactory.NotifyGpuDeviceLost(hr);
                    AssertHr(hr, "EndDraw");
                    if (hr == D2DERR_RECREATE_TARGET || hr == D2DERR_WRONG_RESOURCE_DOMAIN)
                    {
                        _onRecreateTarget?.Invoke();
                    }

                    if (hr >= 0 && _onPresentTarget != null)
                    {
                        int presentHr = _onPresentTarget();
                        _ = _ownerFactory.NotifyGpuDeviceLost(presentHr);
                        AssertHr(presentHr, "Present");
                        if (presentHr < 0 && Direct2DGraphicsFactory.IsRecoverableGpuDeviceChainFailure(presentHr))
                        {
                            _onRecreateTarget?.Invoke();
                        }
                    }
                }
            }

            if (_gpuPixelSurfaceBitmap != 0 && _renderTarget != 0)
            {
                // Restore parent's target so the next draw on the shared DC isn't writing into
                // our scratch bitmap. _previousDeviceTarget may be 0 (no parent target) — that
                // also restores cleanly because SetTarget(NULL) resets to "no target".
                D2D1VTable.SetTarget((ID2D1DeviceContext*)_renderTarget, _previousDeviceTarget);
                if (_previousDeviceTarget != 0)
                {
                    ComHelpers.Release(_previousDeviceTarget);
                    _previousDeviceTarget = 0;
                }
                // Restore the DC's DPI to whatever the parent pass had set. Without this,
                // the inner GPU pass's DPI leaks into the outer pass's draws and either
                // shrinks or expands them (e.g. SvgFilter source layer at DPI 1× leaving
                // the DC at 1× when SvgView's outer pass continues at 6× expectation).
                if (_previousDcDpiX > 0 && _previousDcDpiY > 0)
                {
                    D2D1VTable.SetDpi((ID2D1RenderTarget*)_renderTarget, _previousDcDpiX, _previousDcDpiY);
                }
                _previousDcDpiX = 0;
                _previousDcDpiY = 0;

                // Restore the DC's transform so the parent pass continues with its own
                // translate/scale (the C# parent context's _transform field has stayed in
                // sync, but the native DC was clobbered when this pass set Identity).
                if (_hasPreviousDcTransform)
                {
                    D2D1VTable.SetTransform((ID2D1RenderTarget*)_renderTarget, _previousDcTransform);
                    _hasPreviousDcTransform = false;
                }
                _gpuPixelSurfaceBitmap = 0;
            }
        }
        finally
        {
            foreach (var (_, brush) in _solidBrushes)
            {
                ComHelpers.Release(brush);
            }

            _solidBrushes.Clear();
            _states.Clear();
            _clipStack.Clear();
        }
    }

    protected override void OnDispose()
    {
        CollectionPool<Dictionary<uint, nint>>.Return(_solidBrushes);
        CollectionPool<Stack<(Matrix3x2, float, int, Rect?, bool)>>.Return(_states);
        CollectionPool<Stack<ClipEntry>>.Return(_clipStack);

        if (_deviceContext != 0)
        {
            ComHelpers.Release(_deviceContext);
            _deviceContext = 0;
        }

        if (_ownsRenderTarget && _renderTarget != 0)
        {
            ComHelpers.Release(_renderTarget);
        }

        _renderTarget = 0;
    }

    public override void Clear(Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        D2D1VTable.Clear((ID2D1RenderTarget*)_renderTarget, ToColorF(color));
    }

    public override void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        RecordDrawPath();
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null || color.A == 0 || thickness <= 0)
        {
            return;
        }

        nint geometry = BuildD2DPathGeometry(path);
        if (geometry == 0)
        {
            return;
        }

        try
        {
            nint brush = GetSolidBrush(color);
            float stroke = QuantizeStrokeDip((float)thickness);
            D2D1VTable.DrawGeometry((ID2D1RenderTarget*)_renderTarget, geometry, brush, stroke, _defaultStrokeStyle);
        }
        finally
        {
            ComHelpers.Release(geometry);
        }
    }

    public override void FillPath(PathGeometry path, Color color)
    {
        FillPath(path, color, FillRule.NonZero);
    }

    public override void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        RecordFillPath();
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null || color.A == 0)
        {
            return;
        }

        nint geometry = BuildD2DPathGeometry(path, fillRule);
        if (geometry == 0)
        {
            return;
        }

        try
        {
            nint brush = GetSolidBrush(color);
            D2D1VTable.FillGeometry((ID2D1RenderTarget*)_renderTarget, geometry, brush);
        }
        finally
        {
            ComHelpers.Release(geometry);
        }
    }

    public override void FillPath(PathGeometry path, IBrush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (brush is not ISolidColorBrush)
        {
            RecordFillPath();
        }
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null)
        {
            return;
        }

        if (brush is ISolidColorBrush solid)
        {
            FillPath(path, solid.Color, fillRule);
            return;
        }
        if (brush is IGradientBrush gradient)
        {
            nint geometry = BuildD2DPathGeometry(path, fillRule);
            if (geometry == 0)
            {
                return;
            }

            var objectBounds = gradient.GradientUnits == GradientUnits.ObjectBoundingBox
                ? path.GetBounds()
                : default;
            nint gradBrush = CreateGradientBrush(gradient, objectBounds, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.FillGeometry((ID2D1RenderTarget*)_renderTarget, geometry, gradBrush);
                }
            }
            finally { ComHelpers.Release(geometry); ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
            return;
        }
        if (brush is IImageBrush imageBrush)
        {
            nint geometry = BuildD2DPathGeometry(path, fillRule);
            if (geometry == 0)
            {
                return;
            }

            nint bmpBrush = CreateImageBrushHandle(imageBrush);
            try
            {
                if (bmpBrush != 0)
                {
                    D2D1VTable.FillGeometry((ID2D1RenderTarget*)_renderTarget, geometry, bmpBrush);
                }
            }
            finally { ComHelpers.Release(geometry); ComHelpers.Release(bmpBrush); }
        }
    }

    public override void FillRectangle(Rect rect, IBrush brush)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        if (brush is ISolidColorBrush solid) { FillRectangle(rect, solid.Color); return; }
        if (brush is IGradientBrush gradient)
        {
            nint gradBrush = CreateGradientBrush(gradient, rect, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.FillRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), gradBrush);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
            return;
        }
        if (brush is IImageBrush imageBrush)
        {
            nint bmpBrush = CreateImageBrushHandle(imageBrush);
            try
            {
                if (bmpBrush != 0)
                {
                    D2D1VTable.FillRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), bmpBrush);
                }
            }
            finally { ComHelpers.Release(bmpBrush); }
        }
    }

    public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        if (brush is ISolidColorBrush solid) { FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return; }
        if (brush is IGradientBrush gradient)
        {
            nint gradBrush = CreateGradientBrush(gradient, rect, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.FillRoundedRectangle((ID2D1RenderTarget*)_renderTarget,
                        new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY), gradBrush);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
            return;
        }
        if (brush is IImageBrush imageBrush)
        {
            nint bmpBrush = CreateImageBrushHandle(imageBrush);
            try
            {
                if (bmpBrush != 0)
                {
                    D2D1VTable.FillRoundedRectangle((ID2D1RenderTarget*)_renderTarget,
                        new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY), bmpBrush);
                }
            }
            finally { ComHelpers.Release(bmpBrush); }
        }
    }

    public override void FillEllipse(Rect bounds, IBrush brush)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        if (brush is ISolidColorBrush solid) { FillEllipse(bounds, solid.Color); return; }
        if (brush is IGradientBrush gradient)
        {
            nint gradBrush = CreateGradientBrush(gradient, bounds, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    var center = new D2D1_POINT_2F(
                        (float)(bounds.X + bounds.Width / 2),
                        (float)(bounds.Y + bounds.Height / 2));
                    D2D1VTable.FillEllipse((ID2D1RenderTarget*)_renderTarget,
                        new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2)), gradBrush);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
            return;
        }
        if (brush is IImageBrush imageBrush)
        {
            nint bmpBrush = CreateImageBrushHandle(imageBrush);
            try
            {
                if (bmpBrush != 0)
                {
                    var center = new D2D1_POINT_2F(
                        (float)(bounds.X + bounds.Width / 2),
                        (float)(bounds.Y + bounds.Height / 2));
                    D2D1VTable.FillEllipse((ID2D1RenderTarget*)_renderTarget,
                        new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2)), bmpBrush);
                }
            }
            finally { ComHelpers.Release(bmpBrush); }
        }
    }

    public override void DrawPath(PathGeometry path, IPen pen)
    {
        RecordDrawPath();
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null || pen.Thickness <= 0)
        {
            return;
        }

        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;

        nint geometry = BuildD2DPathGeometry(path, FillRule.NonZero);
        if (geometry == 0)
        {
            return;
        }

        try
        {
            if (pen.Brush is IGradientBrush gradient)
            {
                var objectBounds = gradient.GradientUnits == GradientUnits.ObjectBoundingBox
                    ? path.GetBounds()
                    : default;
                nint gradBrush = CreateGradientBrush(gradient, objectBounds, out nint stopCol);
                try
                {
                    if (gradBrush != 0)
                    {
                        D2D1VTable.DrawGeometry((ID2D1RenderTarget*)_renderTarget, geometry, gradBrush, stroke, ssHandle);
                    }
                }
                finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
            }
            else
            {
                Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
                if (color.A == 0)
                {
                    return;
                }

                nint brush = GetSolidBrush(color);
                D2D1VTable.DrawGeometry((ID2D1RenderTarget*)_renderTarget, geometry, brush, stroke, ssHandle);
            }
        }
        finally
        {
            ComHelpers.Release(geometry);
        }
    }

    public override void DrawLine(Point start, Point end, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0)
        {
            return;
        }

        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;
        var p0 = ToPoint2F(start);
        var p1 = ToPoint2F(end);

        if (pen.Brush is IGradientBrush gradient)
        {
            Rect objectBounds = default;
            if (gradient.GradientUnits == GradientUnits.ObjectBoundingBox)
            {
                double minX = Math.Min(start.X, end.X);
                double minY = Math.Min(start.Y, end.Y);
                double maxX = Math.Max(start.X, end.X);
                double maxY = Math.Max(start.Y, end.Y);
                objectBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            }
            nint gradBrush = CreateGradientBrush(gradient, objectBounds, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, gradBrush, stroke, ssHandle);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0)
            {
                return;
            }

            D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override void DrawRectangle(Rect rect, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0)
        {
            return;
        }

        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;
        var rf = ToRectF(rect);

        if (pen.Brush is IGradientBrush gradient)
        {
            nint gradBrush = CreateGradientBrush(gradient, rect, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, rf, gradBrush, stroke, ssHandle);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0)
            {
                return;
            }

            D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, rf, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0)
        {
            return;
        }

        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);

        if (pen.Brush is IGradientBrush gradient)
        {
            nint gradBrush = CreateGradientBrush(gradient, rect, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, gradBrush, stroke, ssHandle);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0)
            {
                return;
            }

            D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override void DrawEllipse(Rect bounds, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0)
        {
            return;
        }

        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;

        var center = new D2D1_POINT_2F(
            (float)(bounds.X + bounds.Width / 2),
            (float)(bounds.Y + bounds.Height / 2));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));

        if (pen.Brush is IGradientBrush gradient)
        {
            nint gradBrush = CreateGradientBrush(gradient, bounds, out nint stopCol);
            try
            {
                if (gradBrush != 0)
                {
                    D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, gradBrush, stroke, ssHandle);
                }
            }
            finally { ComHelpers.Release(gradBrush); ComHelpers.Release(stopCol); }
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0)
            {
                return;
            }

            D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override TextLayout? CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
    {
        if (text.IsEmpty)
        {
            return null;
        }

        if (format.Font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(format));
        }

        var bounds = constraints.Bounds;
        double maxWidth = double.IsPositiveInfinity(bounds.Width) ? float.MaxValue : Math.Max(0, bounds.Width);

        // Use cached native format when available, fall back to temporary.
        nint nativeFormat;
        bool ownFormat;
        if (_textFormatCache != null)
        {
            nativeFormat = _textFormatCache.GetOrCreate(_dwriteFactory, dwFont,
                format.HorizontalAlignment, format.VerticalAlignment, format.Wrapping);
            ownFormat = false;
        }
        else
        {
            nativeFormat = CreateDWriteTextFormat(dwFont, format.HorizontalAlignment, format.VerticalAlignment, format.Wrapping);
            ownFormat = true;
        }
        if (nativeFormat == 0)
        {
            return null;
        }

        float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)maxWidth;
        float h = bounds.Height > 0 && !double.IsPositiveInfinity(bounds.Height) ? (float)bounds.Height : float.MaxValue;
        int hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, nativeFormat, w, h, out nint nativeLayout);

        if (hr < 0 || nativeLayout == 0)
        {
            if (ownFormat)
            {
                ComHelpers.Release(nativeFormat);
            }

            return null;
        }

        ApplyCustomFontFallback(nativeLayout);

        // Apply trimming if requested.
        if (format.Trimming == TextTrimming.CharacterEllipsis)
        {
            DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)_dwriteFactory, nativeFormat, out nint trimmingSign);
            var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
            DWriteVTable.SetTrimming(nativeLayout, dwriteTrimming, trimmingSign);
            ComHelpers.Release(trimmingSign);
        }

        if (ownFormat)
        {
            ComHelpers.Release(nativeFormat);
        }

        hr = DWriteVTable.GetMetrics(nativeLayout, out var metrics);
        if (hr < 0)
        {
            ComHelpers.Release(nativeLayout);
            return null;
        }

        var height = metrics.height;
        if (metrics.top < 0)
        {
            height += -metrics.top;
        }

        var measured = new Size(TextMeasurePolicy.ApplyWidthPadding(metrics.widthIncludingTrailingWhitespace), height);
        double effectiveMaxWidth = bounds.Width > 0 && !double.IsPositiveInfinity(bounds.Width) ? bounds.Width : measured.Width;

        var result = new TextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = bounds,
            EffectiveMaxWidth = effectiveMaxWidth,
            ContentHeight = measured.Height,
            BackendHandle = nativeLayout
        };
        TextTracker?.TrackLayout(result);

        return result;
    }

    public override void DrawTextLayout(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color)
    {
        if (layout == null)
        {
            return;
        }

        var bounds = layout.EffectiveBounds;
        if (_renderTarget == 0 || text.IsEmpty)
        {
            return;
        }

        if (_clipBoundsWorld.HasValue && bounds.Width < 100_000)
        {
            var clip = _clipBoundsWorld.Value;
            var wv = Vector2.Transform(new Vector2((float)bounds.X, (float)bounds.Y), _transform);
            if (wv.X + bounds.Width <= clip.X || wv.X >= clip.Right ||
                wv.Y + bounds.Height <= clip.Y || wv.Y >= clip.Bottom)
            {
                return;
            }
        }

        if (layout.BackendHandle == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        var options = _textPixelSnap
            ? D2D1_DRAW_TEXT_OPTIONS.CLIP | D2D1_DRAW_TEXT_OPTIONS.ENABLE_COLOR_FONT
            : D2D1_DRAW_TEXT_OPTIONS.NO_SNAP | D2D1_DRAW_TEXT_OPTIONS.CLIP | D2D1_DRAW_TEXT_OPTIONS.ENABLE_COLOR_FONT;

        var rt = _deviceContext != 0 ? _deviceContext : _renderTarget;
        D2D1VTable.DrawTextLayout((ID2D1RenderTarget*)rt,
            new D2D1_POINT_2F((float)bounds.X, (float)bounds.Y), layout.BackendHandle, brush, options);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => MeasureTextDirect(text, font, float.MaxValue);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => MeasureTextDirect(text, font, maxWidth);

    public override void DrawImage(IImage image, Point location) =>
        DrawImageCore(image, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));

    protected override void SaveCore()
        => _states.Push((_transform, _globalAlpha, _clipStack.Count, _clipBoundsWorld, _textPixelSnap));

    protected override void RestoreCore()
    {
        if (_states.Count == 0 || _renderTarget == 0)
        {
            return;
        }

        var state = _states.Pop();
        while (_clipStack.Count > state.clipCount)
        {
            PopClip();
        }

        _transform = state.transform;
        _globalAlpha = state.globalAlpha;

        bool snapChanged = _textPixelSnap != state.textPixelSnap;
        _textPixelSnap = state.textPixelSnap;
        _clipBoundsWorld = state.clipBoundsWorld;

        SyncNativeTransform();

        if (snapChanged && _clearTypeEnabled)
        {
            D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget,
                _textPixelSnap ? D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE : D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE);
        }
    }

    protected override void SetClipCore(Rect rect)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        // Track bounding box in world space for text-culling heuristics.
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, TransformRect(rect));

        D2D1VTable.PushAxisAlignedClip((ID2D1RenderTarget*)_renderTarget, ToRectF(rect));
        _clipStack.Push(new ClipEntry(ClipKind.AxisAligned, 0, 0));
    }

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        if (_d2dFactory == 0)
        {
            SetClip(rect);
            return;
        }

        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, TransformRect(rect));

        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        int hr = D2D1VTable.CreateRoundedRectangleGeometry((ID2D1Factory*)_d2dFactory, rr, out var geometry);
        if (hr < 0 || geometry == 0)
        {
            SetClip(rect);
            return;
        }

        hr = D2D1VTable.CreateLayer((ID2D1RenderTarget*)_renderTarget, out var layer);
        if (hr < 0 || layer == 0)
        {
            ComHelpers.Release(geometry);
            SetClip(rect);
            return;
        }

        if (_deviceContext != 0)
        {
            // D2D 1.1: INITIALIZE_FROM_BACKGROUND copies the existing render target content into the layer,
            // providing an opaque backing so ClearType works even without an explicit background fill.
            var parameters1 = new D2D1_LAYER_PARAMETERS1(
                contentBounds: ToRectF(rect),
                geometricMask: geometry,
                maskAntialiasMode: D2D1_ANTIALIAS_MODE.PER_PRIMITIVE,
                maskTransform: D2D1_MATRIX_3X2_F.Identity,
                opacity: 1.0f,
                opacityBrush: 0,
                layerOptions: D2D1_LAYER_OPTIONS1.INITIALIZE_FROM_BACKGROUND);

            D2D1VTable.PushLayer((ID2D1DeviceContext*)_deviceContext, parameters1, layer);
        }
        else
        {
            var parameters = new D2D1_LAYER_PARAMETERS(
                contentBounds: ToRectF(rect),
                geometricMask: geometry,
                maskAntialiasMode: D2D1_ANTIALIAS_MODE.PER_PRIMITIVE,
                maskTransform: D2D1_MATRIX_3X2_F.Identity,
                opacity: 1.0f,
                opacityBrush: 0,
                layerOptions: _clearTypeEnabled ? D2D1_LAYER_OPTIONS.INITIALIZE_FOR_CLEARTYPE : D2D1_LAYER_OPTIONS.NONE);

            D2D1VTable.PushLayer((ID2D1RenderTarget*)_renderTarget, parameters, layer);
        }

        _clipStack.Push(new ClipEntry(ClipKind.Layer, layer, geometry));
    }

    protected override void SetClipPathCore(PathGeometry path)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        var bounds = path.GetBounds();
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, TransformRect(bounds));

        nint geometry = BuildD2DPathGeometry(path, path.FillRule);
        if (geometry == 0)
        {
            SetClip(bounds);
            return;
        }

        int hr = D2D1VTable.CreateLayer((ID2D1RenderTarget*)_renderTarget, out var layer);
        if (hr < 0 || layer == 0)
        {
            ComHelpers.Release(geometry);
            SetClip(bounds);
            return;
        }

        if (_deviceContext != 0)
        {
            var parameters1 = new D2D1_LAYER_PARAMETERS1(
                contentBounds: ToRectF(bounds),
                geometricMask: geometry,
                maskAntialiasMode: D2D1_ANTIALIAS_MODE.PER_PRIMITIVE,
                maskTransform: D2D1_MATRIX_3X2_F.Identity,
                opacity: 1.0f,
                opacityBrush: 0,
                layerOptions: D2D1_LAYER_OPTIONS1.INITIALIZE_FROM_BACKGROUND);

            D2D1VTable.PushLayer((ID2D1DeviceContext*)_deviceContext, parameters1, layer);
        }
        else
        {
            var parameters = new D2D1_LAYER_PARAMETERS(
                contentBounds: ToRectF(bounds),
                geometricMask: geometry,
                maskAntialiasMode: D2D1_ANTIALIAS_MODE.PER_PRIMITIVE,
                maskTransform: D2D1_MATRIX_3X2_F.Identity,
                opacity: 1.0f,
                opacityBrush: 0,
                layerOptions: _clearTypeEnabled ? D2D1_LAYER_OPTIONS.INITIALIZE_FOR_CLEARTYPE : D2D1_LAYER_OPTIONS.NONE);

            D2D1VTable.PushLayer((ID2D1RenderTarget*)_renderTarget, parameters, layer);
        }

        _clipStack.Push(new ClipEntry(ClipKind.Layer, layer, geometry));
    }

    protected override void ResetClipCore()
    {
        // Pop clips back to the save-boundary, or all clips if no save was pushed.
        int targetCount = _states.Count > 0 ? _states.Peek().clipCount : 0;
        while (_clipStack.Count > targetCount)
        {
            PopClip();
        }

        _clipBoundsWorld = null;
    }

    protected override void TranslateCore(double dx, double dy)
    {
        _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;
        SyncNativeTransform();
    }

    protected override void RotateCore(double angleRadians)
    {
        _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;
        SyncNativeTransform();
    }

    protected override void ScaleCore(double sx, double sy)
    {
        _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;
        SyncNativeTransform();
    }

    protected override void SetTransformCore(Matrix3x2 matrix)
    {
        _transform = matrix;
        SyncNativeTransform();
    }

    protected override Matrix3x2 GetTransformCore() => _transform;

    protected override void ResetTransformCore()
    {
        _transform = Matrix3x2.Identity;
        SyncNativeTransform();
    }

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var p0 = ToPoint2F(start);
        var p1 = ToPoint2F(end);

        D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, brush, stroke, _defaultStrokeStyle);
    }

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness, bool strokeInset)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        if (strokeInset)
        {
            rect = rect.Deflate(new Thickness(QuantizeHalfStroke(thickness, DpiScale)));
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), brush, stroke, _defaultStrokeStyle);
    }

    protected override void FillRectangleCore(Rect rect, Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        D2D1VTable.FillRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), brush);
    }

    protected override void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, brush, stroke, _defaultStrokeStyle);
    }

    protected override void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        D2D1VTable.FillRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, brush);
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);

        var center = new D2D1_POINT_2F(
            (float)(bounds.X + bounds.Width / 2),
            (float)(bounds.Y + bounds.Height / 2));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));
        D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, brush, stroke, _defaultStrokeStyle);
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        var center = new D2D1_POINT_2F(
            (float)(bounds.X + bounds.Width / 2),
            (float)(bounds.Y + bounds.Height / 2));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));
        D2D1VTable.FillEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, brush);
    }

    protected override void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        if (_renderTarget == 0 || text.IsEmpty)
        {
            return;
        }

        if (_clipBoundsWorld.HasValue && bounds.Width < 100_000)
        {
            var clip = _clipBoundsWorld.Value;
            var wv = Vector2.Transform(new Vector2((float)bounds.X, (float)bounds.Y), _transform);
            if (wv.X + bounds.Width <= clip.X || wv.X >= clip.Right ||
                wv.Y + bounds.Height <= clip.Y || wv.Y >= clip.Bottom)
            {
                return;
            }
        }

        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));
        }

        nint textFormat = CreateDWriteTextFormat(dwFont, horizontalAlignment, verticalAlignment, wrapping);
        if (textFormat == 0)
        {
            return;
        }

        // Build layout rect so that width/height are converted to float independently of position,
        // avoiding float precision loss from (float)(X+W) - (float)X != (float)W.
        float left = (float)bounds.X;
        float top = (float)bounds.Y;
        float w = (float)bounds.Width;
        float h = (float)bounds.Height;
        var layoutRect = new D2D1_RECT_F(left, top, left + w, top + h);

        nint textLayout = 0;
        nint trimmingSign = 0;
        try
        {
            nint brush = GetSolidBrush(color);
            var options = _textPixelSnap
                ? D2D1_DRAW_TEXT_OPTIONS.CLIP | D2D1_DRAW_TEXT_OPTIONS.ENABLE_COLOR_FONT
                : D2D1_DRAW_TEXT_OPTIONS.NO_SNAP | D2D1_DRAW_TEXT_OPTIONS.CLIP | D2D1_DRAW_TEXT_OPTIONS.ENABLE_COLOR_FONT;

            if (trimming == TextTrimming.CharacterEllipsis)
            {
                int hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat,
                    w, h, out textLayout);
                if (hr >= 0 && textLayout != 0)
                {
                    ApplyCustomFontFallback(textLayout);
                    DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)_dwriteFactory, textFormat, out trimmingSign);
                    var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                    DWriteVTable.SetTrimming(textLayout, dwriteTrimming, trimmingSign);
                    var rtLayout = _deviceContext != 0 ? _deviceContext : _renderTarget;
                    D2D1VTable.DrawTextLayout((ID2D1RenderTarget*)rtLayout,
                        new D2D1_POINT_2F(left, top), textLayout, brush, options);
                    return;
                }
            }

            // Use ID2D1DeviceContext (D2D 1.1) when available — required for ENABLE_COLOR_FONT.
            var rt = _deviceContext != 0 ? _deviceContext : _renderTarget;
            D2D1VTable.DrawText((ID2D1RenderTarget*)rt, text, textFormat, layoutRect, brush, options);
        }
        finally
        {
            ComHelpers.Release(trimmingSign);
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    protected override void DrawImageCore(IImage image, Rect destRect) =>
        DrawImageCore(image, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
    {
        switch (image)
        {
            case Direct2DImage d2dImage:
                DrawImageCore(d2dImage, destRect, sourceRect);
                return;
            case Direct2DNativeBitmapImage nativeBitmapImage:
                DrawImageCore(nativeBitmapImage, destRect, sourceRect);
                return;
            case Direct2DDxgiSurfaceImage dxgiSurfaceImage:
                DrawImageCore(dxgiSurfaceImage, destRect, sourceRect);
                return;
            default:
                throw new ArgumentException("Image must be a Direct2D backend image", nameof(image));
        }
    }

    private static Dictionary<uint, nint> RentBrushDict() => CollectionPool<Dictionary<uint, nint>>.Rent();

    private static Stack<(Matrix3x2, float, int, Rect?, bool)> RentStateStack() => CollectionPool<Stack<(Matrix3x2, float, int, Rect?, bool)>>.Rent();

    private static Stack<ClipEntry> RentClipStack() => CollectionPool<Stack<ClipEntry>>.Rent();

    [Conditional("DEBUG")]
    private static void AssertHr(int hr, string op)
    {
        if (hr >= 0)
        {
            return;
        }

        string msg = $"Direct2D: {op} failed: 0x{hr:X8}";
        Debug.Fail(msg);
        DiagLog.Write(msg);
    }


    private static D2D1_MATRIX_3X2_F ToMatrix3x2F(Matrix3x2 m)
        => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

    private static D2D1_COLOR_F ToColorF(Color color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static D2D1_POINT_2F ToPoint2F(Point point) =>
        new((float)point.X, (float)point.Y);

    private static D2D1_RECT_F ToRectF(Rect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    private static Rect IntersectClipBounds(Rect? current, Rect next)
    {
        if (!current.HasValue)
        {
            return next;
        }

        double left = Math.Max(current.Value.X, next.X);
        double top = Math.Max(current.Value.Y, next.Y);
        double right = Math.Min(current.Value.Right, next.Right);
        double bottom = Math.Min(current.Value.Bottom, next.Bottom);
        return right > left && bottom > top
            ? new Rect(left, top, right - left, bottom - top)
            : new Rect(left, top, 0, 0);
    }

    private void SyncNativeTransform()
    {
        if (_renderTarget == 0)
        {
            return;
        }

        var m = new D2D1_MATRIX_3X2_F(
            _transform.M11, _transform.M12,
            _transform.M21, _transform.M22,
            _transform.M31, _transform.M32);
        D2D1VTable.SetTransform((ID2D1RenderTarget*)_renderTarget, m);
    }

    private nint BuildD2DPathGeometry(PathGeometry path, FillRule fillRule = FillRule.NonZero)
    {
        int hr = D2D1VTable.CreatePathGeometry((ID2D1Factory*)_d2dFactory, out nint geometry);
        if (hr < 0 || geometry == 0)
        {
            return 0;
        }

        hr = D2D1VTable.OpenPathGeometry((ID2D1Geometry*)geometry, out nint sink);
        if (hr < 0 || sink == 0)
        {
            ComHelpers.Release(geometry);
            return 0;
        }

        bool figureOpen = false;
        try
        {
            var d2dFillMode = fillRule == FillRule.EvenOdd ? D2D1_FILL_MODE.ALTERNATE : D2D1_FILL_MODE.WINDING;
            D2D1VTable.SetFillMode((ID2D1GeometrySink*)sink, d2dFillMode);

            foreach (var cmd in path.Commands)
            {
                switch (cmd.Type)
                {
                    case PathCommandType.MoveTo:
                        if (figureOpen)
                        {
                            D2D1VTable.EndFigure((ID2D1GeometrySink*)sink, D2D1_FIGURE_END.OPEN);
                            figureOpen = false;
                        }
                        D2D1VTable.BeginFigure((ID2D1GeometrySink*)sink,
                            new D2D1_POINT_2F((float)cmd.X0, (float)cmd.Y0),
                            D2D1_FIGURE_BEGIN.FILLED);
                        figureOpen = true;
                        break;

                    case PathCommandType.LineTo:
                        if (figureOpen)
                        {
                            D2D1VTable.AddLine((ID2D1GeometrySink*)sink,
                                new D2D1_POINT_2F((float)cmd.X0, (float)cmd.Y0));
                        }

                        break;

                    case PathCommandType.BezierTo:
                        if (figureOpen)
                        {
                            var bezier = new D2D1_BEZIER_SEGMENT(
                                new D2D1_POINT_2F((float)cmd.X0, (float)cmd.Y0),
                                new D2D1_POINT_2F((float)cmd.X1, (float)cmd.Y1),
                                new D2D1_POINT_2F((float)cmd.X2, (float)cmd.Y2));
                            D2D1VTable.AddBezier((ID2D1GeometrySink*)sink, bezier);
                        }
                        break;

                    case PathCommandType.Close:
                        if (figureOpen)
                        {
                            D2D1VTable.EndFigure((ID2D1GeometrySink*)sink, D2D1_FIGURE_END.CLOSED);
                            figureOpen = false;
                        }
                        break;
                }
            }

            if (figureOpen)
            {
                D2D1VTable.EndFigure((ID2D1GeometrySink*)sink, D2D1_FIGURE_END.OPEN);
            }

            hr = D2D1VTable.CloseGeometrySink((ID2D1GeometrySink*)sink);
        }
        finally
        {
            ComHelpers.Release(sink);
        }

        if (hr < 0)
        {
            ComHelpers.Release(geometry);
            return 0;
        }

        return geometry;
    }

    private Size MeasureTextDirect(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));
        }

        nint textFormat = 0;
        nint textLayout = 0;
        try
        {
            textFormat = CreateDWriteTextFormat(dwFont, TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap);
            if (textFormat == 0)
            {
                return Size.Empty;
            }

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)Math.Max(0, maxWidth);
            int hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return Size.Empty;
            }

            ApplyCustomFontFallback(textLayout);

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0)
            {
                return Size.Empty;
            }

            var height = metrics.height;
            if (metrics.top < 0)
            {
                height += -metrics.top;
            }

            return new Size(TextMeasurePolicy.ApplyWidthPadding(metrics.widthIncludingTrailingWhitespace), height);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    private nint CreateDWriteTextFormat(DirectWriteFont font, TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment, TextWrapping wrapping)
    {
        var weight = (DWRITE_FONT_WEIGHT)(int)font.Weight;
        var style = font.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
        // Use private font collection if available (for fonts loaded via FontResources.Register)
        int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, font.Family,
            font.PrivateFontCollection, weight, style, (float)font.Size, out nint textFormat);
        if (hr < 0 || textFormat == 0)
        {
            return 0;
        }

        DWriteVTable.SetTextAlignment(textFormat, horizontalAlignment switch
        {
            TextAlignment.Left => DWRITE_TEXT_ALIGNMENT.LEADING,
            TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.CENTER,
            TextAlignment.Right => DWRITE_TEXT_ALIGNMENT.TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT.LEADING
        });

        DWriteVTable.SetParagraphAlignment(textFormat, verticalAlignment switch
        {
            TextAlignment.Top => DWRITE_PARAGRAPH_ALIGNMENT.NEAR,
            TextAlignment.Center => DWRITE_PARAGRAPH_ALIGNMENT.CENTER,
            TextAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT.FAR,
            _ => DWRITE_PARAGRAPH_ALIGNMENT.NEAR
        });

        DWriteVTable.SetWordWrapping(textFormat,
            wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);
        return textFormat;
    }

    /// <summary>
    /// Applies the user-configured font fallback chain to a text layout (if any).
    /// Uses IDWriteTextLayout2::SetFontFallback with a custom IDWriteFontFallback
    /// built from <see cref="FontFallback.FallbackChain"/>.
    /// Safe to call on any layout — silently no-ops if IDWriteFactory2 is unavailable.
    /// </summary>
    private void ApplyCustomFontFallback(nint textLayout)
    {
        if (textLayout == 0)
        {
            return;
        }

        var fallback = DWriteFontFallbackHelper.GetOrCreate((IDWriteFactory*)_dwriteFactory);
        if (fallback == 0)
        {
            return;
        }

        // This may fail if the layout doesn't support IDWriteTextLayout2 — that's fine.
        _ = DWriteTextLayout2VTable.SetFontFallback(textLayout, fallback);
    }

    private void DrawImageCore(Direct2DImage image, Rect destRect, Rect sourceRect)
        => DrawImageBitmapCore(image.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration), destRect, sourceRect);

    private void DrawImageCore(Direct2DNativeBitmapImage image, Rect destRect, Rect sourceRect)
        => DrawImageBitmapCore(image.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration), destRect, sourceRect);

    private void DrawImageCore(Direct2DDxgiSurfaceImage image, Rect destRect, Rect sourceRect)
        => DrawImageBitmapCore(image.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration), destRect, sourceRect);

    private void DrawImageBitmapCore(nint bmp, Rect destRect, Rect sourceRect)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        if (bmp == 0)
        {
            return;
        }

        // Pixel-snap destination rect in world space to avoid shimmer.
        // Use the translation components of _transform (M31/M32); this gives the correct result
        // for the common pure-translation case and degrades gracefully for rotated transforms.
        double tx = _transform.M31;
        double ty = _transform.M32;
        var worldDest = new Rect(destRect.X + tx, destRect.Y + ty, destRect.Width, destRect.Height);
        var snappedWorldDest = LayoutRounding.SnapRectEdgesToPixels(worldDest, DpiScale);
        var snappedLocalDest = new Rect(
            snappedWorldDest.X - tx,
            snappedWorldDest.Y - ty,
            snappedWorldDest.Width,
            snappedWorldDest.Height);

        var dst = ToRectF(snappedLocalDest);
        var src = new D2D1_RECT_F(
            left: (float)sourceRect.X,
            top: (float)sourceRect.Y,
            right: (float)sourceRect.Right,
            bottom: (float)sourceRect.Bottom);

        // When the active target is a DeviceContext (the common case — shared filter DC,
        // window via QI'd DC), route through the DC-overload of DrawBitmap which accepts
        // the full D2D1_INTERPOLATION_MODE enum. Default → HIGH_QUALITY_CUBIC: D2D
        // handles down-sampling on the GPU (no CPU mip pyramid, no per-pixel cost the
        // caller pays). HIGH_QUALITY_CUBIC over LINEAR is single-digit microseconds for
        // typical SVG-cache-sized bitmaps and removes the aliasing seen on zoom-out /
        // window-shrink where cache pixels > target pixels. Fast keeps NEAREST for the
        // pan-responsiveness opt-out callers use during interaction.
        if (_deviceContext != 0)
        {
            var interpolation = ImageScaleQuality switch
            {
                ImageScaleQuality.Fast => D2D1_INTERPOLATION_MODE.NEAREST_NEIGHBOR,
                _ => D2D1_INTERPOLATION_MODE.HIGH_QUALITY_CUBIC,
            };
            D2D1VTable.DrawBitmap(
                (ID2D1DeviceContext*)_deviceContext,
                bmp, dst, opacity: 1.0f, interpolation, src);
            return;
        }

        // Legacy fallback: HwndRenderTarget without DC QI. Only LINEAR / NEAREST available.
        var legacyInterp = ImageScaleQuality switch
        {
            ImageScaleQuality.Fast => D2D1_BITMAP_INTERPOLATION_MODE.NEAREST_NEIGHBOR,
            _ => D2D1_BITMAP_INTERPOLATION_MODE.LINEAR,
        };
        D2D1VTable.DrawBitmap((ID2D1RenderTarget*)_renderTarget, bmp, dst, opacity: 1.0f, legacyInterp, src);
    }

    /// <summary>
    /// Creates a D2D gradient brush and its stop collection. Caller must release both handles.
    /// Returns 0 if creation fails.
    /// </summary>
    private nint CreateGradientBrush(IGradientBrush brush, Rect objectBounds, out nint stopCollection)
    {
        stopCollection = 0;
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0)
        {
            return 0;
        }

        int stopCount = stops.Count;
        Span<D2D1_GRADIENT_STOP> d2dStops = stopCount <= 8
            ? stackalloc D2D1_GRADIENT_STOP[8]
            : new D2D1_GRADIENT_STOP[stopCount];
        d2dStops = d2dStops[..stopCount];
        for (int i = 0; i < stopCount; i++)
        {
            var s = stops[i];
            d2dStops[i] = new D2D1_GRADIENT_STOP((float)Math.Clamp(s.Offset, 0.0, 1.0), ToColorF(s.Color));
        }

        var extendMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => D2D1_EXTEND_MODE.MIRROR,
            SpreadMethod.Repeat => D2D1_EXTEND_MODE.WRAP,
            _ => D2D1_EXTEND_MODE.CLAMP
        };

        int hr = D2D1VTable.CreateGradientStopCollection(
            (ID2D1RenderTarget*)_renderTarget,
            d2dStops, D2D1_GAMMA.GAMMA_2_2, extendMode, out stopCollection);
        if (hr < 0 || stopCollection == 0)
        {
            return 0;
        }

        var gt = brush.GradientTransform;
        var bProps = new D2D1_BRUSH_PROPERTIES(
            _globalAlpha,
            gt.HasValue ? ToMatrix3x2F(gt.Value) : D2D1_MATRIX_3X2_F.Identity);

        nint gradBrush = 0;

        if (brush is ILinearGradientBrush linear)
        {
            var start = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
            var end = ResolveGradientPoint(linear.EndPoint, brush.GradientUnits, objectBounds);
            var linProps = new D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES(
                new D2D1_POINT_2F((float)start.X, (float)start.Y),
                new D2D1_POINT_2F((float)end.X, (float)end.Y));
            D2D1VTable.CreateLinearGradientBrush(
                (ID2D1RenderTarget*)_renderTarget, linProps, bProps, stopCollection, out gradBrush);
        }
        else if (brush is IRadialGradientBrush radial)
        {
            var center = ResolveGradientPoint(radial.Center, brush.GradientUnits, objectBounds);
            var origin = ResolveGradientPoint(radial.GradientOrigin, brush.GradientUnits, objectBounds);
            double rx = brush.GradientUnits == GradientUnits.ObjectBoundingBox
                ? radial.RadiusX * objectBounds.Width : radial.RadiusX;
            double ry = brush.GradientUnits == GradientUnits.ObjectBoundingBox
                ? radial.RadiusY * objectBounds.Height : radial.RadiusY;
            var radProps = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES(
                new D2D1_POINT_2F((float)center.X, (float)center.Y),
                new D2D1_POINT_2F((float)(origin.X - center.X), (float)(origin.Y - center.Y)),
                (float)rx, (float)ry);
            D2D1VTable.CreateRadialGradientBrush(
                (ID2D1RenderTarget*)_renderTarget, radProps, bProps, stopCollection, out gradBrush);
        }

        if (gradBrush == 0)
        {
            ComHelpers.Release(stopCollection);
            stopCollection = 0;
        }

        return gradBrush;
    }

    /// <summary>
    /// Creates a native ID2D1BitmapBrush for an <see cref="IImageBrush"/>.
    /// Returns 0 if the image is not a Direct2DImage or the bitmap could not be uploaded.
    /// Caller owns the returned brush handle and must Release it.
    /// </summary>
    private nint CreateImageBrushHandle(IImageBrush imageBrush)
    {
        nint bitmap = imageBrush.Image switch
        {
            Direct2DImage d2dImage => d2dImage.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration),
            Direct2DNativeBitmapImage nativeBitmapImage => nativeBitmapImage.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration),
            Direct2DDxgiSurfaceImage dxgiSurfaceImage => dxgiSurfaceImage.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration),
            _ => 0,
        };

        if (bitmap == 0)
        {
            return 0;
        }

        var extendX = imageBrush.TileMode is TileMode.Tile or TileMode.TileX
            ? D2D1_EXTEND_MODE.WRAP : D2D1_EXTEND_MODE.CLAMP;
        var extendY = imageBrush.TileMode is TileMode.Tile or TileMode.TileY
            ? D2D1_EXTEND_MODE.WRAP : D2D1_EXTEND_MODE.CLAMP;

        // D2D BitmapBrush samples at texel coordinates matching the bitmap's DIP size.
        // The brush transform maps sampling space → geometry space. To place one tile at
        // DestinationRect with SourceRect of the bitmap as the tile source, compose:
        //   translate(-src.XY) -> scale(dest/src) -> translate(dest.XY) -> userTransform
        var src = imageBrush.SourceRect;
        var dst = imageBrush.DestinationRect;
        float scaleX = src.Width > 0 ? (float)(dst.Width / src.Width) : 1f;
        float scaleY = src.Height > 0 ? (float)(dst.Height / src.Height) : 1f;

        var transform =
            System.Numerics.Matrix3x2.CreateTranslation(-(float)src.X, -(float)src.Y) *
            System.Numerics.Matrix3x2.CreateScale(scaleX, scaleY) *
            System.Numerics.Matrix3x2.CreateTranslation((float)dst.X, (float)dst.Y);

        if (imageBrush.Transform.HasValue)
        {
            transform *= imageBrush.Transform.Value;
        }

        // Seam avoidance: when the brush transform is purely axis-aligned (scale+translate, no
        // rotation/shear), pixel-snap the translation to destination-pixel grid. Fractional
        // tile origins push every tile boundary onto sub-pixel positions where LINEAR + WRAP
        // sampling blends the first and last texel of the tile, producing visible seams at
        // every boundary. Snapping shifts the whole pattern by <1 DIP (imperceptible) but
        // keeps tile boundaries on integer pixel lines, eliminating the blend artifact.
        bool axisAligned = transform.M12 == 0f && transform.M21 == 0f;
        if (axisAligned)
        {
            transform.M31 = MathF.Round(transform.M31);
            transform.M32 = MathF.Round(transform.M32);
        }

        var bmpProps = new D2D1_BITMAP_BRUSH_PROPERTIES(
            extendX, extendY,
            ImageScaleQuality == ImageScaleQuality.Fast
                ? D2D1_BITMAP_INTERPOLATION_MODE.NEAREST_NEIGHBOR
                : D2D1_BITMAP_INTERPOLATION_MODE.LINEAR);

        float opacity = _globalAlpha * (float)imageBrush.Opacity;
        var brushProps = new D2D1_BRUSH_PROPERTIES(opacity, ToMatrix3x2F(transform));

        int hr = D2D1VTable.CreateBitmapBrush(
            (ID2D1RenderTarget*)_renderTarget, bitmap, bmpProps, brushProps, out nint bitmapBrush);
        return hr >= 0 ? bitmapBrush : 0;
    }

    private nint GetSolidBrush(Color color)
    {
        // Apply global alpha multiplier before looking up or creating the brush.
        if (_globalAlpha < 1f)
        {
            color = Color.FromArgb((byte)(int)(color.A * _globalAlpha), color.R, color.G, color.B);
        }

        uint key = color.ToArgb();
        if (_solidBrushes.TryGetValue(key, out var brush) && brush != 0)
        {
            return brush;
        }

        int hr = D2D1VTable.CreateSolidColorBrush((ID2D1RenderTarget*)_renderTarget, ToColorF(color), out brush);
        if (hr < 0 || brush == 0)
        {
            return 0;
        }

        _solidBrushes[key] = brush;
        return brush;
    }

    /// <summary>
    /// Snaps a logical (DIP) stroke width to whole device pixels at the current effective
    /// scale (user transform × DPI), and returns the snapped value back in DIP. The default
    /// stroke style uses <see cref="D2D1_STROKE_TRANSFORM_TYPE.NORMAL"/>, so D2D will multiply
    /// the returned DIP by the same effective scale to land on the snapped device-pixel count.
    /// Result: stroke scales with transform like Skia/WPF/SVG while staying crisp on
    /// fractional DPI/zoom.
    /// </summary>
    private float QuantizeStrokeDip(float thickness)
    {
        if (thickness <= 0)
        {
            return 0;
        }

        float sx = MathF.Sqrt(_transform.M11 * _transform.M11 + _transform.M12 * _transform.M12);
        float sy = MathF.Sqrt(_transform.M21 * _transform.M21 + _transform.M22 * _transform.M22);
        float avgScale = (sx + sy) * 0.5f;
        float totalScale = avgScale * (float)DpiScale;
        if (totalScale < 0.001f)
        {
            return thickness;
        }
        float snappedPx = Math.Max(1, MathF.Round(thickness * totalScale, MidpointRounding.AwayFromZero));
        return snappedPx / totalScale;
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of <paramref name="rect"/> after applying
    /// <see cref="_transform"/>. Used for conservative world-space culling tracking.
    /// </summary>
    private Rect TransformRect(Rect rect)
    {
        var tl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Y), _transform);
        var tr = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Y), _transform);
        var bl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Bottom), _transform);
        var br = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), _transform);
        float minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        float minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        float maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        float maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private void PopClip()
    {
        if (_clipStack.Count == 0 || _renderTarget == 0)
        {
            return;
        }

        var entry = _clipStack.Pop();
        if (entry.Kind == ClipKind.AxisAligned)
        {
            D2D1VTable.PopAxisAlignedClip((ID2D1RenderTarget*)_renderTarget);
            return;
        }

        D2D1VTable.PopLayer((ID2D1RenderTarget*)_renderTarget);
        ComHelpers.Release(entry.Geometry);
        ComHelpers.Release(entry.Layer);
    }

    private readonly struct ClipEntry(ClipKind kind, nint layer, nint geometry)
    {
        public ClipKind Kind { get; } = kind;

        public nint Layer { get; } = layer;

        public nint Geometry { get; } = geometry;
    }
}
