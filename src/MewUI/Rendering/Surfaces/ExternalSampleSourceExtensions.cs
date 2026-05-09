using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public static class ExternalSampleSourceExtensions
{
    public static IExternalSampleSource AsExternalSampleSource(
        this IExternalLockedTexture texture,
        ExternalSampleSourceKind kind = ExternalSampleSourceKind.Unknown,
        bool ownsTexture = false)
        => new ExternalLockedTextureSampleSource(texture, kind, ownsTexture);
}
