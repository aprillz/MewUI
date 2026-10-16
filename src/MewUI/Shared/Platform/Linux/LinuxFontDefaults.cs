namespace Aprillz.MewUI.Platform.Linux;

/// <summary>System UI font and fallback chain shared by the Linux platform hosts (fontconfig resolves the names).</summary>
internal static class LinuxFontDefaults
{
    internal const string SystemFontFamily = "sans-serif";

    internal static string[] BuildFallbacks()
    {
        var locale = Rendering.FontFallback.ResolvedLocale;
        var cjk = Rendering.FontFallback.OrderCjkByLocale(locale,
            kr: "Noto Sans CJK KR", jp: "Noto Sans CJK JP",
            sc: "Noto Sans CJK SC", tc: "Noto Sans CJK TC");

        var chain = new List<string>(16) { "Noto Sans" };
        chain.AddRange(cjk);
        chain.AddRange([
            "Noto Color Emoji",
            "Noto Sans Arabic", "Noto Sans Hebrew",
            "Noto Sans Devanagari", "Noto Sans Thai", "Noto Sans Bengali",
            "DejaVu Sans", "Liberation Sans",
            "Noto Sans Symbols", "Noto Sans Symbols 2", "Noto Sans Math",
        ]);
        return [.. chain];
    }
}
