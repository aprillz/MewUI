namespace Aprillz.MewUI;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

public sealed class AppOptions
{
    public PlatformHostKind? Platform { get; set; }

    public GraphicsBackend? GraphicsBackend { get; set; }

    public ThemeVariant? ThemeMode { get; set; }

    public Accent? Accent { get; set; }

    public ThemeSeed? LightSeed { get; set; }

    public ThemeSeed? DarkSeed { get; set; }

    public ThemeMetrics? Metrics { get; set; }
}

