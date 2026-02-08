namespace Aprillz.MewUI;

/// <summary>
/// General fluent API extensions.
/// </summary>
public static class FluentExtensions
{
    /// <summary>
    /// Captures a reference to the object.
    /// </summary>
    /// <typeparam name="T">Object type.</typeparam>
    /// <param name="control">The object.</param>
    /// <param name="field">Output reference.</param>
    /// <returns>The object for chaining.</returns>
    public static T Ref<T>(this T control, out T field) where T : class
    {
        field = control;
        return control;
    }

    /// <summary>
    /// Applies an action to the object.
    /// </summary>
    /// <typeparam name="T">Object type.</typeparam>
    /// <param name="obj">Target object.</param>
    /// <param name="action">Action to apply.</param>
    /// <returns>The object for chaining.</returns>
    public static T Apply<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
}
