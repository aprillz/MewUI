using System.Diagnostics;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

/// <summary>
/// A confetti particle adorner control.
/// Renders particles on top of the adorned element via <see cref="AdornerLayer"/>.
/// Port of WpfConfetti by caefale, adapted for MewUI's rendering model.
/// </summary>
public sealed class ConfettiOverlay : FrameworkElement
{
    private enum ParticleShape { Rectangle, Ellipse, Triangle }

    // 128 bytes — doubles first, small fields packed at end. No IsDead (swap-remove instead).
    private struct Particle
    {
        // 8-byte aligned fields (15 doubles = 120B)
        public double X, Y;
        public double BaseX, BaseY;
        public double VX, VY;
        public double Size, Drag;
        public double WobbleAmp, WobblePhase, WobbleFreq;
        public double Age;
        public double Rotation, RotationSpeed;
        public double Gravity;
        // 4-byte fields (4B + 4B = 8B)
        public Color Color;
        public ParticleShape Shape;
        // 1-byte field (1B + 7B padding — but shares 8B block above if Shape < 4B... still 128B total)
        public bool IsWide;
    }

    private struct CannonBatch
    {
        public int Remaining;
        public double MinSpeed, MaxSpeed, Gravity, MinSize, MaxSize, Spread, Rate;
        public Color[]? Colors;
    }

    private static readonly Color[] DefaultColors =
    [
        new Color(255, 255, 107, 107),
        new Color(255, 255, 213, 0),
        new Color(255, 164, 212, 0),
        new Color(255, 62, 223, 211),
        new Color(255, 84, 175, 255),
        new Color(255, 200, 156, 255),
    ];

    private readonly List<Particle> _particles = new();
    private readonly Queue<CannonBatch> _cannonQueue = new();
    private AnimationClock? _clock;
    private long _lastTimestamp;
    private double _cannonAccumulator;

    private bool _isRaining;
    private double _rainAccumulator;
    private double _rainRate = 80;
    private double _rainMinSpeed = 60, _rainMaxSpeed = 120;
    private double _rainMinSize = 2, _rainMaxSize = 5;
    private double _rainGravity = 85;
    private Color[]? _rainColors;

    private static readonly Random Rng = new();
        
    public ConfettiOverlay()
    {
        IsHitTestVisible = false;
    }

    public void Burst(int amount = 75, Point? position = null,
        double minSpeed = 50, double maxSpeed = 300,
        double minSize = 3, double maxSize = 5,
        double minAngle = 0, double maxAngle = 360,
        double gravity = 85, Color[]? colors = null)
    {
        var bounds = Bounds;
        var p = position ?? new Point(bounds.Width / 2, bounds.Height / 2);
        for (int i = 0; i < amount; i++)
            SpawnParticle(p, minAngle, maxAngle, minSpeed, maxSpeed, gravity, minSize, maxSize, 90, colors);
        EnsureTimer();
    }

    public void Cannons(int amount = 500, double rate = 75, double spread = 15,
        double minSpeed = 300, double maxSpeed = 500,
        double minSize = 2, double maxSize = 5,
        double gravity = 120, Color[]? colors = null)
    {
        _cannonQueue.Enqueue(new CannonBatch
        {
            Remaining = amount,
            MinSpeed = minSpeed, MaxSpeed = maxSpeed,
            Gravity = gravity,
            MinSize = minSize, MaxSize = maxSize,
            Spread = spread, Rate = rate,
            Colors = colors
        });
        EnsureTimer();
    }

    public void StartRain(double rate = 80, double minSpeed = 60, double maxSpeed = 120,
        double minSize = 2, double maxSize = 5, double gravity = 85, Color[]? colors = null)
    {
        _isRaining = true;
        _rainRate = rate;
        _rainMinSpeed = minSpeed; _rainMaxSpeed = maxSpeed;
        _rainMinSize = minSize; _rainMaxSize = maxSize;
        _rainGravity = gravity;
        _rainColors = colors;
        EnsureTimer();
    }

    public void StopRain() => _isRaining = false;

    public void StopCannons()
    {
        _cannonQueue.Clear();
        _cannonAccumulator = 0;
    }

