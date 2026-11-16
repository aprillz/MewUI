using System.Reflection;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.HotReload;

namespace MewUI.Test.HotReload;

/// <summary>
/// Delta classification: a build reacts when a method it runs changed (its own body, a helper, a
/// constructed control, an override it dispatches to); a framework override only repaints; code only
/// event handlers run needs nothing; anything untraceable re-runs every build. Simulates "the method
/// changed" by seeding a baseline from a different body (a real Hot Reload IL swap is not reproducible
/// in-process).
/// </summary>
[TestClass]
public sealed class HotReloadPlannerTests
{
    private const BindingFlags ANY_STATIC = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    private static int ReturnsOne() => 1;

    private static int ReturnsTwo() => 2;

    private static Element BuildWithHelper() => Helper();

    private static Element Helper() => new TextBlock();

    private static Element BuildWithHandler() => new Button().OnClick(() => Handler());

    private static void Handler()
    {
    }

    private static Element BuildWithControl() => new ProbeControl();

    private static Element BuildWithOverride(ProbePart part) => part.Create();

    private static Element BuildWithRepaintProbe() => new RepaintProbe();

    private static void NeverCalled()
    {
    }

    private class ProbePart
    {
        public virtual Element Create() => new TextBlock();
    }

    private sealed class DerivedProbePart : ProbePart
    {
        public override Element Create() => new Border();
    }

    private sealed class ProbeControl : UserControl
    {
        public ProbeControl()
        {
            Content = new TextBlock();
        }
    }

    private sealed class RepaintProbe : UserControl
    {
        public override void InvalidateVisual() => base.InvalidateVisual();
    }

    private sealed class UnrelatedProbe
    {
    }

    private static MethodBase Method(string name) => typeof(HotReloadPlannerTests).GetMethod(name, ANY_STATIC)!;

    private static HotReloadReachability NewReachability()
        => new(static assembly => assembly == typeof(HotReloadPlannerTests).Assembly);

    private static HotReloadEntry Entry(Element owner, MethodBase method, HotReloadRole role = HotReloadRole.Build)
        => new()
        {
            Method = method,
            Role = role,
            Owner = new WeakReference<Element>(owner),
        };

    private static HotReloadPlan PlanAfterEdit(MethodBase build, MethodBase? edited, Type[]? updatedTypes = null,
        HotReloadRole role = HotReloadRole.Build)
    {
        var reachability = NewReachability();
        reachability.Track(build);
        if (edited != null)
        {
            reachability.SetBaseline(edited, IlNormalizer.Hash(Method(nameof(ReturnsTwo))));
        }
        var changes = reachability.DetectChanges(updatedTypes ?? [edited!.DeclaringType!]);
        return HotReloadPlanner.Plan(new List<HotReloadEntry> { Entry(new UserControl(), build, role) }, changes, reachability);
    }

