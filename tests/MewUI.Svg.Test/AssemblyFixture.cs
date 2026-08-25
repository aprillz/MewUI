using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Svg.Test;

[TestClass]
public static class AssemblyFixture
{
    private static GdiGraphicsFactory? _factory;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            _factory = new GdiGraphicsFactory();
            Application.DefaultGraphicsFactory = _factory;
        }
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
