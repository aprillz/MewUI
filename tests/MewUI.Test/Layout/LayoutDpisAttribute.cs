using System.Reflection;

namespace MewUI.Test.Layout;

/// <summary>Runs a layout test once per display scale from 100% to 200%, the fractional ones included.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LayoutDpisAttribute : Attribute, ITestDataSource
{
    // 120 and 168 make one DIP a fractional pixel; 144 puts many edges on half pixels.
    private static readonly uint[] DPIS = [96, 120, 144, 168, 192];

    public IEnumerable<object?[]> GetData(MethodInfo methodInfo) => DPIS.Select(dpi => new object?[] { dpi });

    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data) => $"{methodInfo.Name} ({data?[0]} DPI)";
}
