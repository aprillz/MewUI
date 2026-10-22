namespace Aprillz.MewUI.Animation;

/// <summary>
/// Standard easing functions mapping normalized time in [0, 1] to progress in [0, 1]; the back and elastic families overshoot that range.
/// </summary>
public static class Easing
{
    /// <summary>
    /// Gets the default easing function used by animations when none is specified.
    /// </summary>
    public static Func<double, double> Default { get; } = EaseOutCubic;

    /// <summary>Linear easing: returns <paramref name="t"/> unchanged.</summary>
    public static double Linear(double t) => t;

    /// <summary>Quadratic ease-in.</summary>
    public static double EaseInQuad(double t) => t * t;

    /// <summary>Quadratic ease-out.</summary>
    public static double EaseOutQuad(double t)
    {
        var u = 1 - t;
        return 1 - u * u;
    }

    /// <summary>Quadratic ease-in-out.</summary>
    public static double EaseInOutQuad(double t) =>
        t < 0.5
            ? 2 * t * t
            : 1 - (-2 * t + 2) * (-2 * t + 2) / 2;

    /// <summary>Cubic ease-in.</summary>
    public static double EaseInCubic(double t) => t * t * t;

    /// <summary>Cubic ease-out, the <see cref="Default"/>.</summary>
    public static double EaseOutCubic(double t)
    {
        var u = 1 - t;
        return 1 - u * u * u;
    }

    /// <summary>Cubic ease-in-out.</summary>
    public static double EaseInOutCubic(double t)
    {
        if (t < 0.5)
        {
            return 4 * t * t * t;
        }

        var u = -2 * t + 2;
        return 1 - u * u * u / 2;
    }

    /// <summary>Quartic ease-in.</summary>
    public static double EaseInQuart(double t) => t * t * t * t;

    /// <summary>Quartic ease-out.</summary>
    public static double EaseOutQuart(double t)
    {
        var u = 1 - t;
        return 1 - u * u * u * u;
    }

    /// <summary>Quartic ease-in-out.</summary>
    public static double EaseInOutQuart(double t)
    {
        if (t < 0.5)
        {
            return 8 * t * t * t * t;
        }

        var u = -2 * t + 2;
        return 1 - u * u * u * u / 2;
    }

    /// <summary>Quintic ease-in.</summary>
    public static double EaseInQuint(double t) => t * t * t * t * t;

    /// <summary>Quintic ease-out.</summary>
    public static double EaseOutQuint(double t)
    {
        var u = 1 - t;
        return 1 - u * u * u * u * u;
    }

    /// <summary>Quintic ease-in-out.</summary>
    public static double EaseInOutQuint(double t)
    {
        if (t < 0.5)
        {
            return 16 * t * t * t * t * t;
        }

        var u = -2 * t + 2;
        return 1 - u * u * u * u * u / 2;
    }

    /// <summary>Exponential ease-in.</summary>
    public static double EaseInExpo(double t) =>
        t <= 0 ? 0 : Math.Pow(2, 10 * t - 10);

