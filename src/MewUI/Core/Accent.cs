namespace Aprillz.MewUI;

public static class BuiltInAccent
{
    private static readonly BuiltInAccentPair[] _builtInAccents =
    [
        new BuiltInAccentPair(Color.FromRgb(51, 122, 255),  Color.FromRgb(62, 141, 255)),  // Blue
        new BuiltInAccentPair(Color.FromRgb(143, 84, 219),  Color.FromRgb(158, 101, 232)), // Purple
        new BuiltInAccentPair(Color.FromRgb(236, 90, 161),  Color.FromRgb(244, 112, 174)), // Pink
        new BuiltInAccentPair(Color.FromRgb(236, 92, 86),   Color.FromRgb(244, 110, 104)), // Red
        new BuiltInAccentPair(Color.FromRgb(240, 140, 56),  Color.FromRgb(248, 156, 74)),  // Orange
        new BuiltInAccentPair(Color.FromRgb(245, 204, 67),  Color.FromRgb(250, 214, 90)),  // Yellow
        new BuiltInAccentPair(Color.FromRgb(132, 192, 79),  Color.FromRgb(150, 204, 98)),  // Green
        new BuiltInAccentPair(Color.FromRgb(150, 150, 150), Color.FromRgb(165, 165, 165)), // Gray
    ];

    public static IReadOnlyList<Accent> Accents { get; } = Enum.GetValues<Accent>();

    private readonly struct BuiltInAccentPair
    {
        public Color Light { get; }

        public Color Dark { get; }

        public BuiltInAccentPair(Color light, Color dark)
        {
            Light = light;
            Dark = dark;
        }
    }

    internal static Color GetColor(this Accent accent, bool isDark)
    {
        int idx = (int)accent;
        if ((uint)idx >= (uint)_builtInAccents.Length)
        {
            idx = (int)Accent.Gray;
        }

        var pair = _builtInAccents[idx];
        return isDark ? pair.Dark : pair.Light;
    }

    public static Color GetAccentColor(this Accent accent, bool isDark)
        => GetColor(accent, isDark);
}

public enum Accent
{
    Blue,
    Purple,
    Pink,
    Red,
    Orange,
    Yellow,
    Green,
    Gray,
}
