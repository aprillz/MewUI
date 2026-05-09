using System.Diagnostics;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Video.Sample.Controls;

public sealed class ConfettiOverlay : FrameworkElement
{
    private enum ParticleShape { Rectangle, Ellipse, Triangle }

    private struct CannonSettings
    {
        public double Rate;
        public double Spread;
        public double MinSpeed;
        public double MaxSpeed;
        public double MinSize;
        public double MaxSize;
        public double Gravity;
        public Color[]? Colors;
    }

    private struct Particle
    {
        public double X, Y;
        public double BaseX, BaseY;
        public double VX, VY;
        public double Size, Drag;
        public double WobbleAmp, WobblePhase, WobbleFreq;
        public double Age;
        public double Rotation, RotationSpeed;
        public double Gravity;
        public Color Color;
        public ParticleShape Shape;
        public bool IsWide;
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

    private static readonly Random Rng = new();

    private readonly List<Particle> _particles = [];
    private readonly PathGeometry _reusablePath = new();
    private AnimationClock? _clock;
    private long _lastTimestamp;
    private bool _isCannonsActive;
    private double _cannonAccumulator;
    private CannonSettings _cannonSettings;

    public bool IsCannonsActive => _isCannonsActive;

    public ConfettiOverlay()
    {
        IsHitTestVisible = false;
        _cannonSettings = new CannonSettings
        {
            Rate = 75,
            Spread = 15,
            MinSpeed = 300,
            MaxSpeed = 500,
            MinSize = 2,
            MaxSize = 5,
            Gravity = 120,
            Colors = null,
        };
    }

    public void Burst(int amount = 75, Point? position = null,
        double minSpeed = 50, double maxSpeed = 300,
        double minSize = 3, double maxSize = 10,
        double minAngle = 0, double maxAngle = 360,
        double gravity = 85, Color[]? colors = null)
    {
        var bounds = Bounds;
        var origin = position ?? new Point(bounds.Width / 2, bounds.Height / 2);
        for (int i = 0; i < amount; i++)
        {
            SpawnParticle(origin, minAngle, maxAngle, minSpeed, maxSpeed, gravity, minSize, maxSize, 90, colors);
        }

        EnsureTimer();
    }

    public void Clear()
    {
        StopCannons();
        _particles.Clear();
        StopTimer();
        InvalidateVisual();
    }

    public void StartCannons(double rate = 125, double spread = 15,
        double minSpeed = 300, double maxSpeed = 500,
        double minSize = 2, double maxSize = 10,
        double gravity = 120, Color[]? colors = null)
    {
        _cannonSettings = new CannonSettings
        {
            Rate = rate,
            Spread = spread,
            MinSpeed = minSpeed,
            MaxSpeed = maxSpeed,
            MinSize = minSize,
            MaxSize = maxSize,
            Gravity = gravity,
            Colors = colors,
        };
        _cannonAccumulator = 0;
        _isCannonsActive = true;
        EnsureTimer();
    }

    public void StopCannons()
    {
        _isCannonsActive = false;
        _cannonAccumulator = 0;

        if (_particles.Count == 0)
        {
            StopTimer();
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var span = CollectionsMarshal.AsSpan(_particles);
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly var particle = ref span[i];

            double w = particle.IsWide ? particle.Size * 2 : particle.Size / 2;
            double h = particle.IsWide ? particle.Size / 2 : particle.Size * 2;
            double cx = particle.X + w / 2;
            double cy = particle.Y + h / 2;
            double rad = particle.Rotation * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            switch (particle.Shape)
            {
                case ParticleShape.Rectangle:
                    _reusablePath.Clear();
                    AppendRotatedRect(_reusablePath, cx, cy, w, h, cos, sin);
                    context.FillPath(_reusablePath, particle.Color);
                    break;
                case ParticleShape.Ellipse:
                    double radius = particle.Size / 2;
                    context.FillEllipse(new Rect(cx - radius, cy - radius, radius * 2, radius * 2), particle.Color);
                    break;
                case ParticleShape.Triangle:
                    _reusablePath.Clear();
                    AppendRotatedTriangle(_reusablePath, cx, cy, particle.Size, cos, sin);
                    context.FillPath(_reusablePath, particle.Color);
                    break;
            }
        }
    }

    private void EnsureTimer()
    {
        if (_clock != null)
        {
            return;
        }

        _lastTimestamp = Stopwatch.GetTimestamp();
        _clock = new AnimationClock(TimeSpan.FromSeconds(1)) { RepeatCount = -1 };
        _clock.TickCallback = OnTick;
        _clock.Start();
    }

    private void StopTimer()
    {
        if (_clock == null)
        {
            return;
        }

        _clock.TickCallback = null;
        _clock.Stop();
        _clock = null;
    }

