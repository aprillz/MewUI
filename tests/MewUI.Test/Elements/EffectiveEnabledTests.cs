using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Elements;

/// <summary>
/// <see cref="UIElement.IsEnabled"/> is the author's intent and is never rewritten by an ancestor;
/// <see cref="UIElement.IsEffectivelyEnabled"/> is what the tree resolved. The second is a read-only
/// MewProperty rather than a computed getter so that it can be bound and observed, which means it has
/// to be pushed at every point the resolution can change: own value, ancestor value, attach, detach,
/// and a re-queried suggestion.
/// </summary>
[TestClass]
public sealed class EffectiveEnabledTests
{
    private static readonly Color ENABLED = Color.FromRgb(0, 255, 0);
    private static readonly Color DISABLED = Color.FromRgb(255, 0, 0);

    private sealed class GatedPanel : StackPanel
    {
        private bool _gate = true;

        public bool Gate
        {
            get => _gate;
            set
            {
                _gate = value;
                RefreshEnabledSubtree();
            }
        }

        protected override bool ComputeIsEnabledSuggestion() => _gate;
    }

    private sealed class CountingControl : Control
    {
        public int Count { get; private set; }

        protected override void OnEnabledChanged() => Count++;
    }

    private static StackPanel Parent(Element child)
    {
        var parent = new StackPanel();
        parent.Add(child);
        return parent;
    }

    [TestMethod]
    public void DetachedElement_ReportsItsOwnState()
    {
        Assert.IsTrue(new Border().IsEffectivelyEnabled);
        Assert.IsFalse(new Border { IsEnabled = false }.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void AttachingIntoDisabledParent_DisablesChild()
    {
        var child = new Border();
        var parent = new StackPanel { IsEnabled = false };

        parent.Add(child);

        Assert.IsFalse(child.IsEffectivelyEnabled);
        Assert.IsTrue(child.IsEnabled, "the ancestor must not rewrite the child's own value");
    }

    [TestMethod]
    public void DisablingAncestor_ReachesGrandchild()
    {
        var grandchild = new Border();
        var root = Parent(Parent(grandchild));

        root.IsEnabled = false;

        Assert.IsFalse(grandchild.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void ReEnablingAncestor_RestoresDescendants()
    {
        var grandchild = new Border();
        var root = Parent(Parent(grandchild));

        root.IsEnabled = false;
        root.IsEnabled = true;

        Assert.IsTrue(grandchild.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void OwnIntent_SurvivesAnAncestorRoundTrip()
    {
        // The reason the two axes stay separate: a single coerced value would lose this.
        var child = new Border { IsEnabled = false };
        var root = Parent(child);

        root.IsEnabled = false;
        root.IsEnabled = true;

        Assert.IsFalse(child.IsEnabled);
        Assert.IsFalse(child.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void DetachingFromDisabledParent_RestoresChild()
    {
        var child = new Border();
        var parent = new StackPanel { IsEnabled = false };
        parent.Add(child);

        parent.Remove(child);

        Assert.IsTrue(child.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void ChangedSuggestion_ReachesDescendants()
    {
        var child = new Border();
        var gated = new GatedPanel();
        gated.Add(child);

        gated.Gate = false;

        Assert.IsFalse(gated.IsEffectivelyEnabled);
        Assert.IsFalse(child.IsEffectivelyEnabled, "a suggestion that disables an element disables its subtree");
    }

    [TestMethod]
    public void Binding_FollowsEffectiveEnabled()
    {
        var child = new Border();
        child.Bind(Control.BorderBrushProperty, child, UIElement.IsEffectivelyEnabledProperty,
            (bool enabled) => enabled ? ENABLED : DISABLED);
        var root = Parent(child);

        Assert.AreEqual(ENABLED, child.BorderBrush);

        root.IsEnabled = false;
        Assert.AreEqual(DISABLED, child.BorderBrush);

        root.IsEnabled = true;
        Assert.AreEqual(ENABLED, child.BorderBrush);
    }

    [TestMethod]
    public void EnabledChanged_RaisedOnEffectiveTransitionsOnly()
    {
        var child = new CountingControl();
        var root = Parent(child);

        root.IsEnabled = false;
        Assert.AreEqual(1, child.Count);

        child.IsEnabled = false;
        Assert.AreEqual(1, child.Count, "already effectively disabled: nothing observable changed");

        root.IsEnabled = true;
        Assert.AreEqual(1, child.Count, "the child's own value still holds it disabled");
    }
}
