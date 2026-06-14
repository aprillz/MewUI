using System.Collections.Concurrent;

namespace Aprillz.MewUI.Skia;

/// <summary>
/// Registry of <see cref="ISkiaInteropProvider"/> instances keyed by backend identifier.
/// Interop packages register their provider via <c>Register()</c> at startup, or via the
/// matching <c>UseSkiaXxxInterop(this ApplicationBuilder)</c> extension when using the
/// <see cref="ApplicationBuilder"/> fluent API. <see cref="Controls.SkiaCanvasView"/>
/// resolves the matching provider at first render.
/// </summary>
public static class SkiaInterop
{
    private static readonly ConcurrentDictionary<string, ISkiaInteropProvider> _providers
        = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ISkiaInteropProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.BackendIdentifier] = provider;
    }

    public static bool TryResolve(string backendIdentifier, out ISkiaInteropProvider provider)
    {
        if (_providers.TryGetValue(backendIdentifier, out var found))
        {
            provider = found;
            return true;
        }
        provider = null!;
        return false;
    }
}
