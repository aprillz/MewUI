namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Optional capability provider for graphics factories and contexts.
/// External packages can use this to attach backend-specific services without
/// requiring the backend assembly to implement those services directly.
/// </summary>
public interface IGraphicsServiceProvider
{
    /// <summary>
    /// Resolves a registered graphics service for the requested type.
    /// Returns <see langword="null"/> when the service is unavailable.
    /// </summary>
    object? GetGraphicsService(Type serviceType);
}

/// <summary>
/// Global registry for backend-attached graphics services.
/// </summary>
public static class GraphicsServiceRegistry
{
    private static readonly object s_lock = new();
    private static readonly List<Func<object, Type, object?>> s_resolvers = [];

    /// <summary>
    /// Registers a global resolver. Resolvers should return <see langword="null"/>
    /// when the supplied instance or service type is not supported.
    /// </summary>
    public static void RegisterResolver(Func<object, Type, object?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        lock (s_lock)
        {
            if (!s_resolvers.Contains(resolver))
            {
                s_resolvers.Add(resolver);
            }
        }
    }

    /// <summary>
    /// Resolves a service for the supplied graphics object.
    /// </summary>
    public static object? Resolve(object instance, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(serviceType);

        Func<object, Type, object?>[] resolvers;
        lock (s_lock)
        {
            resolvers = [.. s_resolvers];
        }

        foreach (var resolver in resolvers)
        {
            var service = resolver(instance, serviceType);
            if (service != null)
            {
                return service;
            }
        }

        return null;
    }
}

public static class GraphicsServiceProviderExtensions
{
    /// <summary>
    /// Tries to resolve a graphics service from the supplied instance.
    /// Direct interface implementation is preferred over provider indirection.
    /// </summary>
    public static T? TryGetGraphicsService<T>(this object? instance) where T : class
    {
        if (instance is T direct)
        {
            return direct;
        }

        if (instance is IGraphicsServiceProvider provider)
        {
            return provider.GetGraphicsService(typeof(T)) as T;
        }

        return null;
    }
}
