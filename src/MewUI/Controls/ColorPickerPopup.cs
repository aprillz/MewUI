using System.Globalization;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Internal popup content for <see cref="ColorPicker"/>.
/// Hosts an optional color wheel, optional alpha slider and optional numeric inputs panel.
/// </summary>
internal sealed class ColorPickerPopup : Control, IVisualTreeHost
{
    private readonly State _state = new();
    private ColorPickerKind _kind;
    private bool _showAlpha;

    private Wheel? _wheel;
    private AlphaSlider? _alphaSlider;
    private DockPanel? _wheelGroup;
    private InputsPanel? _inputs;

    internal event Action<Color>? ColorChanged;

    private readonly StackPanel _panel;
    private readonly ScrollViewer _scrollViewer;

    public ColorPickerPopup(ColorPickerKind kind, bool showAlpha)
    {
        _panel = new StackPanel { Spacing = 8 };
        _scrollViewer = new ScrollViewer
        {
            BorderThickness = 0,
            Background = default,
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Disabled,
            Content = _panel,
        };
        _scrollViewer.Parent = this;

        Padding = new Thickness(12);

        _state.Changed += OnStateChanged;

        Configure(kind, showAlpha);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_scrollViewer);

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(Padding);
        _scrollViewer.Measure(inner);
        return _scrollViewer.DesiredSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        _scrollViewer.Arrange(inner);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        _scrollViewer.Render(context);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        const double radius = 4;
        context.FillRoundedRectangle(bounds, radius, radius, Theme.Palette.ButtonFace);
        context.DrawRoundedRectangle(bounds, radius, radius, Theme.Palette.ControlBorder, 1, strokeInset: true);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
            return null;

        var hit = _scrollViewer.HitTest(point);
        if (hit != null) return hit;

