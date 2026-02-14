using FT = Aprillz.MewUI.Native.FreeType.FreeType;

namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed class FreeTypeLibrary : IDisposable
{
    public static FreeTypeLibrary Instance => field ??= new FreeTypeLibrary();

    private nint _library;
    private bool _disposed;

    private FreeTypeLibrary()
    {
        int err = FT.FT_Init_FreeType(out _library);
        if (err != 0 || _library == 0)
        {
            throw new InvalidOperationException($"FT_Init_FreeType failed: {err}");
        }
    }

    public nint Handle => _library;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_library != 0)
        {
            FT.FT_Done_FreeType(_library);
            _library = 0;
        }
    }
}