    public void Clear()
    {
        _isRaining = false;
        _cannonQueue.Clear();
        _cannonAccumulator = 0;
        _particles.Clear();
        StopTimer();
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        RenderParticles(context);
    }

    private readonly PathGeometry _reusablePath = new();

    private void RenderParticles(IGraphicsContext ctx)
    {
        var span = CollectionsMarshal.AsSpan(_particles);
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly var p = ref span[i];

            double w = p.IsWide ? p.Size * 2 : p.Size / 2;
            double h = p.IsWide ? p.Size / 2 : p.Size * 2;
            double cx = p.X + w / 2;
            double cy = p.Y + h / 2;
            double rad = p.Rotation * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            switch (p.Shape)
            {
                case ParticleShape.Rectangle:
                    _reusablePath.Clear();
                    AppendRotatedRect(_reusablePath, cx, cy, w, h, cos, sin);
                    ctx.FillPath(_reusablePath, p.Color);
                    break;
                case ParticleShape.Ellipse:
                    double r = p.Size / 2;
                    ctx.FillEllipse(new Rect(cx - r, cy - r, r * 2, r * 2), p.Color);
                    break;
                case ParticleShape.Triangle:
                    _reusablePath.Clear();
                    AppendRotatedTriangle(_reusablePath, cx, cy, p.Size, cos, sin);
                    ctx.FillPath(_reusablePath, p.Color);
                    break;
            }
        }
    }

    private void EnsureTimer()
    {
        if (_clock != null) return;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _clock = new AnimationClock(TimeSpan.FromSeconds(1)) { RepeatCount = -1 };
        _clock.TickCallback = OnTick;
        _clock.Start();
    }

    private void StopTimer()
    {
        if (_clock == null) return;
        _clock.TickCallback = null;
        _clock.Stop();
        _clock = null;
    }

    private void OnTick(double _)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        _lastTimestamp = now;
        if (dt <= 0 || dt > 0.5) dt = 0.016;

        var bounds = Bounds;
        double w = bounds.Width, h = bounds.Height;
        if (w <= 0 || h <= 0) return;

        if (_isRaining)
        {
            _rainAccumulator += dt;
            double interval = 1.0 / _rainRate;
            while (_rainAccumulator >= interval)
            {
                SpawnParticle(new Point(Rng.NextDouble() * w, -10),
                    85, 95, _rainMinSpeed, _rainMaxSpeed, _rainGravity,
                    _rainMinSize, _rainMaxSize, 0, _rainColors);
                _rainAccumulator -= interval;
            }
        }

        if (_cannonQueue.Count > 0)
        {
            _cannonAccumulator += dt;
            while (_cannonQueue.Count > 0)
            {
                var batch = _cannonQueue.Peek();
                double interval = 1.0 / batch.Rate;
                if (_cannonAccumulator < interval) break;

                SpawnCannonParticle(new Point(0, h), batch, w, h);
                SpawnCannonParticle(new Point(w, h), batch, w, h);
                _cannonAccumulator -= interval;

                batch.Remaining -= 2;
                if (batch.Remaining <= 0)
                {
                    _cannonQueue.Dequeue();
                    _cannonAccumulator = 0;
                }
            }
        }

        UpdateParticles(dt, h);

        if (_particles.Count == 0 && !_isRaining && _cannonQueue.Count == 0)
            StopTimer();

        InvalidateVisual();
    }

    private void UpdateParticles(double dt, double areaHeight)
    {
        var span = CollectionsMarshal.AsSpan(_particles);
        double killY = areaHeight + 50;
        int alive = span.Length;

        for (int i = 0; i < alive; i++)
        {
            ref var p = ref span[i];
            p.Age += dt;
            p.BaseX += p.VX * dt;
            p.BaseY += p.VY * dt;
            p.VY += p.Gravity * dt;
            double drag = Math.Pow(p.Drag, dt);
            p.VX *= drag;
            p.VY *= drag;
            p.RotationSpeed *= drag;

            double wobbleStrength = Math.Clamp(p.Age * 1.5, 0.0, 1.0);
            double wobbleOffset = Math.Sin(p.Age * p.WobbleFreq + p.WobblePhase) * p.WobbleAmp * wobbleStrength;
            p.X = p.BaseX + wobbleOffset;
            p.Y = p.BaseY;
            p.Rotation += p.RotationSpeed * dt;

            // Swap-remove: move last alive particle here, recheck this index
            if (p.Y > killY)
            {
                alive--;
                if (i < alive)
                {
                    span[i] = span[alive];
                    i--; // re-process swapped particle
                }
            }
        }

        if (alive < _particles.Count)
            _particles.RemoveRange(alive, _particles.Count - alive);
    }

    private void SpawnCannonParticle(Point position, CannonBatch batch, double areaW, double areaH)
    {
        double targetX = areaW / 2 + (Rng.NextDouble() - 0.5) * 80;
        double targetY = areaH * 0.35;
        double dx = targetX - position.X;
        double dy = targetY - position.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 0) { dx /= len; dy /= len; }

        double baseAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double speedScale = areaH / 400.0;

        SpawnParticle(position,
            baseAngle - batch.Spread, baseAngle + batch.Spread,
            batch.MinSpeed * speedScale, batch.MaxSpeed * speedScale,
            batch.Gravity, batch.MinSize, batch.MaxSize, 0, batch.Colors);
    }

    private void SpawnParticle(Point position, double minAngle, double maxAngle,
        double minSpeed, double maxSpeed, double gravity,
        double minSize, double maxSize, int angleAdjust = 0, Color[]? colors = null)
    {
        double angleDeg = minAngle + Rng.NextDouble() * (maxAngle - minAngle) - angleAdjust;
        double angleRad = angleDeg * Math.PI / 180.0;
        double speed = minSpeed + Rng.NextDouble() * (maxSpeed - minSpeed);
        double shapeRoll = Rng.NextDouble();
        var colorList = colors ?? DefaultColors;

        _particles.Add(new Particle
        {
            X = position.X, Y = position.Y,
            BaseX = position.X, BaseY = position.Y,
            VX = Math.Cos(angleRad) * speed,
            VY = Math.Sin(angleRad) * speed,
            Size = minSize + Rng.NextDouble() * (maxSize - minSize),
            Color = colorList[Rng.Next(colorList.Length)],
            Shape = shapeRoll < 0.7 ? ParticleShape.Rectangle
                  : shapeRoll < 0.95 ? ParticleShape.Ellipse
                  : ParticleShape.Triangle,
            Drag = 0.65 + Rng.NextDouble() * 0.3,
            IsWide = Rng.Next(2) == 0,
            WobbleAmp = 2 + Rng.NextDouble() * 6,
            WobbleFreq = 1 + Rng.NextDouble() * 3,
            WobblePhase = Rng.NextDouble() * Math.PI * 2,
            Rotation = Rng.NextDouble() * 360,
            RotationSpeed = (Rng.NextDouble() - 0.5) * 2 * (10 + Rng.NextDouble() * 300),
            Gravity = gravity
        });
    }

    private static void AppendRotatedRect(PathGeometry path, double cx, double cy,
        double w, double h, double cos, double sin)
    {
        double hw = w / 2, hh = h / 2;
        Span<double> lx = stackalloc double[] { -hw, hw, hw, -hw };
        Span<double> ly = stackalloc double[] { -hh, -hh, hh, hh };

        for (int i = 0; i < 4; i++)
        {
            double rx = lx[i] * cos - ly[i] * sin + cx;
            double ry = lx[i] * sin + ly[i] * cos + cy;
            if (i == 0) path.MoveTo(rx, ry);
            else path.LineTo(rx, ry);
        }
        path.Close();
    }

    private static void AppendRotatedTriangle(PathGeometry path, double cx, double cy,
        double size, double cos, double sin)
    {
        Span<double> lx = stackalloc double[] { 0, size, -size };
        Span<double> ly = stackalloc double[] { -size, size, size };

        for (int i = 0; i < 3; i++)
        {
            double rx = lx[i] * cos - ly[i] * sin + cx;
            double ry = lx[i] * sin + ly[i] * cos + cy;
            if (i == 0) path.MoveTo(rx, ry);
            else path.LineTo(rx, ry);
        }
        path.Close();
    }

}
