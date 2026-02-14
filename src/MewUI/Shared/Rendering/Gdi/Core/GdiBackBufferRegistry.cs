namespace Aprillz.MewUI.Rendering.Gdi.Core;

internal static class GdiBackBufferRegistry
{
    private static readonly object gate = new();
    private static readonly Dictionary<nint, Entry> entries = new();

    public readonly struct Entry
    {
        public Entry(nint bits, int width, int height, int stride)
        {
            Bits = bits;
            Width = width;
            Height = height;
            Stride = stride;
        }

        public nint Bits { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
    }

    public static void Register(nint memDc, nint bits, int width, int height, int stride)
    {
        if (memDc == 0 || bits == 0 || width <= 0 || height <= 0 || stride <= 0)
        {
            return;
        }

        lock (gate)
        {
            entries[memDc] = new Entry(bits, width, height, stride);
        }
    }

    public static void Unregister(nint memDc)
    {
        if (memDc == 0)
        {
            return;
        }

        lock (gate)
        {
            entries.Remove(memDc);
        }
    }

    public static bool TryGet(nint memDc, out Entry entry)
    {
        lock (gate)
        {
            return entries.TryGetValue(memDc, out entry);
        }
    }
}

