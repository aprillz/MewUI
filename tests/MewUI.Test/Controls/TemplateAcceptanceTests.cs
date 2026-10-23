using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Every public control either lets a template take over its measure, arrange, render, visual
/// traversal and hit testing, or is listed here with the reason it does not yet.
/// </summary>
[TestClass]
public sealed class TemplateAcceptanceTests
{
    // Controls whose template is built but not used yet; the value names the control-templating plan stage.
    private static readonly Dictionary<Type, string> _deferred = new()
    {
        [typeof(Slider)] = "T2",
        [typeof(ScrollBar)] = "T2",
        [typeof(ProgressBar)] = "T3",
        [typeof(ScrollViewer)] = "T4",
        [typeof(ItemsControl)] = "T4",
        [typeof(ListBox)] = "T4",
        [typeof(TreeView)] = "T4",
        [typeof(GridView)] = "T4",
        [typeof(NavigationList)] = "T4",
        [typeof(TabControl)] = "T5",
        [typeof(SegmentedControl)] = "T5",
        [typeof(ButtonGroup)] = "T5",
        [typeof(MenuBar)] = "T5",
        [typeof(ToolBar)] = "T5",
        [typeof(Calendar)] = "T5",
        [typeof(TextBox)] = "T6",
        [typeof(PasswordBox)] = "T6",
        [typeof(MultiLineTextBox)] = "T6",
        [typeof(SyntaxViewer)] = "T6",
        [typeof(NavigationView)] = "T8",
        [typeof(ContextMenu)] = "T8",
    };

    // Controls a template does not target.
    private static readonly Dictionary<Type, string> _notTargets = new()
    {
        [typeof(Border)] = "decorator element: template material, not a template target",
        [typeof(TransitionContentControl)] = "owns two contents during a transition; there is no chrome for a template to replace",
        [typeof(Window)] = "needs a backend; covered by WindowTemplateTests",
    };

    // Parts a control's OnApplyTemplate requires. They are registered but kept out of the tree so the
    // probe stays the only visual the template contributes.
    private static readonly Dictionary<Type, Action<ControlTemplateContext>> _requiredParts = new()
    {
        [typeof(DropDownButton)] = ctx => ctx.Register(DropDownButton.PART_DROP_DOWN_BUTTON, new Button()),
        [typeof(SplitButton)] = ctx =>
        {
            ctx.Register(SplitButton.PART_PRIMARY_BUTTON, new Button());
            ctx.Register(SplitButton.PART_DROP_DOWN_BUTTON, new Button());
        },
    };

    private static IEnumerable<Type> PublicControls()
        => typeof(Control).Assembly.GetTypes()
            .Where(type => type.IsPublic
                && !type.IsAbstract
                && !type.IsGenericTypeDefinition
                && type.IsSubclassOf(typeof(Control))
                && type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    public static IEnumerable<object[]> AcceptedControls()
        => PublicControls()
            .Where(type => !_deferred.ContainsKey(type) && !_notTargets.ContainsKey(type))
            .Select(type => new object[] { type });

    public static string AcceptedControlName(System.Reflection.MethodInfo method, object[] data)
        => $"{method.Name}({((Type)data[0]).Name})";

    [TestMethod]
    public void EveryPublicControlIsClassifiedOnce()
    {
        var controls = PublicControls().ToList();
        foreach (var type in controls)
        {
            Assert.IsFalse(_deferred.ContainsKey(type) && _notTargets.ContainsKey(type),
                $"{type.Name} is listed as both deferred and not a target");
        }

        foreach (var listed in _deferred.Keys.Concat(_notTargets.Keys))
        {
            Assert.IsTrue(controls.Contains(listed), $"{listed.Name} is listed but is not a public control with a parameterless constructor");
        }
    }

    [TestMethod]
    [DynamicData(nameof(AcceptedControls), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(AcceptedControlName))]
    public void TemplateRootOwnsLayoutRenderTraversalAndHitTesting(Type controlType)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var name = controlType.Name;
        var control = (Control)Activator.CreateInstance(controlType)!;
        var probe = new ProbeElement();
        control.Template = new DelegateControlTemplate<Control>((_, ctx) =>
        {
            if (_requiredParts.TryGetValue(controlType, out var registerParts))
            {
                registerParts(ctx);
            }

            return probe;
        });

        var window = HeadlessWindow.Create();
        window.Content = control;
        window.PerformLayout();

        Assert.IsTrue(probe.MeasureCount > 0, $"{name}: the template root was not measured");
        if (double.IsNaN(control.Width) && control.MinWidth <= 0)
        {
            Assert.AreEqual(probe.DesiredSize.Width, control.DesiredSize.Width, 0.01,
                $"{name}: the desired width does not come from the template root");
        }

        if (double.IsNaN(control.Height) && control.MinHeight <= 0)
        {
            Assert.AreEqual(probe.DesiredSize.Height, control.DesiredSize.Height, 0.01,
                $"{name}: the desired height does not come from the template root");
        }

        Assert.AreEqual(control.Bounds, probe.Bounds, $"{name}: the template root is not arranged to the control bounds");

        var recorder = new DrawRecordingContext();
        control.Render(recorder);
        Assert.AreEqual(1, probe.RenderCount, $"{name}: the template root was not rendered exactly once");
        Assert.AreEqual(1, recorder.DrawCalls,
            $"{name}: expected the template root's single draw call, but {recorder.DrawCalls} draw calls were recorded");
        CollectionAssert.Contains(recorder.FillColors, ProbeElement.Marker, $"{name}: the recorded draw call is not the probe's");

        var visited = new List<Element>();
        ((IVisualTreeHost)control).VisitChildren(child =>
        {
            visited.Add(child);
            return true;
        });
        Assert.AreEqual(1, visited.Count, $"{name}: visual traversal yields {visited.Count} children instead of the template root alone");
        Assert.AreSame(probe, visited[0], $"{name}: visual traversal does not yield the template root");

        if (control.IsHitTestVisible)
        {
            var bounds = control.Bounds;
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            Assert.AreSame(probe, window.HitTest(center), $"{name}: hit testing does not reach the template root");
        }
    }