    /// <summary>Exponential ease-out.</summary>
    public static double EaseOutExpo(double t) =>
        t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);

    /// <summary>Exponential ease-in-out.</summary>
    public static double EaseInOutExpo(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return t < 0.5
            ? Math.Pow(2, 20 * t - 10) / 2
            : (2 - Math.Pow(2, -20 * t + 10)) / 2;
    }

    private const double BackC1 = 1.70158;
    private const double BackC2 = BackC1 * 1.525;
    private const double BackC3 = BackC1 + 1;

    /// <summary>Back ease-in, dipping below 0 before accelerating.</summary>
    public static double EaseInBack(double t) =>
        BackC3 * t * t * t - BackC1 * t * t;

    /// <summary>Back ease-out, overshooting past 1 before settling.</summary>
    public static double EaseOutBack(double t)
    {
        var u = t - 1;
        return 1 + BackC3 * u * u * u + BackC1 * u * u;
    }

    /// <summary>Back ease-in-out, overshooting at both ends.</summary>
    public static double EaseInOutBack(double t)
    {
        if (t < 0.5)
        {
            var v = 2 * t;
            return v * v * ((BackC2 + 1) * v - BackC2) / 2;
        }
        else
        {
            var v = 2 * t - 2;
            return (v * v * ((BackC2 + 1) * v + BackC2) + 2) / 2;
        }
    }

    /// <summary>Bounce ease-out, settling at 1 after a few bounces.</summary>
    public static double EaseOutBounce(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;

        if (t < 1 / d1)
        {
            return n1 * t * t;
        }

        if (t < 2 / d1)
        {
            t -= 1.5 / d1;
            return n1 * t * t + 0.75;
        }

        if (t < 2.5 / d1)
        {
            t -= 2.25 / d1;
            return n1 * t * t + 0.9375;
        }

        t -= 2.625 / d1;
        return n1 * t * t + 0.984375;
    }

    /// <summary>Bounce ease-in, bouncing away from 0 before arriving.</summary>
    public static double EaseInBounce(double t) =>
        1 - EaseOutBounce(1 - t);

    /// <summary>Bounce ease-in-out.</summary>
    public static double EaseInOutBounce(double t) =>
        t < 0.5
            ? (1 - EaseOutBounce(1 - 2 * t)) / 2
            : (1 + EaseOutBounce(2 * t - 1)) / 2;

    private const double ElasticC4 = 2 * Math.PI / 3;
    private const double ElasticC5 = 2 * Math.PI / 4.5;

    /// <summary>Elastic ease-in, oscillating around 0 before arriving.</summary>
    public static double EaseInElastic(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return -Math.Pow(2, 10 * t - 10) * Math.Sin((10 * t - 10.75) * ElasticC4);
    }

    /// <summary>Elastic ease-out, overshooting around 1 before settling.</summary>
    public static double EaseOutElastic(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return Math.Pow(2, -10 * t) * Math.Sin((10 * t - 0.75) * ElasticC4) + 1;
    }

    /// <summary>Elastic ease-in-out, oscillating at both ends.</summary>
    public static double EaseInOutElastic(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return t < 0.5
            ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * ElasticC5)) / 2
            : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * ElasticC5) / 2 + 1;
    }

    /// <summary>
    /// Creates a CSS-style cubic-bezier easing with control points (x1, y1) and (x2, y2) between (0, 0) and (1, 1); x1 and x2 are clamped to [0, 1].
    /// </summary>
    public static Func<double, double> CubicBezier(double x1, double y1, double x2, double y2)
    {
        // Pre-validate
        x1 = Math.Clamp(x1, 0, 1);
        x2 = Math.Clamp(x2, 0, 1);

        // Pre-compute coefficients for the x(t) polynomial: at³ + bt² + ct
        double cx = 3 * x1;
        double bx = 3 * (x2 - x1) - cx;
        double ax = 1 - cx - bx;

        double cy = 3 * y1;
        double by = 3 * (y2 - y1) - cy;
        double ay = 1 - cy - by;

        return x =>
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;

            // Find parameter t for given x using Newton-Raphson
            double t = x; // initial guess
            for (int i = 0; i < 8; i++)
            {
                double currentX = ((ax * t + bx) * t + cx) * t;
                double dx = currentX - x;
                if (Math.Abs(dx) < 1e-7) break;

                double derivative = (3 * ax * t + 2 * bx) * t + cx;
                if (Math.Abs(derivative) < 1e-7) break;

                t -= dx / derivative;
            }

            t = Math.Clamp(t, 0, 1);
            return ((ay * t + by) * t + cy) * t;
        };
    }

    /// <summary>
    /// Creates a spring physics easing function using damped harmonic oscillator.
    /// </summary>
    /// <param name="stiffness">Spring stiffness (k). Higher = faster. Default 100.</param>
    /// <param name="damping">Damping coefficient (c). Higher = less bounce. Default 10.</param>
    /// <param name="mass">Mass. Default 1.</param>
    /// <param name="velocity">Initial velocity. Default 0.</param>
    public static Func<double, double> Spring(
        double stiffness = 100, double damping = 10, double mass = 1, double velocity = 0)
    {
        double omega0 = Math.Sqrt(stiffness / mass);       // natural frequency
        double zeta = damping / (2 * Math.Sqrt(stiffness * mass)); // damping ratio

        if (zeta < 1) // underdamped
        {
            double omegaD = omega0 * Math.Sqrt(1 - zeta * zeta);
            double a = -1.0; // start offset (animating from 0 to 1, displacement = -1)
            double b = (-velocity + zeta * omega0 * a) / omegaD; // adjusted for initial velocity

            return t =>
            {
                if (t <= 0) return 0;
                if (t >= 1) return 1;

                // Scale time: spring settles in ~4/(zeta*omega0) seconds, map [0,1] accordingly
                double st = t * 4.0 / (zeta * omega0);
                double decay = Math.Exp(-zeta * omega0 * st);
                double displacement = decay * (a * Math.Cos(omegaD * st) + b * Math.Sin(omegaD * st));
                return 1 + displacement; // offset so 0→1
            };
        }

        if (zeta > 1) // overdamped
        {
            double s1 = -omega0 * (zeta + Math.Sqrt(zeta * zeta - 1));
            double s2 = -omega0 * (zeta - Math.Sqrt(zeta * zeta - 1));
            double c2 = (-1.0 * s1 + velocity) / (s2 - s1);
            double c1 = -1.0 - c2;

            return t =>
            {
                if (t <= 0) return 0;
                if (t >= 1) return 1;

                double st = t * 4.0 / (zeta * omega0);
                double displacement = c1 * Math.Exp(s1 * st) + c2 * Math.Exp(s2 * st);
                return 1 + displacement;
            };
        }

        // critically damped (zeta == 1)
        {
            double c1d = -1.0;
            double c2d = velocity + omega0;

            return t =>
            {
                if (t <= 0) return 0;
                if (t >= 1) return 1;

                double st = t * 4.0 / omega0;
                double displacement = (c1d + c2d * st) * Math.Exp(-omega0 * st);
                return 1 + displacement;
            };
        }
    }
}