    [TestMethod]
    public void NormalizedHash_SameMethod_IsDeterministic()
    {
        var first = IlNormalizer.Hash(Method(nameof(ReturnsOne)));
        var second = IlNormalizer.Hash(Method(nameof(ReturnsOne)));

        Assert.IsNotNull(first);
        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    public void NormalizedHash_DifferentBodies_Differ()
    {
        var one = IlNormalizer.Hash(Method(nameof(ReturnsOne)));
        var two = IlNormalizer.Hash(Method(nameof(ReturnsTwo)));

        CollectionAssert.AreNotEqual(one, two);
    }

    [TestMethod]
    public void Plan_BuildBodyChanged_RebuildsThatBuild()
    {
        var plan = PlanAfterEdit(Method(nameof(ReturnsOne)), Method(nameof(ReturnsOne)));

        Assert.HasCount(1, plan.Reactions);
        Assert.AreEqual(HotReloadReactionKind.RebuildNode, plan.Reactions[0].Kind);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_NothingChanged_NoReaction()
    {
        var reachability = NewReachability();
        var build = Method(nameof(BuildWithHelper));
        reachability.Track(build);

        var changes = reachability.DetectChanges([typeof(HotReloadPlannerTests)]);
        var plan = HotReloadPlanner.Plan(new List<HotReloadEntry> { Entry(new UserControl(), build) }, changes, reachability);

        Assert.IsTrue(changes.IsEmpty);
        Assert.IsEmpty(plan.Reactions);
    }

    [TestMethod]
    public void DetectChanges_AdvancesBaseline_SecondDetectionFindsNothing()
    {
        var reachability = NewReachability();
        var build = Method(nameof(ReturnsOne));
        reachability.Track(build);
        reachability.SetBaseline(build, IlNormalizer.Hash(Method(nameof(ReturnsTwo))));

        Assert.HasCount(1, reachability.DetectChanges(null).Methods);
        Assert.IsEmpty(reachability.DetectChanges(null).Methods);
    }

    [TestMethod]
    public void Plan_HelperChanged_RebuildsThatBuild()
    {
        var plan = PlanAfterEdit(Method(nameof(BuildWithHelper)), Method(nameof(Helper)));

        Assert.HasCount(1, plan.Reactions);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_ConstructedControlChanged_RebuildsThatBuild()
    {
        var constructor = typeof(ProbeControl).GetConstructor(Type.EmptyTypes)!;

        var plan = PlanAfterEdit(Method(nameof(BuildWithControl)), constructor);

        Assert.HasCount(1, plan.Reactions);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_OverrideChanged_RebuildsThatBuild()
    {
        var derivedCreate = typeof(DerivedProbePart).GetMethod(nameof(ProbePart.Create))!;

        var plan = PlanAfterEdit(Method(nameof(BuildWithOverride)), derivedCreate);

        Assert.HasCount(1, plan.Reactions);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_HandlerChanged_NeedsNothing()
    {
        var plan = PlanAfterEdit(Method(nameof(BuildWithHandler)), Method(nameof(Handler)));

        Assert.IsEmpty(plan.Reactions);
        Assert.AreEqual(1, plan.HandlerOnlyChanges);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_FrameworkOverrideChanged_RepaintsWithoutRebuild()
    {
        var invalidateVisual = typeof(RepaintProbe).GetMethod(nameof(Element.InvalidateVisual))!;

        var plan = PlanAfterEdit(Method(nameof(BuildWithRepaintProbe)), invalidateVisual);

        Assert.IsEmpty(plan.Reactions);
        CollectionAssert.AreEqual(new[] { typeof(RepaintProbe) }, plan.RepaintTypes);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_UnreachedMethodChanged_RebuildsEveryBuild()
    {
        var plan = PlanAfterEdit(Method(nameof(BuildWithHelper)), Method(nameof(NeverCalled)));

        Assert.HasCount(1, plan.Reactions);
        Assert.IsNotNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_UnrecordedAppTypeChanged_RebuildsEveryBuild()
    {
        var plan = PlanAfterEdit(Method(nameof(BuildWithHelper)), edited: null, [typeof(UnrelatedProbe)]);

        Assert.HasCount(1, plan.Reactions);
        Assert.IsNotNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_TypeOutsideAppCodeChanged_NoReaction()
    {
        var plan = PlanAfterEdit(Method(nameof(BuildWithHelper)), Method(nameof(Helper)), [typeof(string)]);

        Assert.IsEmpty(plan.Reactions);
        Assert.IsNull(plan.RebuildAllReason);
    }

    [TestMethod]
    public void Plan_TemplateRole_EmitsRefresh()
    {
        var plan = PlanAfterEdit(Method(nameof(ReturnsOne)), Method(nameof(ReturnsOne)), role: HotReloadRole.Template);

        Assert.HasCount(1, plan.Reactions);
        Assert.AreEqual(HotReloadReactionKind.RefreshTemplate, plan.Reactions[0].Kind);
    }
}
