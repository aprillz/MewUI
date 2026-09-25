using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>Describes a shape hit-test provider's result.</summary>
public enum ShapeHitTestResult
{
    /// <summary>The provider does not handle the shape.</summary>
    Unhandled,

    /// <summary>The shape contains the hit-test point.</summary>
    Hit,

    /// <summary>The shape does not contain the hit-test point.</summary>
    Miss,
}

/// <summary>Provides precise hit testing for <see cref="Shape"/> instances.</summary>
public interface IShapeHitTestProvider
{
    /// <summary>Tests whether a shape contains a point after its bounds test succeeds.</summary>
    /// <param name="shape">The shape being tested.</param>
    /// <param name="renderedGeometry">The rendered geometry, which the provider must not modify.</param>
    /// <param name="point">The point in window coordinates.</param>
    ShapeHitTestResult HitTest(Shape shape, PathGeometry? renderedGeometry, Point point);
}

/// <summary>Registers precise hit testing supplied by optional shape extensions.</summary>
public static class ShapeHitTesting
{
    private static readonly object _syncRoot = new();
    private static volatile ShapeHitTestRegistration[] _registrations = [];

    /// <summary>Registers a provider until the returned registration is disposed; newest providers run first.</summary>
    public static IDisposable Register(IShapeHitTestProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var registration = new ShapeHitTestRegistration(provider);
        lock (_syncRoot)
        {
            _registrations = [.. _registrations, registration];
        }

        return registration;
    }

    internal static ShapeHitTestResult HitTest(Shape shape, PathGeometry? renderedGeometry, Point point)
    {
        ShapeHitTestRegistration[] registrations = _registrations;
        for (int registrationIndex = registrations.Length - 1; registrationIndex >= 0; registrationIndex--)
        {
            ShapeHitTestResult result = registrations[registrationIndex].Provider.HitTest(shape, renderedGeometry, point);
            if (result != ShapeHitTestResult.Unhandled)
            {
                return result;
            }
        }

        return ShapeHitTestResult.Unhandled;
    }

    private static void Unregister(ShapeHitTestRegistration registration)
    {
        lock (_syncRoot)
        {
            int registrationIndex = Array.IndexOf(_registrations, registration);
            if (registrationIndex < 0)
            {
                return;
            }

            var registrations = new ShapeHitTestRegistration[_registrations.Length - 1];
            Array.Copy(_registrations, registrations, registrationIndex);
            Array.Copy(
                _registrations,
                registrationIndex + 1,
                registrations,
                registrationIndex,
                registrations.Length - registrationIndex);
            _registrations = registrations;
        }
    }

    private sealed class ShapeHitTestRegistration : IDisposable
    {
        private int _isDisposed;

        public ShapeHitTestRegistration(IShapeHitTestProvider provider)
        {
            Provider = provider;
        }

        public IShapeHitTestProvider Provider { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                Unregister(this);
            }
        }
    }
}
