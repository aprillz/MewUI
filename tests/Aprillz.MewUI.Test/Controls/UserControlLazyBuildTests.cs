using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// UserControl builds its OnBuild content lazily at the first measure, so subclasses need no
/// constructor Build() call; explicit Build() and externally assigned Content take precedence.
/// </summary>
[TestClass]
public sealed class UserControlLazyBuildTests
{
    private sealed class LazyCard : UserControl
    {
        public int BuildCount { get; private set; }

        protected override Element? OnBuild()
        {
            BuildCount++;
            return new Border { Width = 10, Height = 10 };
        }
    }

    private sealed class EagerCard : UserControl
    {
        public EagerCard()
        {
            Build();
        }

        public int BuildCount { get; private set; }

        protected override Element? OnBuild()
        {
            BuildCount++;
            return new Border { Width = 10, Height = 10 };
        }
    }

    private sealed class EmptyCard : UserControl
    {
        public int BuildCount { get; private set; }

        protected override Element? OnBuild()
        {
            BuildCount++;
            return null;
        }
    }

    [TestMethod]
    public void Measure_BuildsContentLazily_Once()
    {
        var card = new LazyCard();
        Assert.IsNull(card.Content, "content must not exist before the first layout");

        card.Measure(new Size(100, 100));
        Assert.IsNotNull(card.Content);
        Assert.AreEqual(1, card.BuildCount);

        card.InvalidateMeasure();
        card.Measure(new Size(200, 200));
        Assert.AreEqual(1, card.BuildCount, "lazy build must run only once");
    }

    [TestMethod]
    public void ExplicitBuild_SkipsLazyPass()
    {
        var card = new EagerCard();
        Assert.IsNotNull(card.Content, "explicit Build() must populate content immediately");

        card.Measure(new Size(100, 100));
        Assert.AreEqual(1, card.BuildCount, "the lazy pass must not rebuild after an explicit Build()");
    }

    [TestMethod]
    public void ExternalContent_WinsOverOnBuild()
    {
        var external = new Border { Width = 5, Height = 5 };
        var card = new LazyCard { Content = external };

        card.Measure(new Size(100, 100));
        Assert.AreSame(external, card.Content);
        Assert.AreEqual(0, card.BuildCount, "externally assigned content must suppress OnBuild");
    }

    [TestMethod]
    public void NullOnBuild_IsNotRetried()
    {
        var card = new EmptyCard();
        card.Measure(new Size(100, 100));
        card.InvalidateMeasure();
        card.Measure(new Size(200, 200));

        Assert.IsNull(card.Content);
        Assert.AreEqual(1, card.BuildCount, "an intentional null build must not be retried every layout");
    }
}
