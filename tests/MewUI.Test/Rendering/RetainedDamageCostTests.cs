extern alias MewVGWin32;

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Forces each partial-update execution candidate of the retained plan on an offscreen surface and
/// reports its per-frame cost, so the damage thresholds rest on measurements instead of estimates.
/// The measuring methods only run when MEWUI_DAMAGE_COST is set; the correctness method always runs.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDamageCostTests
{
    private const int SURFACE_WIDTH = 1280;
    private const int SURFACE_HEIGHT = 800;
    private const int CELL_WIDTH = 64;
    private const int CELL_HEIGHT = 32;
    private const int WARMUP_FRAMES = 10;
    private const int WARMUP_MILLISECONDS = 600;
    private const int MEASURED_FRAMES = 60;
    private const int SPLIT_REGION_COUNT = 8;

    private static readonly double[] _damageRatios = [0.01, 0.05, 0.25, 0.5, 1.0];
    private static readonly Color _background = Color.FromArgb(255, 250, 250, 250);
    private static readonly Color _cellStroke = Color.FromArgb(255, 60, 60, 70);

    /// <summary>A cell whose own content is a fixed number of primitives, so command density is known.</summary>
    private sealed class CostCell : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 120, 150, 200);

        protected override Size MeasureContent(Size availableSize) => new(CELL_WIDTH, CELL_HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            var bounds = Bounds;
            context.FillRectangle(bounds, Fill);
            context.DrawRectangle(bounds, _cellStroke, 1);
            context.FillRectangle(
                new Rect(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, 6),
                Color.FromArgb(160, 255, 255, 255));
        }
    }

    [TestMethod]
    public void RepeatedPartialUpdates_MatchAFullReplay()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var tight = RunRepeatedPartialUpdates(inflatePixels: 0);

        Assert.AreEqual(
            0,
            tight.DifferingPixels,
            $"Repeated partial updates left {tight.DifferingPixels} pixels different from a full replay, " +
            $"first at {tight.FirstDifference} (max channel delta {tight.MaxChannelDelta}).");
    }

    /// <summary>
    /// A frame can repaint everything, the box around what changed, or each changed area by itself.
    /// Which one a frame takes is a matter of cost alone, so all three have to end at the same pixels.
    /// </summary>
    [TestMethod]
    [DataRow("Whole")]
    [DataRow("Union")]
    [DataRow("Split")]
    public void EveryDamageCandidate_EndsAtTheSamePixels(string candidate)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var cells = new List<CostCell>();
        var root = BuildScene(cells);
        LayoutScene(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        using var shown = CreateSurface(factory, hasAlpha: false);
        using var shownContext = factory.CreateContext(shown);
        shownContext.BeginFrame(shown);
        shownContext.Clear(_background);
        UpdateAndReplayWhole(scene, capture, registry, root, shownContext);
        shownContext.EndFrame();

        var random = new Random(20260920);
        for (int round = 0; round < 12; round++)
        {
            // Two cells far apart, so the areas and the box around them differ.
            for (int change = 0; change < 2; change++)
            {
                var cell = cells[random.Next(cells.Count)];
                cell.Fill = Color.FromArgb(255, (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                cell.InvalidateVisual();
            }

            scene.ResetDamage();
            SetPreserve(shown, true);
            shownContext.BeginFrame(shown);
            capture.Capture(scene, root, new RenderDataRecorder(shownContext) { SuppressDrawing = true }, registry);
            shownContext.EndFrame();
            Assert.IsTrue(scene.HasDamage && !scene.IsFullDamage, "the round changed nothing the scene could bound");

            var areas = new Rect[scene.DamageRegion.Areas.Count];
            for (int index = 0; index < areas.Length; index++)
            {
                areas[index] = SnapOut(scene.DamageRegion.Areas[index]);
            }

            if (candidate == "Whole")
            {
                RenderFullSurface(scene, shown, shownContext, clearingBeginFrame: false);
            }
            else if (candidate == "Union")
            {
                RenderDamageRegions(scene, shown, shownContext, [SnapOut(scene.DamageBounds)]);
            }
            else
            {
                RenderDamageRegions(scene, shown, shownContext, areas);
            }
        }

        using var whole = CreateSurface(factory, hasAlpha: false);
        using (var wholeContext = factory.CreateContext(whole))
        {
            wholeContext.BeginFrame(whole);
            wholeContext.Clear(_background);
            FrameRenderer.Replay(scene, wholeContext);
            wholeContext.EndFrame();
        }

        var difference = ComparePixels(shown, whole);
        Assert.AreEqual(0, difference.DifferingPixels, $"{candidate}: {difference.DifferingPixels} pixels differ from a full replay, first at {difference.FirstDifference}");
    }

    /// <summary>
    /// Repeats single-cell updates through the damage path and reports how far the surface ends up
    /// from a full replay of the same scene.
    /// </summary>
    private static PixelDifference RunRepeatedPartialUpdates(int inflatePixels)
    {
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var cells = new List<CostCell>();
        var root = BuildScene(cells);
        LayoutScene(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();

        using var partial = CreateSurface(factory, hasAlpha: false);
        var persistent = (IPersistentFrameSurface)partial;
        using var partialContext = factory.CreateContext(partial);

        partialContext.BeginFrame(partial);
        partialContext.Clear(_background);
        UpdateAndReplayWhole(scene, capture, registry, root, partialContext);
        partialContext.EndFrame();

        persistent.PreserveContentsOnBeginFrame = true;

        var random = new Random(20260918);
        for (int round = 0; round < 24; round++)
        {
            var cell = cells[random.Next(cells.Count)];
            cell.Fill = Color.FromArgb(255, (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            cell.InvalidateVisual();

            scene.ResetDamage();
            partialContext.BeginFrame(partial);
            var recorder = new RenderDataRecorder(partialContext) { SuppressDrawing = true };
            capture.Capture(scene, root, recorder, registry);

            if (scene.IsFullDamage || !scene.HasDamage)
            {
                partialContext.Clear(_background);
                FrameRenderer.Replay(scene, partialContext);
            }
            else
            {
                var damage = Inflate(SnapOut(scene.DamageBounds), inflatePixels);
                ((IOpaqueDamageContext)partialContext).ClearRectangle(damage, _background);
                FrameRenderer.Replay(scene, partialContext, damage);
            }

            partialContext.EndFrame();
        }

        using var whole = CreateSurface(factory, hasAlpha: false);
        using (var wholeContext = factory.CreateContext(whole))
        {
            wholeContext.BeginFrame(whole);
            wholeContext.Clear(_background);
            FrameRenderer.Replay(scene, wholeContext);
            wholeContext.EndFrame();
        }

        return ComparePixels(partial, whole);
    }

    [TestMethod]
    public void MeasureDamageCandidates()
    {
        if (!RequestedByEnvironment(out string? skip))
        {
            Assert.Inconclusive(skip);
            return;
        }

        var report = new StringBuilder();
        report.AppendLine(Environment.NewLine + "=== retained damage cost ===");
        report.AppendLine(CultureInfo.InvariantCulture, $"machine={Environment.MachineName} " +
            $"os={Environment.OSVersion.Version} cores={Environment.ProcessorCount} " +
            $"runtime={Environment.Version} configuration={BuildConfiguration()}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"surface={SURFACE_WIDTH}x{SURFACE_HEIGHT} warmup={WARMUP_FRAMES} measured={MEASURED_FRAMES}");

        MeasureBackend(report, "Gdi", () => new GdiGraphicsFactory());
        MeasureBackend(report, "Direct2D", () => new Direct2DGraphicsFactory());
        MeasureBackend(report, "MewVG.Win32", () => new MewVGWin32GraphicsFactory());

        Console.Error.WriteLine(report.ToString());
    }

    /// <summary>
    /// The frame path repaints everything once the damage passes a share of the surface. That share is
    /// only right while it sits where the two costs cross, so this measures both on every backend and
    /// fails when the one the frame path takes costs clearly more than the other.
    /// </summary>
    [TestMethod]
    public void TheFramePath_TakesTheCheaperCandidate()
    {
        if (!RequestedByEnvironment(out string? skip))
        {
            Assert.Inconclusive(skip);
            return;
        }

        var failures = new StringBuilder();
        var report = new StringBuilder(Environment.NewLine + "=== policy against measured cost ===" + Environment.NewLine);
        CheckPolicy(report, failures, "Gdi", () => new GdiGraphicsFactory());
        CheckPolicy(report, failures, "Direct2D", () => new Direct2DGraphicsFactory());
        CheckPolicy(report, failures, "MewVG.Win32", () => new MewVGWin32GraphicsFactory());
        Console.Error.WriteLine(report.ToString());
        Assert.AreEqual(0, failures.Length, failures.ToString());
    }

    private const double POLICY_COST_TOLERANCE = 1.15;

    private static void CheckPolicy(StringBuilder report, StringBuilder failures, string name, Func<IGraphicsFactory> create)
    {
        var factory = create();
        using var disposable = factory as IDisposable;
        using var backgroundScope = factory is MewVGWin32GraphicsFactory mewVG ? mewVG.AcquireBackgroundRenderScope() : null;
        Application.DefaultGraphicsFactory = factory;

        var cells = new List<CostCell>();
        var root = BuildScene(cells);
        LayoutScene(root);
        using var scene = new RenderScene();
        var capture = new SceneCapture();
        using var target = CreateSurface(factory, hasAlpha: false);
        using var targetContext = factory.CreateContext(target);
        targetContext.BeginFrame(target);
        targetContext.Clear(_background);
        UpdateAndReplayWhole(scene, capture, registry: null, root, targetContext);
        targetContext.EndFrame();

        if (target is not IPersistentFrameSurface || targetContext is not IOpaqueDamageContext)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"[{name}] cannot repaint part of a frame: nothing to choose between");
            return;
        }

        WarmUp(() => RenderFullSurface(scene, target, targetContext, clearingBeginFrame: false));
        var whole = Measure(() => RenderFullSurface(scene, target, targetContext, clearingBeginFrame: false));
        foreach (double ratio in _policyRatios)
        {
            var damage = DamageRect(ratio);
            var part = Measure(() => RenderDamageDirect(scene, target, targetContext, damage));
            bool takesWhole = ratio >= Window.WHOLE_FRAME_DAMAGE_RATIO;
            double taken = takesWhole ? whole.Median : part.Median;
            double other = takesWhole ? part.Median : whole.Median;
            report.AppendLine(CultureInfo.InvariantCulture, $"[{name}] ratio {ratio:0.00}: part {part.Median:0.0} us, whole {whole.Median:0.0} us, takes {(takesWhole ? "whole" : "part")}");
            if (taken > other * POLICY_COST_TOLERANCE)
            {
                failures.AppendLine(CultureInfo.InvariantCulture, $"[{name}] at {ratio:0.00} of the surface the frame path takes {(takesWhole ? "whole" : "part")} at {taken:0.0} us while the other costs {other:0.0} us");
            }
        }
    }

    private static readonly double[] _policyRatios = [0.05, 0.25, 0.5, 0.7, 0.8, 0.9, 1.0];

    private static bool RequestedByEnvironment(out string? reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            reason = "The measured backends are Windows-only.";
            return false;
        }

        if (Environment.GetEnvironmentVariable("MEWUI_DAMAGE_COST") != "1")
        {
            reason = "Set MEWUI_DAMAGE_COST=1 to run the damage cost measurement.";
            return false;
        }

        reason = null;
        return true;
    }

    private static string BuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static void MeasureBackend(StringBuilder report, string name, Func<IGraphicsFactory> create)
    {
        IGraphicsFactory? factory = null;
        IDisposable? backgroundScope = null;
        try
        {
            factory = create();
            if (factory is MewVGWin32GraphicsFactory mewVG)
            {
                backgroundScope = mewVG.AcquireBackgroundRenderScope();
            }

            Application.DefaultGraphicsFactory = factory;
            MeasureBackendCore(report, name, factory);
        }
        catch (Exception error)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"[{name}] not measured: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            backgroundScope?.Dispose();
            (factory as IDisposable)?.Dispose();
        }
    }

    private static void MeasureBackendCore(StringBuilder report, string name, IGraphicsFactory factory)
    {
        var cells = new List<CostCell>();
        var root = BuildScene(cells);
        LayoutScene(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();

        using var target = CreateSurface(factory, hasAlpha: false);
        using var targetContext = factory.CreateContext(target);
        targetContext.BeginFrame(target);
        targetContext.Clear(_background);
        UpdateAndReplayWhole(scene, capture, registry: null, root, targetContext);
        targetContext.EndFrame();

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"[{name}]");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  surface type={target.GetType().Name} persistent={target is IPersistentFrameSurface} " +
            $"cpuPixels={target is ICpuPixelSurface} " +
            $"stride={(target as ICpuPixelSurface)?.StrideBytes.ToString(CultureInfo.InvariantCulture) ?? "n/a"}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  nodes={scene.NodeCount} cells={cells.Count} commandBytes={scene.EstimatedCommandBytes} " +
            $"replaysPerFullFrame={scene.Statistics.ContentReplayCount}");

        bool canRepaintPart = target is IPersistentFrameSurface && targetContext is IOpaqueDamageContext;
        if (!canRepaintPart)
        {
            report.AppendLine("  partial repaint unavailable on this surface: damage candidates not measured.");
        }

        // A GPU backend starts at a low clock, so the first block measured otherwise reads several
        // times its steady cost. This spins the heaviest path until the clock has settled.
        WarmUp(() => RenderFullSurface(scene, target, targetContext, clearingBeginFrame: true));
        WarmUp(() => RenderFullSurface(scene, target, targetContext, clearingBeginFrame: false));

        var fullPreserved = Measure(() => RenderFullSurface(scene, target, targetContext, clearingBeginFrame: false));
        var fullClearing = Measure(() => RenderFullSurface(scene, target, targetContext, clearingBeginFrame: true));
        var emptyFrame = Measure(() => RenderEmptyFrame(target, targetContext));
        var switchCost = MeasureTargetSwitch(factory, target, targetContext);

        report.AppendLine(CultureInfo.InvariantCulture,
            $"  fixed cost us p50/p95: fullReplay(preserving BeginFrame)={fullPreserved} " +
            $"fullReplay(clearing BeginFrame)={fullClearing} emptyFrame={emptyFrame} extraTargetSwitch={switchCost}");

        report.AppendLine("  erase only (no replay), whole surface:");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    Clear(preserving)={Measure(() => RenderEraseOnly(target, targetContext, null, true))} " +
            $"Clear(clearing)={Measure(() => RenderEraseOnly(target, targetContext, null, false))} " +
            $"ClearRectangle(whole)={(canRepaintPart ? Measure(() => RenderEraseOnly(target, targetContext, new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT), true)) : Samples.Missing)}");

        if (canRepaintPart)
        {
            report.AppendLine("  erase only, ClearRectangle by ratio:");
            foreach (double ratio in _damageRatios)
            {
                var box = DamageRect(ratio);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {ratio,5:0.00} | {(long)box.Width * (long)box.Height,10} px | " +
                    $"{Measure(() => RenderEraseOnly(target, targetContext, box, true))}");
            }
        }

        report.AppendLine("  ratio | damaged px | damage us p50/p95 | isolation us p50/p95 | " +
            "isolation(miss) us p50/p95 | frameCopy us p50/p95");

        foreach (double ratio in _damageRatios)
        {
            var damage = DamageRect(ratio);
            long damagedPixels = (long)damage.Width * (long)damage.Height;

            var direct = canRepaintPart
                ? Measure(() => RenderDamageDirect(scene, target, targetContext, damage))
                : Samples.Missing;
            var isolationHit = canRepaintPart
                ? MeasureWithIsolationSurface(factory, scene, target, targetContext, damage, recreateEveryFrame: false)
                : Samples.Missing;
            var isolationMiss = canRepaintPart
                ? MeasureWithIsolationSurface(factory, scene, target, targetContext, damage, recreateEveryFrame: true)
                : Samples.Missing;
            var frameCopy = MeasureFrameSurfaceCopy(factory, scene, target, targetContext, damage);

            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {ratio,5:0.00} | {damagedPixels,10} | {direct} | {isolationHit} | {isolationMiss} | {frameCopy}");
        }

        report.AppendLine("  region merging (8 boxes holding the ratio between them):");
        report.AppendLine("  ratio | scattered union px | scattered split us | scattered merged us | " +
            "clustered union px | clustered split us | clustered merged us");

        foreach (double ratio in _damageRatios)
        {
            var scattered = SplitRegions(ratio, clustered: false);
            var clustered = SplitRegions(ratio, clustered: true);
            var scatteredUnion = Union(scattered);
            var clusteredUnion = Union(clustered);

            var scatteredSplit = canRepaintPart
                ? Measure(() => RenderDamageRegions(scene, target, targetContext, scattered))
                : Samples.Missing;
            var scatteredMerged = canRepaintPart
                ? Measure(() => RenderDamageRegions(scene, target, targetContext, [scatteredUnion]))
                : Samples.Missing;
            var clusteredSplit = canRepaintPart
                ? Measure(() => RenderDamageRegions(scene, target, targetContext, clustered))
                : Samples.Missing;
            var clusteredMerged = canRepaintPart
                ? Measure(() => RenderDamageRegions(scene, target, targetContext, [clusteredUnion]))
                : Samples.Missing;

            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {ratio,5:0.00} | {(long)scatteredUnion.Width * (long)scatteredUnion.Height,10} | " +
                $"{scatteredSplit} | {scatteredMerged} | " +
                $"{(long)clusteredUnion.Width * (long)clusteredUnion.Height,10} | " +
                $"{clusteredSplit} | {clusteredMerged}");
        }

        MeasureComposite(report, factory, target, targetContext);
    }

    /// <summary>Measures the image composite alone, which is the cost a cache or isolation surface adds.</summary>
    private static void MeasureComposite(
        StringBuilder report,
        IGraphicsFactory factory,
        IRenderSurface target,
        IGraphicsContext targetContext)
    {
        report.AppendLine("  composite only (DrawImage of a same-size source):");
        report.AppendLine("  ratio | opaque us p50/p95 | alpha us p50/p95");

        foreach (double ratio in _damageRatios)
        {
            var damage = DamageRect(ratio);
            var opaque = MeasureCompositeOnce(factory, target, targetContext, damage, hasAlpha: false);
            var alpha = MeasureCompositeOnce(factory, target, targetContext, damage, hasAlpha: true);
            report.AppendLine(CultureInfo.InvariantCulture, $"  {ratio,5:0.00} | {opaque} | {alpha}");
        }
    }

    private static Samples MeasureCompositeOnce(
        IGraphicsFactory factory,
        IRenderSurface target,
        IGraphicsContext targetContext,
        Rect damage,
        bool hasAlpha)
    {
        IRenderSurface? source = null;
        try
        {
            source = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
                (int)damage.Width,
                (int)damage.Height,
                1.0,
                "damage-cost-composite",
                hasAlpha));

            using (var sourceContext = factory.CreateContext(source))
            {
                sourceContext.BeginFrame(source);
                sourceContext.Clear(hasAlpha ? Color.FromArgb(128, 40, 90, 160) : Color.FromArgb(255, 40, 90, 160));
                sourceContext.EndFrame();
            }

            var readySource = source;
            return Measure(() =>
            {
                var view = factory.CreateImageView(readySource);
                try
                {
                    targetContext.BeginFrame(target);
                    targetContext.DrawImage(view, damage);
                    targetContext.EndFrame();
                }
                finally
                {
                    view.Dispose();
                }
            });
        }
        catch (Exception error)
        {
            return Samples.Failed(error);
        }
        finally
        {
            source?.Dispose();
        }
    }

    private static void RenderFullSurface(
        RenderScene scene,
        IRenderSurface target,
        IGraphicsContext context,
        bool clearingBeginFrame)
    {
        SetPreserve(target, !clearingBeginFrame);
        context.BeginFrame(target);
        context.Clear(_background);
        FrameRenderer.Replay(scene, context);
        context.EndFrame();
    }

    /// <summary>Frames that only erase, so the erase cost is told apart from the replay cost.</summary>
    private static void RenderEraseOnly(
        IRenderSurface target,
        IGraphicsContext context,
        Rect? rectangle,
        bool preserving)
    {
        SetPreserve(target, preserving);
        context.BeginFrame(target);
        if (rectangle == null)
        {
            context.Clear(_background);
        }
        else
        {
            ((IOpaqueDamageContext)context).ClearRectangle(rectangle.Value, _background);
        }

        context.EndFrame();
    }

    private static void RenderEmptyFrame(IRenderSurface target, IGraphicsContext context)
    {
        SetPreserve(target, true);
        context.BeginFrame(target);
        context.EndFrame();
    }

    /// <summary>
    /// Measures what one extra target switch costs per frame: a frame that begins and ends on a
    /// second surface before the target's own frame, with nothing drawn on either.
    /// </summary>
    private static Samples MeasureTargetSwitch(
        IGraphicsFactory factory,
        IRenderSurface target,
        IGraphicsContext targetContext)
    {
        IRenderSurface? other = null;
        IGraphicsContext? otherContext = null;
        try
        {
            other = CreateIsolationSurface(factory, new Rect(0, 0, 256, 256));
            otherContext = factory.CreateContext(other);
            var readyOther = other;
            var readyOtherContext = otherContext;

            var both = Measure(() =>
            {
                readyOtherContext.BeginFrame(readyOther);
                readyOtherContext.EndFrame();
                RenderEmptyFrame(target, targetContext);
            });
            var single = Measure(() => RenderEmptyFrame(target, targetContext));
            return new Samples(
                Math.Max(0, both.Median - single.Median),
                Math.Max(0, both.P95 - single.P95),
                null);
        }
        catch (Exception error)
        {
            return Samples.Failed(error);
        }
        finally
        {
            otherContext?.Dispose();
            other?.Dispose();
        }
    }

    private static void RenderDamageDirect(
        RenderScene scene,
        IRenderSurface target,
        IGraphicsContext context,
        Rect damage)
    {
        SetPreserve(target, true);
        context.BeginFrame(target);
        ((IOpaqueDamageContext)context).ClearRectangle(damage, _background);
        FrameRenderer.Replay(scene, context, damage);
        context.EndFrame();
    }

    private static void RenderDamageRegions(
        RenderScene scene,
        IRenderSurface target,
        IGraphicsContext context,
        Rect[] regions)
    {
        SetPreserve(target, true);
        context.BeginFrame(target);
        var damageContext = (IOpaqueDamageContext)context;
        for (int index = 0; index < regions.Length; index++)
        {
            damageContext.ClearRectangle(regions[index], _background);
            FrameRenderer.Replay(scene, context, regions[index]);
        }

        context.EndFrame();
    }

    private static Samples MeasureWithIsolationSurface(
        IGraphicsFactory factory,
        RenderScene scene,
        IRenderSurface target,
        IGraphicsContext targetContext,
        Rect damage,
        bool recreateEveryFrame)
    {
        IRenderSurface? shared = null;
        IGraphicsContext? sharedContext = null;
        try
        {
            if (!recreateEveryFrame)
            {
                shared = CreateIsolationSurface(factory, damage);
                sharedContext = factory.CreateContext(shared);
            }

            return Measure(() =>
            {
                var isolation = shared ?? CreateIsolationSurface(factory, damage);
                var isolationContext = sharedContext ?? factory.CreateContext(isolation);
                try
                {
                    isolationContext.BeginFrame(isolation);
                    isolationContext.Clear(_background);
                    isolationContext.SetTransform(
                        Matrix3x2.CreateTranslation((float)-damage.X, (float)-damage.Y));
                    FrameRenderer.Replay(scene, isolationContext, damage);
                    isolationContext.SetTransform(Matrix3x2.Identity);
                    isolationContext.EndFrame();

                    var view = factory.CreateImageView(isolation);
                    try
                    {
                        SetPreserve(target, true);
                        targetContext.BeginFrame(target);
                        targetContext.DrawImage(view, damage);
                        targetContext.EndFrame();
                    }
                    finally
                    {
                        view.Dispose();
                    }
                }
                finally
                {
                    if (shared == null)
                    {
                        isolationContext.Dispose();
                        isolation.Dispose();
                    }
                }
            });
        }
        catch (Exception error)
        {
            return Samples.Failed(error);
        }
        finally
        {
            sharedContext?.Dispose();
            shared?.Dispose();
        }
    }

    /// <summary>
    /// Measures the window path that repaints part of a persistent frame surface and then copies the
    /// whole surface to a target that does not keep its contents.
    /// </summary>
    private static Samples MeasureFrameSurfaceCopy(
        IGraphicsFactory factory,
        RenderScene scene,
        IRenderSurface target,
        IGraphicsContext targetContext,
        Rect damage)
    {
        IRenderSurface? frameSurface = null;
        IGraphicsContext? frameContext = null;
        try
        {
            frameSurface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
                SURFACE_WIDTH,
                SURFACE_HEIGHT,
                1.0,
                "damage-cost-frame",
                hasAlpha: false));

            if (frameSurface is not IPersistentFrameSurface persistentFrame)
            {
                return Samples.Unavailable;
            }

            frameContext = factory.CreateContext(frameSurface);
            frameContext.BeginFrame(frameSurface);
            frameContext.Clear(_background);
            FrameRenderer.Replay(scene, frameContext);
            frameContext.EndFrame();
            persistentFrame.PreserveContentsOnBeginFrame = true;

            if (frameContext is not IOpaqueDamageContext damageContext)
            {
                return Samples.Unavailable;
            }

            var readySurface = frameSurface;
            var readyContext = frameContext;
            return Measure(() =>
            {
                readyContext.BeginFrame(readySurface);
                damageContext.ClearRectangle(damage, _background);
                FrameRenderer.Replay(scene, readyContext, damage);
                readyContext.EndFrame();

                var view = factory.CreateImageView(readySurface);
                try
                {
                    SetPreserve(target, false);
                    targetContext.BeginFrame(target);
                    targetContext.DrawImage(view, new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
                    targetContext.EndFrame();
                }
                finally
                {
                    view.Dispose();
                }
            });
        }
        catch (Exception error)
        {
            return Samples.Failed(error);
        }
        finally
        {
            frameContext?.Dispose();
            frameSurface?.Dispose();
        }
    }

    private static IRenderSurface CreateIsolationSurface(IGraphicsFactory factory, Rect damage)
        => factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
            (int)damage.Width,
            (int)damage.Height,
            1.0,
            "damage-cost-isolation",
            hasAlpha: false));

    private static void SetPreserve(IRenderSurface surface, bool preserve)
    {
        if (surface is IPersistentFrameSurface persistent)
        {
            persistent.PreserveContentsOnBeginFrame = preserve;
        }
    }

    private static void WarmUp(Action frame)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < WARMUP_MILLISECONDS)
        {
            frame();
        }
    }

    private static Samples Measure(Action frame)
    {
        for (int index = 0; index < WARMUP_FRAMES; index++)
        {
            frame();
        }

        var elapsed = new double[MEASURED_FRAMES];
        var watch = new Stopwatch();
        for (int index = 0; index < MEASURED_FRAMES; index++)
        {
            watch.Restart();
            frame();
            watch.Stop();
            elapsed[index] = watch.Elapsed.TotalMilliseconds * 1000;
        }

        Array.Sort(elapsed);
        return new Samples(
            elapsed[elapsed.Length / 2],
            elapsed[(int)(elapsed.Length * 0.95)],
            null);
    }

    private readonly record struct Samples(double Median, double P95, string? Note)
    {
        internal static Samples Missing { get; } = new(0, 0, "n/a");

        internal static Samples Unavailable { get; } = new(0, 0, "unsupported");

        internal static Samples Failed(Exception error) => new(0, 0, error.GetType().Name);

        public override string ToString()
            => Note ?? string.Create(CultureInfo.InvariantCulture, $"{Median,8:0.0}/{P95,8:0.0}");
    }

    private static Rect DamageRect(double ratio)
    {
        double scale = Math.Sqrt(ratio);
        int width = Math.Max(1, (int)Math.Round(SURFACE_WIDTH * scale));
        int height = Math.Max(1, (int)Math.Round(SURFACE_HEIGHT * scale));
        int left = (SURFACE_WIDTH - width) / 2;
        int top = (SURFACE_HEIGHT - height) / 2;
        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// Spreads the damaged area over several boxes, which is what merging has to beat. Scattered
    /// boxes sit in the four quadrants, clustered boxes sit half a box apart.
    /// </summary>
    private static Rect[] SplitRegions(double ratio, bool clustered)
    {
        double perRegion = ratio / SPLIT_REGION_COUNT;
        double scale = Math.Sqrt(perRegion);
        int width = Math.Max(1, (int)Math.Round(SURFACE_WIDTH * scale));
        int height = Math.Max(1, (int)Math.Round(SURFACE_HEIGHT * scale));
        int stepX = clustered ? (int)(width * 1.5) : SURFACE_WIDTH / 4;
        int stepY = clustered ? (int)(height * 1.5) : SURFACE_HEIGHT / 2;
        int originX = clustered ? Math.Max(0, (SURFACE_WIDTH - (stepX * 3) - width) / 2) : 16;
        int originY = clustered ? Math.Max(0, (SURFACE_HEIGHT - stepY - height) / 2) : 16;

        var regions = new Rect[SPLIT_REGION_COUNT];
        for (int index = 0; index < SPLIT_REGION_COUNT; index++)
        {
            int column = index % 4;
            int row = index / 4;
            int left = Math.Min(SURFACE_WIDTH - width, originX + (column * stepX));
            int top = Math.Min(SURFACE_HEIGHT - height, originY + (row * stepY));
            regions[index] = new Rect(left, top, width, height);
        }

        return regions;
    }

    private static Rect Union(Rect[] regions)
    {
        double left = double.MaxValue;
        double top = double.MaxValue;
        double right = double.MinValue;
        double bottom = double.MinValue;
        for (int index = 0; index < regions.Length; index++)
        {
            left = Math.Min(left, regions[index].X);
            top = Math.Min(top, regions[index].Y);
            right = Math.Max(right, regions[index].Right);
            bottom = Math.Max(bottom, regions[index].Bottom);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Grows the box by <paramref name="pixels"/> on every side, clamped to the surface.</summary>
    private static Rect Inflate(Rect rect, int pixels)
    {
        if (pixels == 0)
        {
            return rect;
        }

        double left = Math.Max(0, rect.X - pixels);
        double top = Math.Max(0, rect.Y - pixels);
        double right = Math.Min(SURFACE_WIDTH, rect.Right + pixels);
        double bottom = Math.Min(SURFACE_HEIGHT, rect.Bottom + pixels);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect SnapOut(Rect rect)
    {
        double left = Math.Floor(rect.X);
        double top = Math.Floor(rect.Y);
        double right = Math.Ceiling(rect.Right);
        double bottom = Math.Ceiling(rect.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void UpdateAndReplayWhole(
        RenderScene scene,
        SceneCapture capture,
        RenderDirtyRegistry? registry,
        UIElement root,
        IGraphicsContext context)
    {
        var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };
        if (registry == null)
        {
            capture.Capture(scene, root, recorder);
        }
        else
        {
            capture.Capture(scene, root, recorder, registry);
        }

        scene.Statistics.Reset();
        FrameRenderer.Replay(scene, context);
    }

    private static StackPanel BuildScene(List<CostCell> cells)
    {
        int columns = SURFACE_WIDTH / CELL_WIDTH;
        int rows = SURFACE_HEIGHT / CELL_HEIGHT;
        var outer = new StackPanel { Orientation = Orientation.Vertical };
        for (int row = 0; row < rows; row++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var rowCells = new UIElement[columns];
            for (int column = 0; column < columns; column++)
            {
                var cell = new CostCell
                {
                    Fill = Color.FromArgb(
                        255,
                        (byte)(40 + ((column * 9) % 200)),
                        (byte)(60 + ((row * 7) % 180)),
                        (byte)(90 + ((column + row) % 150))),
                };
                cells.Add(cell);
                rowCells[column] = cell;
            }

            line.Children(rowCells);
            outer.Children(line);
        }

        return outer;
    }

    private static void LayoutScene(UIElement root)
    {
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
    }

    private static IRenderSurface CreateSurface(IGraphicsFactory factory, bool hasAlpha)
        => factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
            SURFACE_WIDTH,
            SURFACE_HEIGHT,
            1.0,
            "damage-cost-target",
            hasAlpha));

    private readonly record struct PixelDifference(int DifferingPixels, int MaxChannelDelta, string FirstDifference);

    private static PixelDifference ComparePixels(IRenderSurface partial, IRenderSurface whole)
    {
        var partialPixels = ((ICpuPixelSurface)partial).GetReadOnlyPixelSpan();
        var wholePixels = ((ICpuPixelSurface)whole).GetReadOnlyPixelSpan();
        int partialStride = ((ICpuPixelSurface)partial).StrideBytes;
        int wholeStride = ((ICpuPixelSurface)whole).StrideBytes;

        int differing = 0;
        int maxDelta = 0;
        string first = "none";

        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            for (int column = 0; column < SURFACE_WIDTH; column++)
            {
                int partialOffset = (row * partialStride) + (column * 4);
                int wholeOffset = (row * wholeStride) + (column * 4);
                int pixelDelta = 0;
                for (int channel = 0; channel < 4; channel++)
                {
                    pixelDelta = Math.Max(
                        pixelDelta,
                        Math.Abs(partialPixels[partialOffset + channel] - wholePixels[wholeOffset + channel]));
                }

                if (pixelDelta == 0)
                {
                    continue;
                }

                if (differing == 0)
                {
                    first =
                        $"({column},{row}) partial BGRA=({partialPixels[partialOffset]}," +
                        $"{partialPixels[partialOffset + 1]},{partialPixels[partialOffset + 2]}," +
                        $"{partialPixels[partialOffset + 3]}) full BGRA=({wholePixels[wholeOffset]}," +
                        $"{wholePixels[wholeOffset + 1]},{wholePixels[wholeOffset + 2]}," +
                        $"{wholePixels[wholeOffset + 3]})";
                }

                differing++;
                maxDelta = Math.Max(maxDelta, pixelDelta);
            }
        }

        return new PixelDifference(differing, maxDelta, first);
    }
}