    private void OnTick(double _)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        _lastTimestamp = now;
        if (dt <= 0 || dt > 0.5)
        {
            dt = 0.016;
        }

        var bounds = Bounds;
        double areaWidth = bounds.Width;
        double areaHeight = bounds.Height;
        if (areaWidth <= 0 || areaHeight <= 0)
        {
            return;
        }

        if (_isCannonsActive)
        {
            _cannonAccumulator += dt;
            double interval = 1.0 / _cannonSettings.Rate;
            while (_cannonAccumulator >= interval)
            {
                SpawnCannonParticle(new Point(0, areaHeight), areaWidth, areaHeight, leftSide: true);
                SpawnCannonParticle(new Point(areaWidth, areaHeight), areaWidth, areaHeight, leftSide: false);
                _cannonAccumulator -= interval;
            }
        }

        UpdateParticles(dt, areaHeight);
        if (_particles.Count == 0 && !_isCannonsActive)
        {
            StopTimer();
        }

        InvalidateVisual();
    }

    private void SpawnCannonParticle(Point position, double areaWidth, double areaHeight, bool leftSide)
    {
        double targetX = areaWidth / 2 + (Rng.NextDouble() - 0.5) * 80;
        double targetY = areaHeight * 0.35;
        double dx = targetX - position.X;
        double dy = targetY - position.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length > 0)
        {
            dx /= length;
            dy /= length;
        }

        double baseAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (!leftSide)
        {
            baseAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        }

        double speedScale = areaHeight / 400.0;
        SpawnParticle(
            position,
            baseAngle - _cannonSettings.Spread,
            baseAngle + _cannonSettings.Spread,
            _cannonSettings.MinSpeed * speedScale,
            _cannonSettings.MaxSpeed * speedScale,
            _cannonSettings.Gravity,
            _cannonSettings.MinSize,
            _cannonSettings.MaxSize,
            0,
            _cannonSettings.Colors);
    }

    private void UpdateParticles(double dt, double areaHeight)
    {
        var span = CollectionsMarshal.AsSpan(_particles);
        double killY = areaHeight + 50;
        int alive = span.Length;

        for (int i = 0; i < alive; i++)
        {
            ref var particle = ref span[i];
            particle.Age += dt;
            particle.BaseX += particle.VX * dt;
            particle.BaseY += particle.VY * dt;
            particle.VY += particle.Gravity * dt;
            double drag = Math.Pow(particle.Drag, dt);
            particle.VX *= drag;
            particle.VY *= drag;
            particle.RotationSpeed *= drag;

            double wobbleStrength = Math.Clamp(particle.Age * 1.5, 0.0, 1.0);
            double wobbleOffset = Math.Sin(particle.Age * particle.WobbleFreq + particle.WobblePhase) * particle.WobbleAmp * wobbleStrength;
            particle.X = particle.BaseX + wobbleOffset;
            particle.Y = particle.BaseY;
            particle.Rotation += particle.RotationSpeed * dt;

            if (particle.Y > killY)
            {
                alive--;
                if (i < alive)
                {
                    span[i] = span[alive];
                    i--;
                }
            }
        }

        if (alive < _particles.Count)
        {
            _particles.RemoveRange(alive, _particles.Count - alive);
        }
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
            X = position.X,
            Y = position.Y,
            BaseX = position.X,
            BaseY = position.Y,
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
            Gravity = gravity,
        });
    }

    private static void AppendRotatedRect(PathGeometry path, double cx, double cy, double w, double h, double cos, double sin)
    {
        double hw = w / 2;
        double hh = h / 2;
        Span<double> lx = stackalloc double[] { -hw, hw, hw, -hw };
        Span<double> ly = stackalloc double[] { -hh, -hh, hh, hh };

        for (int i = 0; i < 4; i++)
        {
            double rx = lx[i] * cos - ly[i] * sin + cx;
            double ry = lx[i] * sin + ly[i] * cos + cy;
            if (i == 0)
            {
                path.MoveTo(rx, ry);
            }
            else
            {
                path.LineTo(rx, ry);
            }
        }

        path.Close();
    }

    private static void AppendRotatedTriangle(PathGeometry path, double cx, double cy, double size, double cos, double sin)
    {
        Span<double> lx = stackalloc double[] { 0, size, -size };
        Span<double> ly = stackalloc double[] { -size, size, size };

        for (int i = 0; i < 3; i++)
        {
            double rx = lx[i] * cos - ly[i] * sin + cx;
            double ry = lx[i] * sin + ly[i] * cos + cy;
            if (i == 0)
            {
                path.MoveTo(rx, ry);
            }
            else
            {
                path.LineTo(rx, ry);
            }
        }

        path.Close();
    }
}
