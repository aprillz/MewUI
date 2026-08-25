using System.Runtime.CompilerServices;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal static class TextServices
{
    private static readonly ConditionalWeakTable<IGraphicsFactory, EngineEntry> Engines = new();
    private static readonly ConditionalWeakTable<IGraphicsContext, RenderContextEntry> RenderContexts = new();

    public static ITextEngine GetEngine(IGraphicsFactory factory)
        => Engines.GetValue(factory, static value => new EngineEntry(value)).GetOrCreate();

    public static ITextRenderContext GetRenderContext(IGraphicsContext context)
        => RenderContexts.GetValue(context, static value => new RenderContextEntry(value)).GetOrCreate();

    public static void ReleaseRenderContext(IGraphicsContext context)
        => RenderContexts.GetValue(context, static value => new RenderContextEntry(value)).DisposeIfCreated();

    public static void ReleaseIfCreated(IGraphicsFactory factory)
        => Engines.GetValue(factory, static value => new EngineEntry(value)).DisposeIfCreated();

    public static void TrimIfCreated(IGraphicsFactory factory)
    {
        if (Engines.TryGetValue(factory, out var entry))
        {
            entry.TrimIfCreated();
        }
    }

    private sealed class EngineEntry(IGraphicsFactory owner)
    {
        private readonly object _sync = new();
        private ManagedTextEngine? _value;
        private bool _disposed;

        internal ManagedTextEngine GetOrCreate()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, owner);
                return _value ??= new ManagedTextEngine(owner);
            }
        }

        internal void DisposeIfCreated()
        {
            lock (_sync)
            {
                _value?.Dispose();
                _value = null;
                _disposed = true;
            }
        }

        internal void TrimIfCreated()
        {
            lock (_sync)
            {
                _value?.ManagedCache.Trim();
            }
        }
    }

    private sealed class RenderContextEntry(IGraphicsContext owner)
    {
        private readonly object _sync = new();
        private ManagedTextRenderContext? _value;
        private bool _disposed;

        internal ManagedTextRenderContext GetOrCreate()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, owner);
                return _value ??= new ManagedTextRenderContext(owner);
            }
        }

        internal void DisposeIfCreated()
        {
            lock (_sync)
            {
                _value?.Dispose();
                _value = null;
                _disposed = true;
            }
        }
    }
}
