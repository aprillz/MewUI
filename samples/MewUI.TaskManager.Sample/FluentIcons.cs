using System.Text.RegularExpressions;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.TaskManager.Sample;

internal static partial class FluentIcons
{
    private static Dictionary<string, string>? s_paths;

    public static PathShape Create(string name)
    {
        var paths = s_paths ??= Load();
        if (!paths.TryGetValue(name, out var data))
        {
            throw new InvalidOperationException($"Fluent icon '{name}' was not found.");
        }

        var icon = new PathShape
        {
            Data = PathGeometry.Parse(data),
            Stretch = Stretch.Uniform,
        };
        icon.Bind(
            Shape.FillProperty,
            icon,
            TextElement.ForegroundProperty,
            static (Color color) => (Brush)new SolidColorBrush(color));
        return icon;
    }

    private static Dictionary<string, string> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons.xaml");
        var xaml = File.ReadAllText(path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in IconRegex().Matches(xaml))
        {
            result[match.Groups[1].Value] = WhitespaceRegex()
                .Replace(match.Groups[2].Value.Trim(), " ");
        }

        return result;
    }

    [GeneratedRegex(
        @"<PathGeometry\s+x:Key=""([^""]+)""[^>]*(?<!/)>\s*([\s\S]*?)\s*</PathGeometry>",
        RegexOptions.Compiled)]
    private static partial Regex IconRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
