namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>Picks the display mode a connector is driven at.</summary>
internal static class FramebufferModeSelection
{
    internal readonly record struct Mode(int Width, int Height, uint RefreshHz, bool IsPreferred);

    /// <summary>
    /// The index of the requested size (fastest refresh among equals), else the preferred mode, else the largest
    /// area at the fastest refresh. Returns -1 only when a requested size is absent.
    /// </summary>
    public static int SelectMode(ReadOnlySpan<Mode> modes, (int Width, int Height)? requested)
    {
        int best = -1;
        if (requested is (int width, int height))
        {
            for (int index = 0; index < modes.Length; index++)
            {
                if (modes[index].Width == width && modes[index].Height == height
                    && (best < 0 || modes[index].RefreshHz > modes[best].RefreshHz))
                {
                    best = index;
                }
            }

            return best;
        }

        for (int index = 0; index < modes.Length; index++)
        {
            if (modes[index].IsPreferred)
            {
                return index;
            }
        }

        for (int index = 0; index < modes.Length; index++)
        {
            if (best < 0 || IsLarger(modes[index], modes[best]))
            {
                best = index;
            }
        }

        return best;
    }

    private static bool IsLarger(Mode candidate, Mode current)
    {
        long candidateArea = (long)candidate.Width * candidate.Height;
        long currentArea = (long)current.Width * current.Height;
        if (candidateArea != currentArea)
        {
            return candidateArea > currentArea;
        }

        return candidate.RefreshHz > current.RefreshHz;
    }
}
