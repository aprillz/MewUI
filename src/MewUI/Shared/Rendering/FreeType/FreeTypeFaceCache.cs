using System.Collections.Concurrent;

using Aprillz.MewUI.Native.FreeType;
using Aprillz.MewUI.Native.HarfBuzz;

using FT = Aprillz.MewUI.Native.FreeType.FreeType;
using HB = Aprillz.MewUI.Native.HarfBuzz.HarfBuzz;

namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed unsafe class FreeTypeFaceCache
{
    public static FreeTypeFaceCache Instance => field ??= new FreeTypeFaceCache();

    private readonly ConcurrentDictionary<FaceKey, FaceEntry> _faces = new();

    private FreeTypeFaceCache() { }

    /// <summary>
    /// Reads the scaled size metrics (26.6 fixed-point) from a FreeType face.
    /// </summary>
    internal static unsafe FT_Size_Metrics GetSizeMetrics(nint face)
    {
        var faceRec = (FT_FaceRec*)face;
        if (faceRec->size == 0)
            return default;
        var sizeRec = (FT_SizeRec*)faceRec->size;
        return sizeRec->metrics;
    }

    public FaceEntry Get(string fontPath, int pixelHeight, FontWeight weight = FontWeight.Normal, bool italic = false)
    {
        var key = new FaceKey(fontPath, Math.Max(1, pixelHeight), weight, italic);
        var entry = _faces.GetOrAdd(key, k => FaceEntry.Create(k.FontPath, k.PixelHeight, k.Weight, k.Italic));
        entry.Touch();
        return entry;
    }

    public void Trim(int maxEntries)
    {
        if (maxEntries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        var removeCount = _faces.Count - maxEntries;
        if (removeCount <= 0)
        {
            return;
        }

        foreach (var pair in _faces.OrderBy(static pair => pair.Value.LastUsedTicks).Take(removeCount))
        {
            if (_faces.TryRemove(pair.Key, out var entry))
            {
                entry.Dispose();
            }
        }
    }

    public void Clear()
    {
        foreach (var pair in _faces)
        {
            if (_faces.TryRemove(pair.Key, out var entry))
            {
                entry.Dispose();
            }
        }
    }

    internal readonly record struct FaceKey(string FontPath, int PixelHeight, FontWeight Weight, bool Italic);

    internal sealed class FaceEntry : IDisposable
    {
        private readonly ConcurrentDictionary<uint, uint> _glyphIndexCache = new();
        private readonly ConcurrentDictionary<uint, int> _advanceCache = new();
        private readonly ConcurrentDictionary<uint, ScaledBitmap?> _scaledBitmapCache = new();

        /// <summary>
        /// Pre-scaled color emoji bitmap cached per glyph ID.
        /// Avoids repeated area-average downscaling for the same glyph.
        /// </summary>
        internal readonly record struct ScaledBitmap(byte[] Bgra, int Width, int Height, int Left, int Top);
        private nint _hbFont;
        private bool _disposed;

        private FaceEntry(nint face, double bitmapScale = 1.0)
        {
            Face = face;
            BitmapScale = bitmapScale;
        }

        public nint Face { get; private set; }

        /// <summary>
        /// Scale factor for bitmap-only fonts. The selected strike may differ from the
        /// requested pixel size; glyphs must be scaled by this factor during blit.
        /// Always 1.0 for scalable (outline) fonts.
        /// </summary>
        public double BitmapScale { get; }

        public long LastUsedTicks { get; private set; } = DateTime.UtcNow.Ticks;

        public object SyncRoot { get; } = new();

        public static FaceEntry Create(string fontPath, int pixelHeight, FontWeight weight = FontWeight.Normal, bool italic = false)
        {
            var lib = FreeTypeLibrary.Instance;

            int err = FT.FT_New_Face(lib.Handle, fontPath, 0, out nint face);
            if (err != 0 || face == 0)
            {
                throw new InvalidOperationException($"FT_New_Face failed: {err} ({fontPath})");
            }

            double bitmapScale = 1.0;
            err = FT.FT_Set_Pixel_Sizes(face, 0, (uint)Math.Max(1, pixelHeight));
            if (err != 0)
            {
                // Bitmap-only fonts (e.g. Noto Color Emoji) require a fixed strike size.
                int strikeHeight = TrySelectNearestBitmapStrike(face, pixelHeight);
                if (strikeHeight <= 0)
                {
                    FT.FT_Done_Face(face);
                    throw new InvalidOperationException($"FT_Set_Pixel_Sizes failed: {err}");
                }
                bitmapScale = (double)pixelHeight / strikeHeight;
            }

            TrySetVariableAxes(lib.Handle, face, weight, italic);

            return new FaceEntry(face, bitmapScale);
        }

        /// <summary>
        /// Selects the bitmap strike closest to <paramref name="pixelHeight"/>.
        /// Returns the selected strike's pixel height, or 0 on failure.
        /// </summary>
        private static int TrySelectNearestBitmapStrike(nint face, int pixelHeight)
        {
            var faceRec = (FT_FaceRec*)face;
            int numSizes = faceRec->num_fixed_sizes;
            if (numSizes <= 0 || faceRec->available_sizes == 0)
                return 0;

            var sizes = (FT_Bitmap_Size*)faceRec->available_sizes;
            int bestIndex = 0;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < numSizes; i++)
            {
                int diff = Math.Abs(sizes[i].height - pixelHeight);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIndex = i;
                }
            }

            if (FT.FT_Select_Size(face, bestIndex) != 0)
                return 0;

            return sizes[bestIndex].height;
        }

        private static void TrySetVariableAxes(nint library, nint face, FontWeight weight, bool italic)
        {
            var faceRec = (FT_FaceRec*)face;
            if (((long)faceRec->face_flags & FreeTypeFaceFlags.FT_FACE_FLAG_MULTIPLE_MASTERS) == 0)
            {
                return; // Not a variable font.
            }

            if (FT.FT_Get_MM_Var(face, out nint mmVarPtr) != 0 || mmVarPtr == 0)
            {
                return;
            }

            try
            {
                var mmVar = (FT_MM_Var*)mmVarPtr;
                uint numAxes = mmVar->num_axis;
                if (numAxes == 0)
                {
                    return;
                }

                // Build coordinate array starting from axis defaults.
                var coords = stackalloc nint[(int)numAxes];
                for (uint i = 0; i < numAxes; i++)
                {
                    coords[i] = mmVar->axis[i].def;
                }

                // Override specific axes.
                int cssWeight = ToCssWeight(weight);
                for (uint i = 0; i < numAxes; i++)
                {
                    uint tag = mmVar->axis[i].tag;
                    if (tag == FreeTypeVarAxisTags.WGHT)
                    {
                        // FT_Fixed 16.16 format.
                        coords[i] = (nint)((long)cssWeight << 16);
                    }
                    else if (tag == FreeTypeVarAxisTags.ITAL && italic)
                    {
                        coords[i] = (nint)(1L << 16); // ital=1.0
                    }
                    else if (tag == FreeTypeVarAxisTags.SLNT && italic)
                    {
                        // Typical slant axis: -12 degrees for italic.
                        coords[i] = (nint)(-12L << 16);
                    }
                }

                FT.FT_Set_Var_Design_Coordinates(face, numAxes, (nint)coords);
            }
            finally
            {
                FT.FT_Done_MM_Var(library, mmVarPtr);
            }
        }

        private static int ToCssWeight(FontWeight weight) => weight switch
        {
            FontWeight.Thin => 100,
            FontWeight.ExtraLight => 200,
            FontWeight.Light => 300,
            FontWeight.Normal => 400,
            FontWeight.Medium => 500,
            FontWeight.SemiBold => 600,
            FontWeight.Bold => 700,
            FontWeight.ExtraBold => 800,
            FontWeight.Black => 900,
            _ => (int)weight,
        };

        public void Touch() => LastUsedTicks = DateTime.UtcNow.Ticks;

        internal ScaledBitmap? GetScaledBitmap(uint glyphId)
            => _scaledBitmapCache.TryGetValue(glyphId, out var bmp) ? bmp : null;

        internal void CacheScaledBitmap(uint glyphId, ScaledBitmap bmp)
            => _scaledBitmapCache.TryAdd(glyphId, bmp);

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
                    if (BitmapScale != 1.0)
                        px *= BitmapScale;
                    return (int)Math.Round(px, MidpointRounding.AwayFromZero);
                }
            });
        }

        public nint GetGlyphSlotPointer()
        {
            var faceRec = (FT_FaceRec*)Face;
            return faceRec->glyph;
        }

        public nint GetOrCreateHbFont()
        {
            if (_hbFont != 0)
            {
                return _hbFont;
            }

            if (!HarfBuzzAvailability.IsAvailable)
            {
                return 0;
            }

            lock (SyncRoot)
            {
                if (_hbFont == 0)
                {
                    _hbFont = HB.hb_ft_font_create(Face, 0);
                }
            }

            return _hbFont;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var hbFont = _hbFont;
            _hbFont = 0;
            if (hbFont != 0)
            {
                HB.hb_font_destroy(hbFont);
            }

            var face = Face;
            Face = 0;
            if (face != 0)
            {
                FT.FT_Done_Face(face);
            }
        }
    }
}
