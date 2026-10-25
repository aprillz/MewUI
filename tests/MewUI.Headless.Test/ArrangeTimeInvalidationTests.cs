using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>
/// An element that invalidates its own measure while it is being arranged is measured and arranged again within the
/// same layout, whatever its host measures in its own measure pass.
/// </summary>
[TestClass]
public sealed class ArrangeTimeInvalidationTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;

    [TestMethod]
    public void ItemUnderScrollViewer_IsMeasuredAndArrangedAgain()
    {
        HeadlessTestHost.WithSession(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen }, session =>
        {
            var host = new Host { ItemHeight = 400 };
            var scroll = new ScrollViewer
            {
                VerticalScroll = ScrollMode.Visible,
                HorizontalScroll = ScrollMode.Disabled,
                Content = host,
            };
            var window = Show(session, scroll);
            session.Run(() => window.PerformLayout());

            // Read the counts where the request is made: the session may run the update pass it schedules before the next call.
            var (measures, arranges) = session.Run(() =>
            {
                host.RaiseFromArrange = true;
                host.InvalidateArrange();
                return (host.MeasureCount, host.ArrangeCount);
            });
            session.Run(() => window.PerformLayout());

            Assert.IsFalse(host.RaiseFromArrange, "the item was not arranged");
            Assert.IsGreaterThan(measures, host.MeasureCount, "the request raised during arrange was not measured");
            Assert.IsGreaterThan(arranges + 1, host.ArrangeCount, "the item was not arranged after the measure it asked for");
        });
    }

    [TestMethod]
    public void ItemUnderVariableHeightPresenter_IsMeasuredAndArrangedAgain()
    {
        HeadlessTestHost.WithSession(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen }, session =>
        {
            var hosts = new List<Host>();
            var list = new ItemsControl
            {
                ItemsSource = new ItemsView<int>(Enumerable.Range(0, 40).ToArray()),
                ItemTemplate = new DelegateTemplate<int>(
                    build: _ =>
                    {
                        var host = new Host { ItemHeight = 40 + (hosts.Count % 5) * 30 };
                        hosts.Add(host);
                        return host;
                    },
                    bind: static (_, _, _, _) => { }),
            }.VariableHeightPresenter(ItemsAnchor.Bottom);

            var window = Show(session, list);
            session.Run(() => window.PerformLayout());
            session.Run(() => list.SetScrollOffsets(0, 1_000_000));
            session.Run(() => window.PerformLayout());

            foreach (var host in hosts)
            {
                host.RaiseFromArrange = true;
            }

            int[] measuresBefore = hosts.Select(host => host.MeasureCount).ToArray();
            int[] arrangesBefore = hosts.Select(host => host.ArrangeCount).ToArray();
            session.Run(() => list.SetScrollOffsets(0, Math.Max(0, list.VerticalOffset - 120)));
            session.Run(() => window.PerformLayout());

            int raised = 0;
            int measured = 0;
            int arranged = 0;
            for (int index = 0; index < hosts.Count; index++)
            {
                if (hosts[index].RaiseFromArrange)
                {
                    continue;
                }

                raised++;
                if (hosts[index].MeasureCount > measuresBefore[index])
                {
                    measured++;
                }

                // Once for the arrange that raised the request, once more after the measure it asked for.
                if (hosts[index].ArrangeCount >= arrangesBefore[index] + 2)
                {
                    arranged++;
                }
            }

            Assert.IsGreaterThan(0, raised, "no item raised an invalidation during the scroll step");
            Assert.AreEqual(raised, measured, "items that invalidated their measure while arranged were not measured again");
            Assert.AreEqual(raised, arranged, "items measured again were not arranged again");
        });
    }

    private static Window Show(HeadlessSession session, Element content)
        => session.Run(() =>
        {
            var shown = new Window
            {
                WindowSize = WindowSize.Fixed(WIDTH, HEIGHT),
                Content = content,
            };
            shown.Show();
            return shown;
        });

    private sealed class Host : Control
    {
        internal int MeasureCount;
        internal int ArrangeCount;
        internal bool RaiseFromArrange;
        internal double ItemHeight = 60;

        protected override Size MeasureContent(Size availableSize)
        {
            MeasureCount++;
            return new Size(Math.Max(1, availableSize.Width), ItemHeight);
        }

        protected override void ArrangeContent(Rect finalRect)
        {
            ArrangeCount++;
            if (RaiseFromArrange)
            {
                RaiseFromArrange = false;
                InvalidateMeasure();
            }
        }
    }
}
