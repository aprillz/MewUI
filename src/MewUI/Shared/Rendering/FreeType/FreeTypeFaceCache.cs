using System.Collections.Concurrent;

using Aprillz.MewUI.Native.FreeType;

using FT = Aprillz.MewUI.Native.FreeType.FreeType;

namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed unsafe class FreeTypeFaceCache
{
    public static FreeTypeFaceCache Instance => field ??= new FreeTypeFaceCache();

    private readonly ConcurrentDictionary<FaceKey, FaceEntry> _faces = new();

    private FreeTypeFaceCache() { }

    public FaceEntry Get(string fontPath, int pixelHeight)
    {
        var key = new FaceKey(fontPath, Math.Max(1, pixelHeight));
        var entry = _faces.GetOrAdd(key, k => FaceEntry.Create(k.FontPath, k.PixelHeight));
        entry.Touch();
        return entry;
    }

    internal readonly record struct FaceKey(string FontPath, int PixelHeight);

    internal sealed class FaceEntry : IDisposable
    {
        private readonly ConcurrentDictionary<uint, uint> _glyphIndexCache = new();
        private readonly ConcurrentDictionary<uint, int> _advanceCache = new();
        private bool _disposed;

        private FaceEntry(nint face) => Face = face;

        public nint Face { get; private set; }

        public long LastUsedTicks { get; private set; } = DateTime.UtcNow.Ticks;

        public object SyncRoot { get; } = new();

        public static FaceEntry Create(string fontPath, int pixelHeight)
        {
            var lib = FreeTypeLibrary.Instance;

            int err = FT.FT_New_Face(lib.Handle, fontPath, 0, out nint face);
            if (err != 0 || face == 0)
            {
                throw new InvalidOperationException($"FT_New_Face failed: {err} ({fontPath})");
            }

            err = FT.FT_Set_Pixel_Sizes(face, 0, (uint)Math.Max(1, pixelHeight));
            if (err != 0)
            {
                throw new InvalidOperationException($"FT_Set_Pixel_Sizes failed: {err}");
            }

            return new FaceEntry(face);
        }

        public void Touch() => LastUsedTicks = DateTime.UtcNow.Ticks;

        public uint GetGlyphIndex(uint charCode)
            => _glyphIndexCache.GetOrAdd(charCode, c => FT.FT_Get_Char_Index(Face, c));

        public int GetAdvancePx(uint charCode)
        {
            return _advanceCache.GetOrAdd(charCode, c =>
            {
                lock (SyncRoot)
                {
                    uint gindex = GetGlyphIndex(c);
                    if (gindex == 0)
                    {
                        return 0;
                    }

                    // Prefer LIGHT target for crisper UI text on typical Linux setups.
                    int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;
                    int err = FT.FT_Get_Advance(Face, gindex, flags, out nint advFixed);
                    if (err != 0)
                    {
                        return 0;
                    }

                    // FT_Fixed 16.16 pixels (scaled).
                    double px = (long)advFixed / 65536.0;
                    return (int)Math.Round(px, MidpointRounding.AwayFromZero);
                }
            });
        }

        public nint GetGlyphSlotPointer()
        {
            var faceRec = (FT_FaceRec*)Face;
            return faceRec->glyph;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var face = Face;
            Face = 0;
            if (face != 0)
            {
                FT.FT_Done_Face(face);
            }
        }
    }
}