    private sealed class ProbeElement : FrameworkElement
    {
        public static readonly Color Marker = Color.FromRgb(1, 2, 3);

        public int MeasureCount { get; private set; }

        public int RenderCount { get; private set; }

        protected override Size MeasureContent(Size availableSize)
        {
            MeasureCount++;
            return new Size(37, 19);
        }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            context.FillRectangle(Bounds, Marker);
        }
    }

    private sealed class DrawRecordingContext : NoOpGraphicsContext, IGraphicsContext
    {
        private RecordingTextContext? _text;

        public int DrawCalls { get; private set; }

        public List<Color> FillColors { get; } = [];

        ITextRenderContext IGraphicsContext.Text => _text ??= new RecordingTextContext(this);

        public override void DrawLine(Point start, Point end, Color color, double thickness = 1) => DrawCalls++;
        public override void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap) => DrawCalls++;
        public override void DrawLine(Point start, Point end, Pen pen) => DrawCalls++;
        public override void DrawRectangle(Rect rect, Color color, double thickness = 1) => DrawCalls++;
        public override void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset) => DrawCalls++;
        public override void DrawRectangle(Rect rect, Pen pen) => DrawCalls++;

        public override void FillRectangle(Rect rect, Color color)
        {
            DrawCalls++;
            FillColors.Add(color);
        }

        public override void FillRectangle(Rect rect, Brush brush) => DrawCalls++;
        public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1) => DrawCalls++;
        public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset) => DrawCalls++;
        public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Pen pen) => DrawCalls++;
        public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) => DrawCalls++;
        public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Brush brush) => DrawCalls++;
        public override void DrawEllipse(Rect bounds, Color color, double thickness = 1) => DrawCalls++;
        public override void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset) => DrawCalls++;
        public override void DrawEllipse(Rect bounds, Pen pen) => DrawCalls++;
        public override void FillEllipse(Rect bounds, Color color) => DrawCalls++;
        public override void FillEllipse(Rect bounds, Brush brush) => DrawCalls++;
        public override void DrawPath(PathGeometry path, Color color, double thickness = 1) => DrawCalls++;
        public override void DrawPath(PathGeometry path, Pen pen) => DrawCalls++;
        public override void FillPath(PathGeometry path, Color color) => DrawCalls++;
        public override void FillPath(PathGeometry path, Color color, FillRule fillRule) => DrawCalls++;
        public override void FillPath(PathGeometry path, Brush brush) => DrawCalls++;
        public override void FillPath(PathGeometry path, Brush brush, FillRule fillRule) => DrawCalls++;
        public override void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius, Color shadowColor, double offsetX = 0, double offsetY = 0) => DrawCalls++;
        public override void DrawImage(IImage image, Point location) => DrawCalls++;
        public override void DrawImage(IImage image, Rect destRect) => DrawCalls++;
        public override void DrawImage(IImage image, Rect destRect, Rect sourceRect) => DrawCalls++;

        private sealed class RecordingTextContext(DrawRecordingContext owner) : ITextRenderContext
        {
            public IGraphicsContext Graphics => owner;
            public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) => owner.DrawCalls++;
            public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options) => owner.DrawCalls++;
            public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options) => owner.DrawCalls++;
        }
    }
}
