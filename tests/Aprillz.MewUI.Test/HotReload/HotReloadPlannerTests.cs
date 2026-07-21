using System.Reflection;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.HotReload;

namespace MewUI.Test.HotReload;

/// <summary>
/// Change classification: the token-normalized hash detects a real body change and the planner
/// emits the role-appropriate reaction once, advancing the epoch so an unrelated later delta
/// does not re-trigger. Simulates "the method changed" by seeding a baseline from a different body
/// (a real Hot Reload IL swap is not reproducible in-process).
/// </summary>
[TestClass]
public sealed class HotReloadPlannerTests
{
    private static int ReturnsOne() => 1;

    private static int ReturnsTwo() => 2;

    private static MethodBase Method(string name)
        => typeof(HotReloadPlannerTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static HotReloadEntry Entry(Element owner, string method, string baselineFrom, HotReloadRole role)
        => new()
        {
            Method = Method(method),
            Role = role,
            Owner = new WeakReference<Element>(owner),
            Baseline = IlNormalizer.Hash(Method(baselineFrom)),
        };

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
    public void Plan_BaselineDiffersFromCurrent_EmitsRebuild()
    {
        var owner = new UserControl();
        var entry = Entry(owner, nameof(ReturnsOne), nameof(ReturnsTwo), HotReloadRole.Build);

        var reactions = HotReloadPlanner.Plan(new List<HotReloadEntry> { entry });

        Assert.HasCount(1, reactions);
        Assert.AreEqual(HotReloadReactionKind.RebuildNode, reactions[0].Kind);
        Assert.AreSame(owner, reactions[0].Owner);
    }

    [TestMethod]
    public void Plan_BaselineMatchesCurrent_NoReaction()
    {
        var owner = new UserControl();
        var entry = Entry(owner, nameof(ReturnsOne), nameof(ReturnsOne), HotReloadRole.Build);

        var reactions = HotReloadPlanner.Plan(new List<HotReloadEntry> { entry });

        Assert.IsEmpty(reactions);
    }

    [TestMethod]
    public void Plan_AdvancesEpoch_SecondPlanHasNoReaction()
    {
        var owner = new UserControl();
        var entry = Entry(owner, nameof(ReturnsOne), nameof(ReturnsTwo), HotReloadRole.Build);
        var entries = new List<HotReloadEntry> { entry };

        Assert.HasCount(1, HotReloadPlanner.Plan(entries));
        Assert.IsEmpty(HotReloadPlanner.Plan(entries));
    }

    [TestMethod]
    public void Plan_TemplateRole_EmitsRefresh()
    {
        var owner = new UserControl();
        var entry = Entry(owner, nameof(ReturnsOne), nameof(ReturnsTwo), HotReloadRole.Template);

        var reactions = HotReloadPlanner.Plan(new List<HotReloadEntry> { entry });

        Assert.HasCount(1, reactions);
        Assert.AreEqual(HotReloadReactionKind.RefreshTemplate, reactions[0].Kind);
    }
}
