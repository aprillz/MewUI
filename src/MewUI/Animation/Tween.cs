namespace Aprillz.MewUI.Animation;

/// <summary>
/// Interpolation function that blends between two values.
/// </summary>
public delegate T LerpFunc<T>(T from, T to, double t);

/// <summary>
/// Interpolates a value between <see cref="From"/> and <see cref="To"/>
/// driven by an <see cref="AnimationClock"/>.
/// </summary>
public sealed class Tween<T>
{
    private readonly LerpFunc<T> _lerp;
    private AnimationClock? _clock;
    private T _currentValue;

    public Tween(T from, T to, LerpFunc<T> lerp)
    {
        ArgumentNullException.ThrowIfNull(lerp);
        From = from;
        To = to;
        _lerp = lerp;
        _currentValue = from;
    }

    /// <summary>
    /// Gets or sets the start value.
    /// </summary>
    public T From { get; set; }

    /// <summary>
    /// Gets or sets the end value.
    /// </summary>
    public T To { get; set; }

    /// <summary>
    /// Gets the current interpolated value.
    /// </summary>
    public T CurrentValue => _currentValue;

    /// <summary>
    /// Raised when the interpolated value changes.
    /// </summary>
    public event Action<T>? ValueChanged;

    /// <summary>
    /// Binds this tween to an animation clock. The tween will update
    /// its value on each clock tick.
    /// </summary>
    public Tween<T> Bind(AnimationClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Unbind();
        _clock = clock;
        _clock.TickCallback = OnClockTick;
        return this;
    }

    /// <summary>
    /// Unbinds from the currently connected clock.
    /// </summary>
    public void Unbind()
    {
        if (_clock == null)
        {
            return;
        }

        _clock.TickCallback = null;
        _clock = null;
    }

    private void OnClockTick(double progress)
    {
        _currentValue = _lerp(From, To, progress);
        ValueChanged?.Invoke(_currentValue);
    }
}

/// <summary>
/// Built-in interpolation functions for common types.
/// </summary>
public static class Lerp
{
    public static double Double(double a, double b, double t) => a + (b - a) * t;

    public static float Float(float a, float b, double t) => (float)(a + (b - a) * t);

    public static Color Color(Color a, Color b, double t) => a.Lerp(b, t);

    public static Point Point(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static Thickness Thickness(Thickness a, Thickness b, double t) =>
        new(Double(a.Left, b.Left, t),
            Double(a.Top, b.Top, t),
            Double(a.Right, b.Right, t),
            Double(a.Bottom, b.Bottom, t));
}

/// <summary>
/// Static registry mapping CLR types to boxed interpolation functions.
/// Type-level fact: "Color can lerp", "double can lerp".
/// </summary>
public static class TypeLerp
{
    private static readonly Dictionary<Type, Func<object, object, double, object>> _registry = new();

    static TypeLerp()
    {
        Register<double>(Lerp.Double);
        Register<float>(Lerp.Float);
        Register<Color>(Lerp.Color);
        Register<Point>(Lerp.Point);
        Register<Thickness>(Lerp.Thickness);
    }

    /// <summary>
    /// Registers a lerp function for a value type.
    /// </summary>
    public static void Register<T>(LerpFunc<T> lerp)
    {
        _registry[typeof(T)] = (from, to, t) => (object)lerp((T)from, (T)to, t)!;
    }

    /// <summary>
    /// Returns true if the type has a registered lerp function.
    /// </summary>
    public static bool CanLerp(Type type) => _registry.ContainsKey(type);

    /// <summary>
    /// Interpolates between two boxed values. Snaps to target if no lerp registered.
    /// </summary>
    public static object Interpolate(Type type, object from, object to, double t)
    {
        if (_registry.TryGetValue(type, out var lerp))
            return lerp(from, to, t);
        return to;
    }

    /// <summary>
    /// Returns the registered lerp delegate for a type, or null if none is registered.
    /// Lets callers (e.g. PropertyAnimator) resolve the delegate once per animation instead
    /// of doing a dictionary lookup on every tick.
    /// </summary>
    internal static Func<object, object, double, object>? GetDelegate(Type type)
    {
        return _registry.TryGetValue(type, out var lerp) ? lerp : null;
    }
}
