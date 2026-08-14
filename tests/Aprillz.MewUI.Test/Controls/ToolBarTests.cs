using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Contract coverage for <see cref="ToolBar"/>: what a group materializes, how a band handles what it
/// cannot fit, and where a dragged group lands.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ToolBarTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static Command Cmd(string id) => new(id, id);

    private static ToolBarItem Text(string id)
        => new(Cmd(id)) { Presentation = CommandPresentationMode.Text };

    private static (Window window, ToolBar bar) Host(double width, params ToolBarBand[] bands)
    {
        var window = HeadlessWindow.Create();
        var bar = new ToolBar { Width = width };
        foreach (var band in bands)
        {
            bar.Bands.Add(band);
        }

        window.Content = bar;
        window.PerformLayout();
        return (window, bar);
    }

    /// <summary>The layout as text: one line per band, groups separated by a comma, entries by a plus.</summary>
    private static string Layout(ToolBar bar)
    {
        var lines = bar.Bands.Select(band => string.Join(
            ",",
            band.Groups.Select(group => string.Join(
                "+",
                group.Items.OfType<ToolBarItem>().Select(item => item.Command!.Id.Replace("g", string.Empty))))));

        return string.Join("\n", lines);
    }

    /// <summary>Five bands holding one group each, named 1 to 5.</summary>
    private static (Window window, ToolBar bar) HostFiveBands()
    {
        var bands = new ToolBarBand[5];
        for (int i = 0; i < 5; i++)
        {
            bands[i] = new ToolBarBand(new ToolBarGroup(Text($"g{i + 1}")));
        }

        var hosted = Host(900, bands);
        hosted.bar.CanReorderGroups = true;
        hosted.window.PerformLayout();
        return hosted;
    }

    private static void DragGrip(Window window, ToolBar bar, int band, int group, Point to)
    {
        var grip = bar.VisualsInternal[band].Groups[group].Grip;
        var start = grip.CenterOf();
        window.SendMouseDown(start);
        window.SendMouseMove(new Point(start.X + (to.X > start.X ? 12 : -12), start.Y));
        window.SendMouseMove(to);
        window.SendMouseUp(to);
        window.PerformLayout();
    }

    [TestMethod]
    public void TheFluentApiBuildsTheSameModel()
    {
        if (SkipOnNonWindows()) return;

        var menu = new Menu();
        var bar = new ToolBar()
            .CanReorderGroups()
            .ItemPresentation(CommandPresentationMode.Text)
            .Band(
                new ToolBarGroup()
                    .Item(Cmd("run"))
                    .Splitter()
                    .Toggle(Cmd("wrap"), isChecked: true)
                    .Split(Cmd("save"), menu),
                new ToolBarGroup()
                    .Label("Zoom")
                    .Menu("More", menu)
                    .Host(new Border { Width = 40 }))
            .Band(new ToolBarGroup().Item(Cmd("find")));

        Assert.IsTrue(bar.CanReorderGroups);
        Assert.AreEqual(CommandPresentationMode.Text, bar.ItemPresentation);
        Assert.HasCount(2, bar.Bands);
        Assert.HasCount(2, bar.Bands[0].Groups);

        var entries = bar.Bands[0].Groups[0].Items;
        Assert.IsInstanceOfType<ToolBarItem>(entries[0]);
        Assert.IsInstanceOfType<ToolBarSplitter>(entries[1]);
        Assert.IsTrue(((ToolBarToggleItem)entries[2]).IsChecked);
        Assert.AreSame(menu, ((ToolBarSplitItem)entries[3]).DropDownMenu);

        var second = bar.Bands[0].Groups[1].Items;
        Assert.AreEqual("Zoom", ((ToolBarLabelItem)second[0]).Text);
        Assert.AreEqual("More", ((ToolBarMenuItem)second[1]).Text);
        Assert.IsInstanceOfType<Border>(((ToolBarHost)second[2]).Content);

        // The entry overrides come back typed, so a chain keeps building the entry it started on.
        var item = new ToolBarToggleItem(Cmd("bold")).Presentation(CommandPresentationMode.Icon).IsChecked();
        Assert.AreEqual(CommandPresentationMode.Icon, item.Presentation);
        Assert.IsTrue(item.IsChecked);
    }

    [TestMethod]
    public void EachEntryKind_MaterializesItsOwnControl()
    {
        if (SkipOnNonWindows()) return;

        var group = new ToolBarGroup(
            new ToolBarItem(Cmd("run")),
            new ToolBarToggleItem(Cmd("wrap")),
            new ToolBarSplitItem(Cmd("save")),
            new ToolBarMenuItem { Text = "More" },
            new ToolBarLabelItem("Zoom"),
            new ToolBarHost(new Border { Width = 40, Height = 20 }));

        var (_, bar) = Host(900, new ToolBarBand(group));
        var entries = bar.VisualsInternal[0].Groups[0].Entries;

        Assert.HasCount(6, entries);
        Assert.IsInstanceOfType<Button>(entries[0]);
        Assert.IsInstanceOfType<ToggleButton>(entries[1]);
        Assert.IsInstanceOfType<SplitButton>(entries[2]);
        Assert.IsInstanceOfType<DropDownButton>(entries[3]);
        Assert.IsInstanceOfType<TextBlock>(entries[4]);
        Assert.IsInstanceOfType<Border>(entries[5]);
    }

    [TestMethod]
    public void GroupIsTheMetricAndTheEntrySitsInsideIt()
    {
        if (SkipOnNonWindows()) return;

        var (_, bar) = Host(900, new ToolBarBand(new ToolBarGroup(Text("g1"))));
        var visual = bar.VisualsInternal[0].Groups[0];

        // The plate is a standard-height entry plus its own padding, so it owns no height metric.
        double group = bar.ThemeInternal.Metrics.BaseControlHeight + 4;

        // A band is the group plus 2 of margin either side; the entry is the group less 2 of padding.
        Assert.AreEqual(group, visual.Bounds.Height, "the plate is one group tall");
        Assert.AreEqual(group - 4, visual.Entries[0].Bounds.Height, "and the entry sits inside its padding");
        Assert.AreEqual(group + 4, bar.VisualsInternal[0].Bounds.Height, "the band adds the margin");
    }

    [TestMethod]
    public void EveryIcon_IsTheCommandIconSizeSquare()
    {
        if (SkipOnNonWindows()) return;

        // A template that fills whatever box it is handed, like a shape-based command icon.
        var icon = new IconTemplate(static _ => new Border());
        var group = new ToolBarGroup(
            new ToolBarItem(new Command("a", "A", icon)),
            new ToolBarToggleItem(new Command("b", "B", icon)),
            new ToolBarSplitItem(new Command("c", "C", icon)),
            new ToolBarMenuItem { Text = "D", Icon = icon });

        var (_, bar) = Host(900, new ToolBarBand(group));
        double expected = bar.ThemeInternal.Metrics.CommandIconSize;

        foreach (var entry in bar.VisualsInternal[0].Groups[0].Entries)
        {
            var built = VisualTree.Find(entry, e => e is Border { Width: > 0 });
            Assert.IsNotNull(built, "the entry materialized no icon");
            Assert.AreEqual($"{expected}x{expected}", $"{built.Bounds.Width}x{built.Bounds.Height}");
        }
    }

    [TestMethod]
    public void ASplitterDividesAGroupAndBecomesASeparatorInTheMenu()
    {
        if (SkipOnNonWindows()) return;

        var group = new ToolBarGroup(Text("g1"), new ToolBarSplitter(), Text("g2"), Text("g3"));
        var (window, bar) = Host(900, new ToolBarBand(group));

        var entries = bar.VisualsInternal[0].Groups[0].Entries;
        var rule = ((UIElement)entries[1]).Bounds;

        Assert.IsGreaterThan(0, rule.Width, "the splitter took no room in the group");
        Assert.IsLessThan(((UIElement)entries[0]).Bounds.Width, rule.Width, "the splitter is as wide as an entry");
        Assert.IsLessThan(rule.X, ((UIElement)entries[0]).Bounds.Right, "the splitter is not between the two runs");

        // Narrow enough that the run after the splitter goes to the chevron, splitter included.
        bar.Width = ((UIElement)entries[2]).Bounds.X - bar.Bounds.X;
        window.PerformLayout();

        var visual = bar.VisualsInternal[0].Groups[0];
        Assert.IsTrue(visual.IsTruncated, "the band did not cut the group");

        visual.Chevron.IsDropDownOpen = true;
        var rows = visual.Chevron.DropDownMenu!.Items;

        Assert.IsTrue(rows.Count > 0, "the cut entries did not reach the menu");
        Assert.IsInstanceOfType<MenuItem>(rows[0], "the menu opens on a separator");
    }

    [TestMethod]
    public void ALabelKeepsItsDistanceFromTheEntriesEitherSide()
    {
        if (SkipOnNonWindows()) return;

        var group = new ToolBarGroup(Text("g1"), new ToolBarLabelItem("Zoom"), Text("g2"));
        var (_, bar) = Host(900, new ToolBarBand(group));
        var entries = bar.VisualsInternal[0].Groups[0].Entries;

        var before = ((UIElement)entries[0]).Bounds;
        var label = ((UIElement)entries[1]).Bounds;
        var after = ((UIElement)entries[2]).Bounds;

        // A label has no button face, so the 2 of entry spacing alone reads as though it were glued on.
        Assert.IsGreaterThan(2, label.X - before.Right, "the label sits against the entry before it");
        Assert.IsGreaterThan(2, after.X - label.Right, "the label sits against the entry after it");
    }

    [TestMethod]
    public void ACrowdedBandCollapsesItsOwnGroupsAndKeepsThem()
    {
        if (SkipOnNonWindows()) return;

        var wide = new ToolBarBand(
            new ToolBarGroup(Text("g1")),
            new ToolBarGroup(Text("g2")),
            new ToolBarGroup(Text("g3")),
            new ToolBarGroup(Text("g4")));
        var narrow = new ToolBarBand(new ToolBarGroup(Text("g5")));

        var (_, bar) = Host(90, wide, narrow);

        Assert.IsTrue(bar.VisualsInternal[0].Groups.Any(group => group.IsTruncated), "the crowded band did not collapse");
        Assert.IsFalse(bar.VisualsInternal[1].Groups.Any(group => group.IsTruncated), "the other band is unaffected");

        // Overflow never moves a group to another band: the bands are what the application declared.
        Assert.AreEqual("1,2,3,4\n5", Layout(bar), "the bands still hold their own groups");
    }

    [TestMethod]
    public void ABandTooNarrowForEvenTheMinimumsDropsGroupsFromTheRight()
    {
        if (SkipOnNonWindows()) return;

        var band = new ToolBarBand(
            new ToolBarGroup(Text("g1")),
            new ToolBarGroup(Text("g2")),
            new ToolBarGroup(Text("g3")),
            new ToolBarGroup(Text("g4")));

        // A grip beside a chevron is what a group costs collapsed; four of them do not fit in this.
        var (_, bar) = Host(70, band);
        bar.CanReorderGroups = true;

        var visual = bar.VisualsInternal[0];
        Assert.IsTrue(visual.IsOverflowing, "the band held groups it has no room for");
        Assert.IsFalse(visual.Groups[0].IsHidden, "the leftmost group went before the ones after it");
        Assert.IsTrue(visual.Groups[^1].IsHidden, "the rightmost group stayed while others went");
        Assert.IsGreaterThan(0, visual.Overflow.Bounds.Width, "the band shows no chevron for what it dropped");

        visual.Overflow.IsDropDownOpen = true;
        var rows = visual.Overflow.DropDownMenu!.Items.OfType<MenuItem>().Select(row => row.Command?.Id).ToList();
        Assert.IsTrue(rows.Contains("g4"), "the dropped group is not in the band's menu");
        Assert.IsFalse(rows.Contains("g1"), "a group the band still shows was offered in its menu");
    }

    [TestMethod]
    public void TheGroupTheEdgeFallsInKeepsItsGripAndWhatFits()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(
                new ToolBarGroup(Text("g1")),
                new ToolBarGroup(Text("g2"), Text("g3"), Text("g4"))));
        bar.CanReorderGroups = true;
        window.PerformLayout();

        var band = bar.VisualsInternal[0];
        double full = band.Groups[^1].Bounds.Right;

        // Narrowing a DIP at a time, the second group has to give up its entries one by one before it
        // gives up its plate: the cut is at an entry, not at the group.
        for (double width = full; width > 40; width -= 2)
        {
            bar.Width = width;
            window.PerformLayout();

            if (!band.Groups[1].IsTruncated)
            {
                continue;
            }

            var group = band.Groups[1];
            Assert.IsGreaterThan(0, group.Grip.Bounds.Width, "the truncated group dropped its grip");
            Assert.IsGreaterThan(0, group.VisibleEntryCount, "the truncated group shows nothing at all");
            Assert.AreEqual(
                band.Groups[0].Bounds.Right + 4,
                group.Bounds.X,
                "the truncated group left its place beside the group before it");

            var last = ((UIElement)group.Entries[group.VisibleEntryCount - 1]).Bounds;
            Assert.IsLessThanOrEqualTo(group.Bounds.Right, last.Right, "an entry hangs off its own plate");

            // The chevron belongs to the group it cut, not to the band's right edge.
            var chevron = group.Chevron.Bounds;
            Assert.IsGreaterThanOrEqualTo(group.Bounds.X, chevron.X, "the chevron sits outside the group");
            Assert.IsLessThanOrEqualTo(group.Bounds.Right, chevron.Right, "the chevron sits outside the group");
            Assert.IsLessThanOrEqualTo(chevron.X, last.Right, "the chevron overlaps the last entry");
            Assert.AreEqual(last.Height, chevron.Height, "the chevron is not the height of an entry");
            Assert.IsLessThan(last.Width, chevron.Width, "the chevron is not narrower than an entry");

            Assert.IsLessThan(
                ((UIElement)group.Entries[0]).Bounds.X,
                group.Grip.Bounds.X,
                "the grip is not at the left of what the group still shows");

            group.Chevron.IsDropDownOpen = true;
            var rows = group.Chevron.DropDownMenu!.Items.OfType<MenuItem>().Select(row => row.Command?.Id).ToList();
            Assert.IsFalse(rows.Contains("g2"), "an entry the band still shows was offered again in the menu");
            Assert.IsTrue(rows.Contains("g4"), "the entry the cut removed is not in the menu");
            return;
        }

        Assert.Fail("no width ever cut inside the group");
    }

    [TestMethod]
    public void AGroupWithNoRoomForAnEntryStillShowsItsGrip()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(
                new ToolBarGroup(Text("g1")),
                new ToolBarGroup(Text("g2"), Text("g3"), Text("g4"))));
        bar.CanReorderGroups = true;
        window.PerformLayout();

        var band = bar.VisualsInternal[0];
        double full = band.Groups[^1].Bounds.Right;

        for (double width = full; width > 20; width -= 2)
        {
            bar.Width = width;
            window.PerformLayout();

            var group = band.Groups[1];
            if (group.IsHidden || group.VisibleEntryCount > 0)
            {
                continue;
            }

            // No entry of the group's own fits, but its grip and its chevron do: the group is still there
            // to be dragged, and everything it holds is still one click away.
            Assert.IsGreaterThan(0, group.Grip.Bounds.Width, "the group vanished instead of keeping its grip");
            Assert.IsGreaterThan(0, group.Chevron.Bounds.Width, "the collapsed group has no chevron of its own");
            Assert.IsTrue(group.IsTruncated, "a grip-only group is not reported as truncated");

            group.Chevron.IsDropDownOpen = true;
            var rows = group.Chevron.DropDownMenu!.Items.OfType<MenuItem>().Select(row => row.Command?.Id).ToList();
            Assert.IsTrue(rows.Contains("g2"), "the entries the band gave up are not all in the menu");
            Assert.IsTrue(rows.Contains("g4"), "the entries the band gave up are not all in the menu");
            group.Chevron.IsDropDownOpen = false;

            // Collapsed is not gone: the grip still starts a drag, so the group can be moved to a band
            // where there is room for it.
            DragGrip(window, bar, 0, 1, new Point(band.Bounds.X + 4, band.Bounds.Bottom + 12));
            Assert.AreEqual("1\n2+3+4", Layout(bar), "a collapsed group could not be moved to another band");
            return;
        }

        Assert.Fail("no width left the group with its grip alone");
    }

    [TestMethod]
    public void AGroupTheBandCutRecoversWhenTheRoomComesBack()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(
                new ToolBarGroup(Text("g1")),
                new ToolBarGroup(Text("g2")),
                new ToolBarGroup(Text("g3"))),
            new ToolBarBand(new ToolBarGroup(Text("g4"))));
        bar.CanReorderGroups = true;
        window.PerformLayout();

        double full = bar.VisualsInternal[0].Groups[^1].Bounds.Right;
        bar.Width = full - 20;
        window.PerformLayout();

        Assert.IsTrue(
            bar.VisualsInternal[0].Groups.Any(group => group.IsTruncated),
            "the band was not narrowed enough to cut anything");

        // The first group leaves for the band below, which is room the crowded band gets back.
        var second = bar.VisualsInternal[1].Bounds;
        DragGrip(window, bar, 0, 0, new Point(second.Right - 2, second.Y + (second.Height / 2)));

        Assert.AreEqual("2,3\n4,1", Layout(bar), "the drag did not move the group");
        Assert.IsFalse(
            bar.VisualsInternal[0].Groups.Any(group => group.IsTruncated),
            "the group it cut never grew back");
    }

    [TestMethod]
    public void AGroupTheBandCutRecoversWhenItsWidthComesFromTheWindow()
    {
        if (SkipOnNonWindows()) return;

        // The gallery's toolbar has no Width of its own: it takes the room its parent hands it, which is
        // the case where a stale desired width can hold a group down after the crowding is gone.
        var window = HeadlessWindow.Create(130, 400);
        var bar = new ToolBar { CanReorderGroups = true };
        bar.Bands.Add(new ToolBarBand(
            new ToolBarGroup(Text("g1")),
            new ToolBarGroup(Text("g2")),
            new ToolBarGroup(Text("g3"))));
        bar.Bands.Add(new ToolBarBand(new ToolBarGroup(Text("g4"))));

        window.Content = new StackPanel().Vertical().Children(bar);
        window.PerformLayout();

        Assert.IsTrue(
            bar.VisualsInternal[0].Groups.Any(group => group.IsTruncated || group.IsHidden),
            "the window is not narrow enough to cut anything");

        var second = bar.VisualsInternal[1].Bounds;
        DragGrip(window, bar, 0, 0, new Point(second.Right - 2, second.Y + (second.Height / 2)));

        Assert.AreEqual("2,3\n4,1", Layout(bar), "the drag did not move the group");

        Assert.IsFalse(
            bar.VisualsInternal[0].Groups.Any(group => group.IsTruncated || group.IsHidden),
            "the band held the group down after the room came back");
    }

    [TestMethod]
    public void AGroupCarriesItsFullSelfToTheBandItLandsOn()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(
                new ToolBarGroup(Text("g1")),
                new ToolBarGroup(Text("g2"), Text("g3"), Text("g4"))));
        bar.CanReorderGroups = true;
        window.PerformLayout();

        double full = bar.VisualsInternal[0].Groups[^1].Bounds.Right;
        bar.Width = full - 20;
        window.PerformLayout();
        Assert.IsTrue(bar.VisualsInternal[0].Groups[1].IsTruncated, "the band did not cut the second group");

        // The cut group goes to a band of its own, where nothing is competing for the width.
        var band = bar.VisualsInternal[0].Bounds;
        DragGrip(window, bar, 0, 1, new Point(band.X + 4, band.Bottom + 12));

        Assert.AreEqual("1\n2+3+4", Layout(bar), "the cut group did not move to a band of its own");
        Assert.IsFalse(bar.VisualsInternal[1].Groups[0].IsTruncated, "it stayed cut on a band with room to spare");
        Assert.AreEqual(3, bar.VisualsInternal[1].Groups[0].VisibleEntryCount, "not all of its entries came back");
    }

    [TestMethod]
    public void NarrowingCollapsesOneGroupAfterAnother_NeverGoingBackwards()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(
                new ToolBarGroup(Text("g1"), Text("g2")),
                new ToolBarGroup(Text("g3"), Text("g4"))));
        bar.CanReorderGroups = true;
        window.PerformLayout();

        var band = bar.VisualsInternal[0];
        double full = band.Groups[^1].Bounds.Right;
        int shown = band.Groups.Sum(group => group.VisibleEntryCount);
        Assert.AreEqual(4, shown, "the band did not start with everything visible");

        bool secondGaveUpEverything = false;
        for (double width = full; width > 20; width -= 2)
        {
            bar.Width = width;
            window.PerformLayout();

            int next = band.Groups.Sum(group => group.VisibleEntryCount);
            Assert.IsLessThanOrEqualTo(shown, next, $"narrowing to {width} put an entry back");
            shown = next;

            // Once the second group has nothing of its own left, the first has to start giving entries up
            // too: the collapse moves on to the group before it rather than stopping.
            if (band.Groups[1].IsHidden || band.Groups[1].VisibleEntryCount == 0)
            {
                secondGaveUpEverything = true;
            }

            if (secondGaveUpEverything && band.Groups[0].VisibleEntryCount < 2)
            {
                Assert.IsTrue(band.Groups[0].IsTruncated, "the first group is cut but does not say so");
                return;
            }
        }

        Assert.Fail("the collapse never reached the group before the last one");
    }

    [TestMethod]
    public void EveryGroupKeepsItsGripAndChevronBeforeAnyGroupIsDropped()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(
                new ToolBarGroup(Text("g1"), Text("g2")),
                new ToolBarGroup(Text("g3"), Text("g4")),
                new ToolBarGroup(Text("g5"), Text("g6"))));
        bar.CanReorderGroups = true;
        window.PerformLayout();

        var band = bar.VisualsInternal[0];
        double full = band.Groups[^1].Bounds.Right;

        // The width where the groups can no longer all be whole. Every one of them must still be on the
        // band, because a collapsed group costs a grip and a chevron and nothing more.
        for (double width = full; width > 40; width -= 2)
        {
            bar.Width = width;
            window.PerformLayout();

            if (!band.Groups.Any(group => group.IsTruncated))
            {
                continue;
            }

            Assert.IsFalse(band.Groups.Any(group => group.IsHidden), $"a group was dropped at {width}");
            Assert.IsFalse(band.IsOverflowing, $"the band took a chevron of its own at {width}");

            foreach (var group in band.Groups)
            {
                Assert.IsGreaterThan(0, group.Grip.Bounds.Width, "a group on the band has no grip");
                if (group.IsTruncated)
                {
                    Assert.IsGreaterThan(0, group.Chevron.Bounds.Width, "a cut group has no chevron");
                }
            }

            // The rightmost group gives up its entries first: an application puts what matters on the left.
            Assert.IsTrue(band.Groups[^1].IsTruncated, "the collapse did not start from the right");
            Assert.IsFalse(band.Groups[0].IsTruncated, "the leftmost group gave up entries first");
            return;
        }

        Assert.Fail("no width ever cut a group");
    }

    [TestMethod]
    public void FiveBandsOfOne_TheSecondReachesEveryBand()
    {
        if (SkipOnNonWindows()) return;

        string[] expected =
        [
            "1,2\n3\n4\n5",
            "1\n2\n3\n4\n5",
            "1\n3,2\n4\n5",
            "1\n3\n4,2\n5",
            "1\n3\n4\n5,2",
        ];

        for (int target = 0; target < 5; target++)
        {
            var (window, bar) = HostFiveBands();
            var band = bar.VisualsInternal[target].Bounds;

            // Dropped past that band's group, which asks to join it at the right.
            DragGrip(window, bar, 1, 0, new Point(band.Right - 2, band.Y + (band.Height / 2)));

            Assert.AreEqual(expected[target], Layout(bar), $"dropped on band {target + 1}");
        }
    }

    [TestMethod]
    public void ADropBelowEveryBandOpensANewOne()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = HostFiveBands();
        var last = bar.VisualsInternal[^1].Bounds;

        DragGrip(window, bar, 1, 0, new Point(last.X + 4, last.Bottom + 12));

        Assert.AreEqual("1\n3\n4\n5\n2", Layout(bar), "it opened a band of its own at the end");
        Assert.HasCount(5, bar.Bands, "and the band it emptied is gone");
    }

    [TestMethod]
    public void AimingPastTheLastBandMakesRoomForTheOneItWouldOpen()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = HostFiveBands();
        double idle = bar.DesiredSize.Height;
        double band = bar.VisualsInternal[0].Bounds.Height;
        var last = bar.VisualsInternal[^1].Bounds;

        var grip = bar.VisualsInternal[1].Groups[0].Grip.CenterOf();
        window.SendMouseDown(grip);
        window.SendMouseMove(new Point(grip.X, grip.Y + 12));
        window.SendMouseMove(new Point(last.X + 4, last.Bottom + 8));
        window.PerformLayout();

        Assert.AreEqual(5, bar.DropTargetInternal.Band, "the drop is aimed past the last band");
        Assert.AreEqual(idle + band, bar.DesiredSize.Height, "the toolbar did not ask for the pending band");

        Assert.IsTrue(bar.TryGetDropLine(out var line));
        Assert.IsGreaterThanOrEqualTo(last.Bottom, line.Y, "the mark stands on the row the toolbar opened");
        Assert.IsLessThanOrEqualTo(idle + band, line.Bottom - bar.Bounds.Y, "and inside the room it asked for");

        // Back onto a band that exists, and the room goes away again.
        window.SendMouseMove(new Point(last.X + 4, last.Y + (last.Height / 2)));
        window.PerformLayout();
        Assert.AreEqual(idle, bar.DesiredSize.Height, "the pending band outlived the drop it was for");

        window.SendMouseUp(new Point(last.X + 4, last.Y + (last.Height / 2)));
    }

    [TestMethod]
    public void OnlyTheGripStartsADrag()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = HostFiveBands();
        var entry = (UIElement)bar.VisualsInternal[1].Groups[0].Entries[0];
        var start = entry.CenterOf();

        window.SendMouseDown(start);
        window.SendMouseMove(new Point(start.X + 40, start.Y));

        Assert.IsFalse(bar.IsReordering, "dragging an entry does not move its group");

        window.SendMouseUp(new Point(start.X + 40, start.Y));
        Assert.AreEqual("1\n2\n3\n4\n5", Layout(bar));
    }

    [TestMethod]
    public void WhileDraggingEntriesDoNotTakeThePointer()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = HostFiveBands();
        var entry = (UIElement)bar.VisualsInternal[2].Groups[0].Entries[0];
        var over = entry.CenterOf();

        Assert.AreSame(entry, bar.HitTest(over), "an idle toolbar hands the pointer to its entries");

        var grip = bar.VisualsInternal[1].Groups[0].Grip.CenterOf();
        window.SendMouseDown(grip);
        window.SendMouseMove(new Point(grip.X + 20, grip.Y));

        Assert.IsTrue(bar.IsReordering);
        Assert.AreSame(bar, bar.HitTest(over), "a dragged-over entry must not light up as though pressed");

        window.SendMouseUp(new Point(grip.X + 20, grip.Y));
        Assert.AreSame(entry, bar.HitTest(over), "and it takes the pointer again once the drag ends");
    }

    [TestMethod]
    public void TheMarkStandsWhereTheGroupLands()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = HostFiveBands();
        var target = bar.VisualsInternal[3].Bounds;
        var drop = new Point(target.Right - 2, target.Y + (target.Height / 2));

        var grip = bar.VisualsInternal[1].Groups[0].Grip.CenterOf();
        window.SendMouseDown(grip);
        window.SendMouseMove(new Point(grip.X + 12, grip.Y));
        window.SendMouseMove(drop);

        Assert.IsTrue(bar.TryGetDropLine(out var line), "the drag draws a mark");
        Assert.AreEqual(3, bar.DropTargetInternal.Band, "aimed at the fourth band");
        Assert.IsTrue(line.Y >= target.Y && line.Bottom <= target.Bottom, "and the mark is on that band");

        window.SendMouseUp(drop);
        window.PerformLayout();

        Assert.AreEqual("1\n3\n4,2\n5", Layout(bar), "the group landed on the band the mark stood on");
    }

    [TestMethod]
    public void ReorderingOffMeansNoGripsAndNoDrag()
    {
        if (SkipOnNonWindows()) return;

        var (window, bar) = Host(900,
            new ToolBarBand(new ToolBarGroup(Text("g1"))),
            new ToolBarBand(new ToolBarGroup(Text("g2"))));

        var grip = bar.VisualsInternal[0].Groups[0].Grip;
        Assert.AreEqual(0, grip.Bounds.Width, "a toolbar that does not allow reordering shows no grip");

        var entry = (UIElement)bar.VisualsInternal[1].Groups[0].Entries[0];
        var start = entry.CenterOf();
        window.SendMouseDown(start);
        window.SendMouseMove(new Point(start.X + 40, start.Y));

        Assert.IsFalse(bar.IsReordering);
        Assert.AreEqual("1\n2", Layout(bar));
    }
}
