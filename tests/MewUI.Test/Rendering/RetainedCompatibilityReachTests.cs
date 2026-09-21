using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// How far a single change reaches depends on whether the panel holding it declares its composition.
/// A panel that does not is recorded as one subtree, so changing one child repaints every sibling.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedCompatibilityReachTests
{
    private const int SURFACE_WIDTH = 240;
    private const int SURFACE_HEIGHT = 180;

    private sealed class FillBox : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 200, 60, 60);

        protected override Size MeasureContent(Size availableSize) => new(80, 40);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    [TestMethod]
    public void DirtyReach_UnderDeclaredAndUndeclaredPanels()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        Report("StackPanel", () => new StackPanel { Orientation = Orientation.Vertical });
        Report("WrapPanel", () => new WrapPanel());
        Report("DockPanel", () => new DockPanel());
    }

    private static void Report(string label, Func<Panel> makePanel)
    {
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var top = new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) };
        var bottom = new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) };
        var panel = makePanel();
        panel.Children(top, bottom);

        var window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        window.Content = panel;
        window.PerformLayout();

        using var live = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        window.RenderFrameToSurface(live);
        window.RenderFrameToSurface(live);

        top.Fill = Color.FromArgb(255, 10, 200, 90);
        top.InvalidateVisual();
        window.RenderFrameToSurface(live);

        var dirtyRect = window.LastRetainedDirtyRect;
        string reach = dirtyRect is Rect area
            ? $"{area} (touches the unchanged sibling at {bottom.Bounds}: {area.Contains(new Point(bottom.Bounds.X + 2, bottom.Bounds.Y + 2))})"
            : "the whole surface";
        Console.Error.WriteLine($"{label} root: changing one child repaints {reach}");
    }

    [TestMethod]
    public void CompositionCoverage_AcrossEveryVisualType()
    {
        var visualBase = typeof(UIElement);
        var types = visualBase.Assembly.GetTypes()
            .Where(candidate => !candidate.IsAbstract && visualBase.IsAssignableFrom(candidate))
            .OrderBy(candidate => candidate.Name)
            .ToList();

        var unsupported = types.Where(candidate => !ExpectedToDeclareComposition(candidate)).ToList();
        Console.Error.WriteLine($"declared composition: {types.Count - unsupported.Count}/{types.Count} visual types");
        foreach (var candidate in unsupported)
        {
            Console.Error.WriteLine($"  compatibility subtree: {candidate.Name}");
        }

        // The product works the answer out from delegates bound to the instance, without looking methods
        // up by name. Wherever a visual can be created here, it has to agree with the lookup by name.
        int compared = 0;
        foreach (var candidate in types)
        {
            var constructor = candidate.GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (constructor == null || candidate.ContainsGenericParameters || typeof(Window).IsAssignableFrom(candidate))
            {
                continue;
            }

            UIElement visual;
            try
            {
                visual = (UIElement)constructor.Invoke(null);
            }
            catch (System.Reflection.TargetInvocationException)
            {
                continue;
            }

            Assert.AreEqual(
                ExpectedToDeclareComposition(candidate),
                visual.SupportsDeclaredComposition,
                $"{candidate.Name} disagrees with the lookup by name");
            compared++;
        }

        Console.Error.WriteLine($"compared with live instances: {compared}");
        Assert.IsGreaterThan(types.Count / 2, compared, $"only {compared} of {types.Count} visual types could be created to compare");
    }

    /// <summary>The same rule as the product's, worked out by looking the two methods up by name.</summary>
    private static bool ExpectedToDeclareComposition(Type type)
    {
        const System.Reflection.BindingFlags LOOKUP =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        Type? subtreeOwner = null;
        Type? compositionOwner = null;
        foreach (var method in type.GetMethods(LOOKUP))
        {
            if (method.GetParameters().Length != 1)
            {
                continue;
            }

            if (method.Name == "RenderSubtree")
            {
                subtreeOwner = method.DeclaringType;
            }
            else if (method.Name == "WriteComposition")
            {
                compositionOwner = method.DeclaringType;
            }
        }

        return subtreeOwner != null && compositionOwner != null && subtreeOwner.IsAssignableFrom(compositionOwner);
    }
}