        return Bounds.Contains(point) ? this : null;
    }

    public void Configure(ColorPickerKind kind, bool showAlpha)
    {
        bool changed = false;

        bool wantWheel = kind != ColorPickerKind.Panel;
        bool wantInputs = kind != ColorPickerKind.Wheel;
        bool wantAlphaSlider = showAlpha && wantWheel;

        if (wantWheel)
        {
            if (_wheelGroup == null)
            {
                _wheelGroup = new DockPanel { Spacing = 8 };
                _panel.Insert(0, _wheelGroup);
                changed = true;
            }

            if (wantAlphaSlider && _alphaSlider == null)
            {
                _alphaSlider = new AlphaSlider(_state) { Orientation = Orientation.Vertical };
                DockPanel.SetDock(_alphaSlider, Dock.Right);
                _wheelGroup.Add(_alphaSlider);
                changed = true;
            }
            else if (!wantAlphaSlider && _alphaSlider != null)
            {
                _wheelGroup.Remove(_alphaSlider);
                _alphaSlider.Detach();
                _alphaSlider = null;
                changed = true;
            }

            if (_wheel == null)
            {
                _wheel = new Wheel(_state);
                _wheelGroup.Add(_wheel);
                changed = true;
            }
        }
        else
        {
            if (_alphaSlider != null)
            {
                _wheelGroup?.Remove(_alphaSlider);
                _alphaSlider.Detach();
                _alphaSlider = null;
                changed = true;
            }
            if (_wheel != null)
            {
                _wheelGroup?.Remove(_wheel);
                _wheel.Detach();
                _wheel = null;
                changed = true;
            }
            if (_wheelGroup != null)
            {
                _panel.Remove(_wheelGroup);
                _wheelGroup = null;
                changed = true;
            }
        }

        if (wantInputs && _inputs == null)
        {
            _inputs = new InputsPanel(_state, showAlpha);
            _panel.Add(_inputs);
            changed = true;
        }
        else if (!wantInputs && _inputs != null)
        {
            _panel.Remove(_inputs);
            _inputs.Detach();
            _inputs = null;
            changed = true;
        }
        else if (_inputs != null && _showAlpha != showAlpha)
        {
            _inputs.SetShowAlpha(showAlpha);
            changed = true;
        }

        _kind = kind;
        _showAlpha = showAlpha;

        if (changed)
        {
            InvalidateMeasure();
        }
    }

    internal void SetColor(Color color) => _state.SetColor(color, this);

    private void OnStateChanged(object? source)
    {
        // Popup.SetColor originates from the owner; don't echo back to it.
        if (ReferenceEquals(source, this)) return;

        var rgb = _state.Rgb;
        var color = _showAlpha ? Color.FromArgb(_state.A, rgb.R, rgb.G, rgb.B) : rgb;
        ColorChanged?.Invoke(color);
    }

    /// <summary>
    /// Shared HSVA state for the pieces of the popup.
    /// Each subscriber receives a <c>source</c> token with every change so it can skip self-echoes.
    /// </summary>
    private sealed class State
    {
        public float H { get; private set; }
        public float S { get; private set; } = 1f;
        public float V { get; private set; } = 1f;
        public byte A { get; private set; } = 255;

        public HsvColor Hsv => new(H, S, V);
        public Color Rgb => Hsv.ToRgb();

        public event Action<object?>? Changed;

        public void SetHsv(float h, float s, float v, object? source)
        {
            if (h == H && s == S && v == V) return;
            H = h; S = s; V = v;
            Changed?.Invoke(source);
        }

        public void SetAlpha(byte a, object? source)
        {
            if (a == A) return;
            A = a;
            Changed?.Invoke(source);
        }

        public void SetAll(float h, float s, float v, byte a, object? source)
        {
            if (h == H && s == S && v == V && a == A) return;
            H = h; S = s; V = v; A = a;
            Changed?.Invoke(source);
        }

        public void SetColor(Color color, object? source)
        {
            var hsv = HsvColor.FromColor(color);
            SetAll(hsv.H, hsv.S, hsv.V, color.A, source);
        }
    }

    /// <summary>
    /// Horizontal slider that drives <see cref="State.A"/>.
    /// The track shows a checkerboard background with a gradient overlay
    /// from transparent to the current opaque color.
    /// </summary>
    private sealed class AlphaSlider : FrameworkElement
    {
        private const double TrackThicknessDip = 14;
        private const double ThumbRadius = 6;
        private const double ThumbOutlinePad = 2; // outline rect +1 + 1.5px stroke rounded up
        private const double ContentThickness = 18;
        private const double NaturalLength = 200;

        private readonly State _state;
        private Orientation _orientation = Orientation.Horizontal;
        private bool _isDragging;

        public AlphaSlider(State state)
        {
            _state = state;
            _state.Changed += OnStateChanged;
        }

        /// <summary>
        /// Gets or sets the slider orientation. Vertical sliders map the top of the track to
        /// fully opaque (alpha = 255) and the bottom to fully transparent (alpha = 0).
        /// </summary>
        public Orientation Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation == value) return;
                _orientation = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        internal void Detach() => _state.Changed -= OnStateChanged;

        private void OnStateChanged(object? source) => InvalidateVisual();

        public override bool Focusable => true;

        protected override Size MeasureContent(Size availableSize)
        {
            if (_orientation == Orientation.Horizontal)
            {
                double w = double.IsPositiveInfinity(availableSize.Width)
                    ? NaturalLength
                    : Math.Min(NaturalLength, availableSize.Width);
                return new Size(w, ContentThickness);
            }
            else
            {
                double h = double.IsPositiveInfinity(availableSize.Height)
                    ? NaturalLength
                    : Math.Min(NaturalLength, availableSize.Height);
                return new Size(ContentThickness, h);
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            base.OnRender(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            double trackPad = ThumbRadius + ThumbOutlinePad;
            Rect trackRect;
            Point gradStart, gradEnd;
            if (_orientation == Orientation.Horizontal)
            {
                double trackY = bounds.Y + (bounds.Height - TrackThicknessDip) / 2;
                double trackX = bounds.X + trackPad;
                double trackW = Math.Max(0, bounds.Width - trackPad * 2);
                trackRect = new Rect(trackX, trackY, trackW, TrackThicknessDip);
                // transparent (t=0) on the left, opaque (t=1) on the right.
                gradStart = new Point(trackRect.X, trackRect.Y);
                gradEnd = new Point(trackRect.Right, trackRect.Y);
            }
            else
            {
                double trackX = bounds.X + (bounds.Width - TrackThicknessDip) / 2;
                double trackY = bounds.Y + trackPad;
                double trackH = Math.Max(0, bounds.Height - trackPad * 2);
                trackRect = new Rect(trackX, trackY, TrackThicknessDip, trackH);
                // transparent (t=0) at the bottom, opaque (t=1) at the top.
                gradStart = new Point(trackRect.X, trackRect.Bottom);
                gradEnd = new Point(trackRect.X, trackRect.Y);
            }

            double radius = TrackThicknessDip / 2;

            context.Save();
            context.SetClipRoundedRect(trackRect, radius, radius);
            AlphaCheckerboard.Fill(context, trackRect, Theme.IsDark);

            var baseColor = _state.Rgb;
            var opaque = Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B);
            var transparent = Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B);
            var stops = new[] { new GradientStop(0, transparent), new GradientStop(1, opaque) };
            var factory = GetGraphicsFactory();
            using var brush = factory.CreateLinearGradientBrush(gradStart, gradEnd, stops);
            context.FillRectangle(trackRect, brush);
            context.Restore();

            double alphaFraction = _state.A / 255.0;
            double thumbCx, thumbCy;
            if (_orientation == Orientation.Horizontal)
            {
                thumbCx = trackRect.X + trackRect.Width * alphaFraction;
                thumbCy = trackRect.Y + trackRect.Height / 2;
            }
            else
            {
                thumbCx = trackRect.X + trackRect.Width / 2;
                thumbCy = trackRect.Bottom - trackRect.Height * alphaFraction;
            }

            DrawMarker(context, thumbCx, thumbCy, ThumbRadius);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!IsEffectivelyEnabled || e.Button != MouseButton.Left || e.Handled)
                return;

            Focus();
            _isDragging = true;
            BeginCapture();
            UpdateAlphaFromPosition(e.Position);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isDragging || !IsMouseCaptured || !e.LeftButton)
                return;

            UpdateAlphaFromPosition(e.Position);
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButton.Left || !_isDragging) return;
            _isDragging = false;
            EndCapture();
            e.Handled = true;
        }

        private void UpdateAlphaFromPosition(Point pos)
        {
            var bounds = Bounds;
            double trackPad = ThumbRadius + ThumbOutlinePad;
            double alphaFraction;
            if (_orientation == Orientation.Horizontal)
            {
                double trackX = bounds.X + trackPad;
                double trackW = Math.Max(1, bounds.Width - trackPad * 2);
                alphaFraction = Math.Clamp((pos.X - trackX) / trackW, 0, 1);
            }
            else
            {
                double trackY = bounds.Y + trackPad;
                double trackH = Math.Max(1, bounds.Height - trackPad * 2);
                alphaFraction = Math.Clamp(1 - (pos.Y - trackY) / trackH, 0, 1);
            }

            _state.SetAlpha((byte)Math.Round(alphaFraction * 255), this);
        }

        private static void DrawMarker(IGraphicsContext context, double cx, double cy, double r)
        {
            context.DrawEllipse(new Rect(cx - r - 1, cy - r - 1, (r + 1) * 2, (r + 1) * 2),
                Color.FromArgb(180, 0, 0, 0), 1.5);
            context.DrawEllipse(new Rect(cx - r, cy - r, r * 2, r * 2),
                Color.FromRgb(255, 255, 255), 2);
        }

        private void BeginCapture()
        {
            if (FindVisualRoot() is Window window)
                window.CaptureMouse(this);
        }

        private void EndCapture()
        {
            if (FindVisualRoot() is Window window)
                window.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Color wheel with an outer hue ring and an inner SV triangle.
    /// Reads and writes HSV through <see cref="State"/>.
    /// </summary>
    private sealed class Wheel : FrameworkElement
    {
        private const double PaddingDip = 8;
        private const float RingWidthRatio = 0.12f;
        private const double DefaultSizeDip = 200 + PaddingDip * 2;

        private readonly State _state;
        private bool _isDraggingHue;
        private bool _isDraggingTriangle;
        private bool _hueRingDirty = true;
        private bool _triangleDirty = true;
        private float _lastRenderedHue = float.NaN;

        private WriteableBitmap? _bitmap;
        private IImage? _image;

        private float _cx, _cy;
        private float _outerR;
        private float _innerR;
        private float _triR;

        public Wheel(State state)
        {
            _state = state;
            _state.Changed += OnStateChanged;
        }

        internal void Detach() => _state.Changed -= OnStateChanged;

        private void OnStateChanged(object? source)
        {
            if (_state.H != _lastRenderedHue)
            {
                _hueRingDirty = true;
                _triangleDirty = true;
            }
            InvalidateVisual();
        }

        protected override Size MeasureContent(Size availableSize)
            => new(DefaultSizeDip, DefaultSizeDip);

        private void EnsureBitmap()
        {
            var bounds = Bounds;
            double scale = GetDpi() / 96.0;
            int pw = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            int ph = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            if (_bitmap != null && _bitmap.PixelWidth == pw && _bitmap.PixelHeight == ph)
            {
                return;
            }

            _image?.Dispose();
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(pw, ph);
            _image = GetGraphicsFactory().CreateImageView(_bitmap);

            float pad = (float)(PaddingDip * scale);
            float diameter = Math.Min(pw, ph) - pad * 2;
            if (diameter <= 0)
            {
                _outerR = 0;
            }
            else
            {
                _cx = pw * 0.5f;
                _cy = ph * 0.5f;
                _outerR = diameter * 0.5f;
                _innerR = _outerR * (1f - RingWidthRatio);
                _triR = _innerR - 4f * (float)scale;
            }

            _hueRingDirty = true;
            _triangleDirty = true;
        }

        private void UpdatePixelsIfDirty()
        {
            if (_bitmap == null || _outerR <= 0) return;
            if (!_hueRingDirty && !_triangleDirty) return;

            using var wctx = _bitmap.LockForWrite();
            var pixels = wctx.PixelsUInt32;
            int w = wctx.PixelWidth;
            int h = wctx.PixelHeight;

            if (_hueRingDirty)
            {
                wctx.Clear(Color.Transparent);
                GenerateHueRing(pixels, w, h);
                _hueRingDirty = false;
            }
            else
            {
                ClearInnerCircle(pixels, w, h);
            }

            GenerateTriangle(pixels, w, h);
            _triangleDirty = false;
            _lastRenderedHue = _state.H;
        }

        protected override void OnDispose()
        {
            base.OnDispose();
            _image?.Dispose();
            _image = null;
            _bitmap?.Dispose();
            _bitmap = null;
        }

        private void ClearInnerCircle(Span<uint> pixels, int w, int h)
        {
            float r2 = _innerR * _innerR;
            int minY = Math.Max(0, (int)(_cy - _innerR));
            int maxY = Math.Min(h - 1, (int)(_cy + _innerR));
            for (int py = minY; py <= maxY; py++)
            {
                float dy = py - _cy;
                float dx = MathF.Sqrt(Math.Max(0, r2 - dy * dy));
                int minX = Math.Max(0, (int)(_cx - dx));
                int maxX = Math.Min(w - 1, (int)(_cx + dx));
                int offset = py * w;
                for (int px = minX; px <= maxX; px++)
                    pixels[offset + px] = 0;
            }
        }

        private void GenerateHueRing(Span<uint> pixels, int w, int h)
        {
            int minY = Math.Max(0, (int)(_cy - _outerR) - 1);
            int maxY = Math.Min(h - 1, (int)(_cy + _outerR) + 1);

            const float aaThreshold = 1.0f;

            for (int py = minY; py <= maxY; py++)
            {
                float dy = py + 0.5f - _cy;
                int offset = py * w;
                int minX = Math.Max(0, (int)(_cx - _outerR) - 1);
                int maxX = Math.Min(w - 1, (int)(_cx + _outerR) + 1);

                for (int px = minX; px <= maxX; px++)
                {
                    float dx = px + 0.5f - _cx;
                    float dist2 = dx * dx + dy * dy;

                    if (dist2 < (_innerR - aaThreshold) * (_innerR - aaThreshold)
                        || dist2 > (_outerR + aaThreshold) * (_outerR + aaThreshold))
                        continue;

                    float dist = MathF.Sqrt(dist2);

                    float alpha;
                    if (dist > _outerR)
                        alpha = Math.Clamp(_outerR + 1f - dist, 0f, 1f);
                    else if (dist < _innerR)
                        alpha = Math.Clamp(dist - _innerR + 1f, 0f, 1f);
                    else
                        alpha = 1f;

                    if (alpha <= 0f) continue;

                    float angle = MathF.Atan2(dy, dx);
                    if (angle < 0) angle += MathF.PI * 2f;
                    float hue = angle / (MathF.PI * 2f) * 360f;

                    var c = HsvColor.HueToRgb(hue);
                    byte a = (byte)(alpha * 255f);
                    pixels[offset + px] = PackBgra(c.R, c.G, c.B, a);
                }
            }
        }

        private void GenerateTriangle(Span<uint> pixels, int w, int h)
        {
            float hueRad = _state.H / 180f * MathF.PI;
            GetTriangleVertices(hueRad, out float x0, out float y0, out float x1, out float y1, out float x2, out float y2);

            float e0x = x1 - x0, e0y = y1 - y0;
            float e1x = x2 - x1, e1y = y2 - y1;
            float e2x = x0 - x2, e2y = y0 - y2;
            float e0Len = MathF.Sqrt(e0x * e0x + e0y * e0y);
            float e1Len = MathF.Sqrt(e1x * e1x + e1y * e1y);
            float e2Len = MathF.Sqrt(e2x * e2x + e2y * e2y);

            int minX = Math.Max(0, (int)MathF.Floor(Math.Min(x0, Math.Min(x1, x2))) - 2);
            int maxX = Math.Min(w - 1, (int)MathF.Ceiling(Math.Max(x0, Math.Max(x1, x2))) + 2);
            int minY = Math.Max(0, (int)MathF.Floor(Math.Min(y0, Math.Min(y1, y2))) - 2);
            int maxY = Math.Min(h - 1, (int)MathF.Ceiling(Math.Max(y0, Math.Max(y1, y2))) + 2);

            float denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (MathF.Abs(denom) < 1e-6f) return;
            float invDenom = 1f / denom;

            var hueColor = HsvColor.HueToRgb(_state.H);

            for (int py = minY; py <= maxY; py++)
            {
                int offset = py * w;
                float fy = py + 0.5f;
                for (int px = minX; px <= maxX; px++)
                {
                    float fx = px + 0.5f;

                    float bary0 = ((y1 - y2) * (fx - x2) + (x2 - x1) * (fy - y2)) * invDenom;
                    float bary1 = ((y2 - y0) * (fx - x2) + (x0 - x2) * (fy - y2)) * invDenom;

                    float d0 = e0Len > 0 ? ((fx - x0) * e0y - (fy - y0) * e0x) / e0Len : 0;
                    float d1 = e1Len > 0 ? ((fx - x1) * e1y - (fy - y1) * e1x) / e1Len : 0;
                    float d2 = e2Len > 0 ? ((fx - x2) * e2y - (fy - y2) * e2x) / e2Len : 0;

                    if (denom > 0) { d0 = -d0; d1 = -d1; d2 = -d2; }

                    float minDist = Math.Min(d0, Math.Min(d1, d2));
                    if (minDist < -1f) continue;

                    float b0 = Math.Max(0, bary0);
                    float b1 = Math.Max(0, bary1);
                    float b2 = Math.Max(0, 1f - bary0 - bary1);
                    float sum = b0 + b1 + b2;
                    if (sum < 1e-6f) continue;
                    b0 /= sum; b1 /= sum;

                    float r = hueColor.R * b0 + 255f * b1;
                    float g = hueColor.G * b0 + 255f * b1;
                    float b = hueColor.B * b0 + 255f * b1;

                    byte alpha = 255;
                    if (minDist < 1f)
                        alpha = (byte)Math.Clamp((minDist + 1f) * 0.5f * 255f, 0f, 255f);

                    byte rb = (byte)Math.Clamp(r, 0f, 255f);
                    byte gb = (byte)Math.Clamp(g, 0f, 255f);
                    byte bb = (byte)Math.Clamp(b, 0f, 255f);
                    pixels[offset + px] = PackBgra(rb, gb, bb, alpha);
                }
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            base.OnRender(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            EnsureBitmap();
            UpdatePixelsIfDirty();

            if (_image != null && _outerR > 0)
            {
                context.DrawImage(_image, bounds);
            }

            if (_outerR <= 0) return;

            double scale = GetDpi() / 96.0;
            float hueRad = _state.H / 180f * MathF.PI;
            double markerR = 5;

            double ox = bounds.X;
            double oy = bounds.Y;

            float ringMidR = (_outerR + _innerR) * 0.5f;
            double hmx = ox + (_cx + MathF.Cos(hueRad) * ringMidR) / scale;
            double hmy = oy + (_cy + MathF.Sin(hueRad) * ringMidR) / scale;

            DrawMarker(context, hmx, hmy, markerR);

            GetTriangleVertices(hueRad, out float vx0, out float vy0, out float vx1, out float vy1, out float vx2, out float vy2);

            float v = _state.V;
            float s = _state.S;
            float bv0 = v * s;
            float bv1 = v - bv0;
            float bv2 = 1f - v;

            double scx = ox + (bv0 * vx0 + bv1 * vx1 + bv2 * vx2) / scale;
            double scy = oy + (bv0 * vy0 + bv1 * vy1 + bv2 * vy2) / scale;

            DrawMarker(context, scx, scy, markerR);
        }

        private static void DrawMarker(IGraphicsContext context, double cx, double cy, double r)
        {
            context.DrawEllipse(new Rect(cx - r - 1, cy - r - 1, (r + 1) * 2, (r + 1) * 2),
                Color.FromArgb(180, 0, 0, 0), 1.5);
            context.DrawEllipse(new Rect(cx - r, cy - r, r * 2, r * 2),
                Color.FromRgb(255, 255, 255), 2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!IsEffectivelyEnabled || e.Button != MouseButton.Left || e.Handled)
                return;

            Focus();
            var (px, py) = ToPixelPosition(e.Position);

            if (HitTestHueRing(px, py))
            {
                _isDraggingHue = true;
                UpdateHueFromPixel(px, py);
                BeginCapture();
                e.Handled = true;
            }
            else if (HitTestTriangle(px, py))
            {
                _isDraggingTriangle = true;
                UpdateTriangleFromPixel(px, py);
                BeginCapture();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!IsEffectivelyEnabled || !IsMouseCaptured || !e.LeftButton)
                return;

            var (px, py) = ToPixelPosition(e.Position);

            if (_isDraggingHue)
            {
                UpdateHueFromPixel(px, py);
                e.Handled = true;
            }
            else if (_isDraggingTriangle)
            {
                UpdateTriangleFromPixel(px, py);
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButton.Left)
                return;

            if (_isDraggingHue || _isDraggingTriangle)
            {
                _isDraggingHue = false;
                _isDraggingTriangle = false;
                EndCapture();
                e.Handled = true;
            }
        }

        private (float px, float py) ToPixelPosition(Point pos)
        {
            var bounds = Bounds;
            double localX = pos.X - bounds.X;
            double localY = pos.Y - bounds.Y;
            double scale = GetDpi() / 96.0;
            return ((float)(localX * scale), (float)(localY * scale));
        }

        private bool HitTestHueRing(float px, float py)
        {
            float dx = px - _cx;
            float dy = py - _cy;
            float dist2 = dx * dx + dy * dy;
            float tolerance = 4f;
            float inner = _innerR - tolerance;
            float outer = _outerR + tolerance;
            return dist2 >= inner * inner && dist2 <= outer * outer;
        }

        private bool HitTestTriangle(float px, float py)
        {
            float hueRad = _state.H / 180f * MathF.PI;
            GetTriangleVertices(hueRad, out float x0, out float y0, out float x1, out float y1, out float x2, out float y2);

            float denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (MathF.Abs(denom) < 1e-6f) return false;

            float b0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / denom;
            float b1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / denom;
            float b2 = 1f - b0 - b1;

            return b0 >= -0.05f && b1 >= -0.05f && b2 >= -0.05f;
        }

        private void UpdateHueFromPixel(float px, float py)
        {
            float dx = px - _cx;
            float dy = py - _cy;
            float angle = MathF.Atan2(dy, dx);
            if (angle < 0) angle += MathF.PI * 2f;
            float h = angle / (MathF.PI * 2f) * 360f;

            _state.SetHsv(h, _state.S, _state.V, this);
        }

        private void UpdateTriangleFromPixel(float px, float py)
        {
            float hueRad = _state.H / 180f * MathF.PI;
            GetTriangleVertices(hueRad, out float x0, out float y0, out float x1, out float y1, out float x2, out float y2);

            float denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (MathF.Abs(denom) < 1e-6f) return;

            float b0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / denom;
            float b1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / denom;
            float b2 = 1f - b0 - b1;

            b0 = Math.Max(0, b0);
            b1 = Math.Max(0, b1);
            b2 = Math.Max(0, b2);
            float sum = b0 + b1 + b2;
            if (sum < 1e-6f) return;
            b0 /= sum; b1 /= sum;

            float v = b0 + b1;
            float s = v > 1e-6f ? b0 / v : 0f;

            _state.SetHsv(_state.H, Math.Clamp(s, 0f, 1f), Math.Clamp(v, 0f, 1f), this);
        }

        private void GetTriangleVertices(float hueRad,
            out float x0, out float y0,
            out float x1, out float y1,
            out float x2, out float y2)
        {
            x0 = _cx + MathF.Cos(hueRad) * _triR;
            y0 = _cy + MathF.Sin(hueRad) * _triR;
            float a1 = hueRad + MathF.PI * 2f / 3f;
            x1 = _cx + MathF.Cos(a1) * _triR;
            y1 = _cy + MathF.Sin(a1) * _triR;
            float a2 = hueRad + MathF.PI * 4f / 3f;
            x2 = _cx + MathF.Cos(a2) * _triR;
            y2 = _cy + MathF.Sin(a2) * _triR;
        }

        private void BeginCapture()
        {
            if (FindVisualRoot() is Window window)
                window.CaptureMouse(this);
        }

        private void EndCapture()
        {
            if (FindVisualRoot() is Window window)
                window.ReleaseMouseCapture();
        }

        private static uint PackBgra(byte r, byte g, byte b, byte a)
            => (uint)(b | (g << 8) | (r << 16) | (a << 24));
    }

    /// <summary>
    /// Numeric editing panel for the <see cref="ColorPicker"/> popup.
    /// Reads and writes HSVA through <see cref="State"/>.
    /// </summary>
    private sealed class InputsPanel : UserControl
    {
        private const double GridSpacing = 6;

        private readonly State _state;

        private readonly TextBox _hexBox;
        private readonly Label _hexLabel;
        private readonly Label[] _rgbLabels;
        private readonly Label[] _hsvLabels;
        private readonly Label _alphaLabel;
        private readonly NumericUpDown _r, _g, _b, _a;
        private readonly NumericUpDown _h, _s, _v;

        // _suppress guards input-widget ValueChanged re-entry while we programmatically sync.
        // Needed because NumericUpDown rounds to int, so a round-trip can land on a slightly
        // different display value even though state itself is unchanged.
        private bool _suppress;
        private bool _showAlpha;

        public InputsPanel(State state, bool showAlpha)
        {
            _state = state;
            _showAlpha = showAlpha;

            _hexLabel = MakeLabel(MewUIStrings.ColorPickerHexLabel.Value);
            _hexBox = new TextBox
            {
                ImeMode = ImeMode.Disabled,
            };
            _hexBox.TextChanged += OnHexChanged;
            _hexBox.LostFocus += SyncHexFromState;

            _rgbLabels = new[] { MakeLabel("R"), MakeLabel("G"), MakeLabel("B") };
            _hsvLabels = new[] { MakeLabel("H"), MakeLabel("S"), MakeLabel("V") };
            _alphaLabel = MakeLabel("A");

            _r = MakeChannel(0, 255);
            _g = MakeChannel(0, 255);
            _b = MakeChannel(0, 255);
            _a = MakeChannel(0, 255);
            _h = MakeChannel(0, 360);
            _s = MakeChannel(0, 100);
            _v = MakeChannel(0, 100);

            _r.ValueChanged += _ => OnRgbInputChanged();
            _g.ValueChanged += _ => OnRgbInputChanged();
            _b.ValueChanged += _ => OnRgbInputChanged();
            _a.ValueChanged += _ => OnAlphaInputChanged();
            _h.ValueChanged += _ => OnHsvInputChanged();
            _s.ValueChanged += _ => OnHsvInputChanged();
            _v.ValueChanged += _ => OnHsvInputChanged();

            Build();
            SyncAllInputsFromState();

            _state.Changed += OnStateChanged;
        }

        internal void Detach() => _state.Changed -= OnStateChanged;

        private void OnStateChanged(object? source)
        {
            if (ReferenceEquals(source, this)) return;
            SyncAllInputsFromState();
        }

        protected override Element? OnBuild()
        {
            if (Content is Panel previous)
            {
                previous.Clear();
            }

            var grid = new Grid()
                .Columns("Auto,60,Auto,60,Auto,60")
                .Rows("Auto,Auto,Auto")
                .Spacing(GridSpacing)
                .ShareStarSize();


            AddChannelRow(grid, row: 0, _rgbLabels, new[] { _r, _g, _b });
            AddChannelRow(grid, row: 1, _hsvLabels, new[] { _h, _s, _v });

            Place(_hexLabel, row: 2, col: 0);
            Place(_hexBox, row: 2, col: 1, colSpan: 3);
            grid.AddRange(_hexLabel, _hexBox);

            Place(_alphaLabel, row: 2, col: 4);
            Place(_a, row: 2, col: 5);
            grid.AddRange(_alphaLabel, _a);
            _a.IsEnabled = _showAlpha;

            return grid;
        }

        private static void Place(Element element, int row, int col, int colSpan = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            if (colSpan > 1)
            {
                Grid.SetColumnSpan(element, colSpan);
            }
        }

        private static void AddChannelRow(Grid grid, int row, Label[] labels, NumericUpDown[] inputs)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                Place(labels[i], row, col: i * 2);
                Place(inputs[i], row, col: i * 2 + 1);
                grid.AddRange(labels[i], inputs[i]);
            }
        }

        private static Label MakeLabel(string text) => new()
        {
            Text = text,
            TextAlignment = TextAlignment.Right,
            VerticalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 0, 4, 0),
        };

        private static NumericUpDown MakeChannel(double min, double max) => new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Step = 1,
            Format = "0",
        }.IsInteger();

        public void SetShowAlpha(bool showAlpha)
        {
            if (_showAlpha == showAlpha) return;
            _showAlpha = showAlpha;
            Build();
            SyncHexFromState();
        }

        private void SyncAllInputsFromState()
        {
            _suppress = true;
            try
            {
                var rgb = _state.Rgb;
                _r.Value = rgb.R;
                _g.Value = rgb.G;
                _b.Value = rgb.B;
                _a.Value = _state.A;
                _h.Value = Math.Round(_state.H);
                _s.Value = Math.Round(_state.S * 100);
                _v.Value = Math.Round(_state.V * 100);
                _hexBox.Text = FormatHex(rgb, _state.A, _showAlpha);
            }
            finally { _suppress = false; }
        }

        private void SyncHexFromState()
        {
            _suppress = true;
            try
            {
                _hexBox.Text = FormatHex(_state.Rgb, _state.A, _showAlpha);
            }
            finally { _suppress = false; }
        }

        private void OnRgbInputChanged()
        {
            if (_suppress) return;

            var rgb = Color.FromArgb(_state.A, (byte)_r.Value, (byte)_g.Value, (byte)_b.Value);
            var hsv = HsvColor.FromColor(rgb);
            // Preserve hue for achromatic inputs so sliding RGB through gray doesn't snap hue to 0.
            float h = (rgb.R == rgb.G && rgb.G == rgb.B) ? _state.H : hsv.H;

            _state.SetHsv(h, hsv.S, hsv.V, this);
            SyncHsvInputsAndHex();
        }

        private void OnHsvInputChanged()
        {
            if (_suppress) return;

            float h = (float)_h.Value;
            float s = (float)(_s.Value / 100.0);
            float v = (float)(_v.Value / 100.0);
            _state.SetHsv(h, s, v, this);
            SyncRgbInputsAndHex();
        }

        private void OnAlphaInputChanged()
        {
            if (_suppress) return;

            _state.SetAlpha((byte)_a.Value, this);
            SyncHexFromState();
        }

        private void SyncHsvInputsAndHex()
        {
            _suppress = true;
            try
            {
                _h.Value = Math.Round(_state.H);
                _s.Value = Math.Round(_state.S * 100);
                _v.Value = Math.Round(_state.V * 100);
                _hexBox.Text = FormatHex(_state.Rgb, _state.A, _showAlpha);
            }
            finally { _suppress = false; }
        }

        private void SyncRgbInputsAndHex()
        {
            _suppress = true;
            try
            {
                var rgb = _state.Rgb;
                _r.Value = rgb.R;
                _g.Value = rgb.G;
                _b.Value = rgb.B;
                _hexBox.Text = FormatHex(rgb, _state.A, _showAlpha);
            }
            finally { _suppress = false; }
        }

        private void OnHexChanged(string text)
        {
            if (_suppress) return;

            if (!TryParseHex(text, _showAlpha, out byte hr, out byte hg, out byte hb, out byte ha, out bool hasAlpha))
                return;

            var rgb = Color.FromRgb(hr, hg, hb);
            var hsv = HsvColor.FromColor(rgb);
            float h = (rgb.R == rgb.G && rgb.G == rgb.B) ? _state.H : hsv.H;
            byte a = (_showAlpha && hasAlpha) ? ha : _state.A;

            _state.SetAll(h, hsv.S, hsv.V, a, this);

            _suppress = true;
            try
            {
                _r.Value = hr; _g.Value = hg; _b.Value = hb;
                _h.Value = Math.Round(_state.H);
                _s.Value = Math.Round(_state.S * 100);
                _v.Value = Math.Round(_state.V * 100);
                _a.Value = _state.A;
            }
            finally { _suppress = false; }
        }

        private static string FormatHex(Color rgb, byte alpha, bool showAlpha)
        {
            if (showAlpha && alpha < 255)
            {
                return $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
            }

            return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
        }

        private static bool TryParseHex(string? text, bool allowAlpha,
            out byte r, out byte g, out byte b, out byte a, out bool hasAlpha)
        {
            r = g = b = 0;
            a = 255;
            hasAlpha = false;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var s = text.Trim();
            if (s.StartsWith('#'))
            {
                s = s[1..];
            }

            if (s.Length == 0)
            {
                return false;
            }

            switch (s.Length)
            {
                case 3:
                    if (!TryHex(s[0], out int r3) || !TryHex(s[1], out int g3) || !TryHex(s[2], out int b3))
                    {
                        return false;
                    }

                    r = (byte)((r3 << 4) | r3);
                    g = (byte)((g3 << 4) | g3);
                    b = (byte)((b3 << 4) | b3);
                    return true;
                case 6:
                    return byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                        && byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                        && byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
                case 8 when allowAlpha:
                    if (!byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)
                        || !byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                        || !byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                        || !byte.TryParse(s.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    {
                        return false;
                    }

                    hasAlpha = true;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryHex(char c, out int value)
        {
            if (c >= '0' && c <= '9') { value = c - '0'; return true; }
            if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
            if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
            value = 0;
            return false;
        }
    }

}
