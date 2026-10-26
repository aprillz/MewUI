using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Layout;

/// <summary>Panels keep wrapped text inside its box, edges on device pixels and adjacent cells touching at every scale and width.</summary>
[TestClass]
[DoNotParallelize]
public sealed class PanelLayoutTests
{
    [TestMethod]
    [LayoutDpis]
    public void GridEqualStars(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => GridOfTexts("*,*,*", 12),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridWeightedStars(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => GridOfTexts("2*,*", 8),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridAutoAndStar(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = new Grid().Columns("Auto,*").Rows("Auto,Auto,Auto,Auto");
            for (int row = 0; row < 4; row++)
            {
                var label = LayoutTexts.Label($"Label {row}:");
                Grid.SetRow(label, row);
                grid.Add(label);
                var text = LayoutTexts.Wrapped(row);
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 1);
                grid.Add(text);
            }

            return grid;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridPixelAndStars(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => GridOfTexts("97,*,*", 12),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridColumnSpans(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = new Grid().Columns("*,*,*").Rows("Auto,Auto,Auto,Auto");
            var texts = LayoutTexts.Many(8);
            for (int row = 0; row < 4; row++)
            {
                // Even rows span the first two columns, odd rows the last two.
                var left = texts[row * 2];
                var right = texts[row * 2 + 1];
                Grid.SetRow(left, row);
                Grid.SetRow(right, row);
                if (row % 2 == 0)
                {
                    Grid.SetColumnSpan(left, 2);
                    Grid.SetColumn(right, 2);
                }
                else
                {
                    Grid.SetColumn(right, 1);
                    Grid.SetColumnSpan(right, 2);
                }

                grid.Add(left);
                grid.Add(right);
            }

            return grid;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridSpacingAndPadding(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = GridOfTexts("*,*,*", 12);
            grid.Spacing = 3;
            grid.Padding = new Thickness(2.5, 1.5, 3.5, 0.5);
            return grid;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridSharedStars(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = GridOfTexts("*,2*", 8);
            grid.ShareStarSize = true;
            return grid;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridCellsMeet(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = new Grid().Columns("*,*,*,*,*").Rows("*,*,*");
            grid.Height = 100.3;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    var cell = LayoutTexts.Cell(row * 5 + column);
                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, column);
                    grid.Add(cell);
                }
            }

            return grid;
        },
        Meeting = root =>
        {
            var grid = (Grid)root;
            var pairs = new List<MeetingPair>();
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    var cell = grid[row * 5 + column];
                    if (column < 4)
                    {
                        pairs.Add(new MeetingPair(cell, grid[row * 5 + column + 1], Orientation.Horizontal));
                    }

                    if (row < 2)
                    {
                        pairs.Add(new MeetingPair(cell, grid[(row + 1) * 5 + column], Orientation.Vertical));
                    }
                }
            }

            return pairs;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void VerticalStackOfTexts(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => new StackPanel().Vertical().Children(LayoutTexts.Many(10)),
        Meeting = root => Consecutive((Panel)root, Orientation.Vertical),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void VerticalStackWithSpacing(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => new StackPanel().Vertical().Spacing(2.5).Children(LayoutTexts.Many(10)),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void HorizontalStackOfCells(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var stack = new StackPanel().Horizontal();
            stack.Height = 40.7;
            for (int index = 0; index < 8; index++)
            {
                var cell = LayoutTexts.Cell(index);
                cell.Width = 33.3 + index * 0.45;
                stack.Add(cell);
            }

            return stack;
        },
        Meeting = root => Consecutive((Panel)root, Orientation.Horizontal),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void DockPanelSides(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var dock = new DockPanel();
            var texts = LayoutTexts.Many(5);
            Dock[] sides = [Dock.Top, Dock.Left, Dock.Right, Dock.Bottom, Dock.Left];
            for (int index = 0; index < texts.Length; index++)
            {
                DockPanel.SetDock(texts[index], sides[index]);
                dock.Add(texts[index]);
            }

            texts[1].MaxWidth = 61.3;
            texts[2].MaxWidth = 70.7;
            return dock;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void DockPanelLeftRow(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var dock = new DockPanel();
            foreach (var text in LayoutTexts.Many(6))
            {
                DockPanel.SetDock(text, Dock.Left);
                text.MaxWidth = 37.3;
                dock.Add(text);
            }

            return dock;
        },
        Meeting = root => Consecutive((Panel)root, Orientation.Horizontal),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void UniformGridOfTexts(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = new UniformGrid { Columns = 3 };
            foreach (var text in LayoutTexts.Many(12))
            {
                grid.Add(text);
            }

            return grid;
        },
        Meeting = root =>
        {
            var grid = (UniformGrid)root;
            var pairs = new List<MeetingPair>();
            for (int index = 0; index < grid.Count; index++)
            {
                if (index % 3 < 2 && index + 1 < grid.Count)
                {
                    pairs.Add(new MeetingPair(grid[index], grid[index + 1], Orientation.Horizontal));
                }
            }

            return pairs;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void WrapPanelOfTexts(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var wrap = new WrapPanel { ItemWidth = 101.3 };
            foreach (var text in LayoutTexts.Many(10))
            {
                wrap.Add(text);
            }

            return wrap;
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void SplitPanelOfTexts(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => new SplitPanel
        {
            First = new StackPanel().Vertical().Children(LayoutTexts.Many(4)),
            Second = new StackPanel().Vertical().Children(LayoutTexts.Many(4, 4)),
            FirstLength = new GridLength(1, GridUnitType.Star),
            SecondLength = new GridLength(2, GridUnitType.Star),
        },
    }, dpi);

    private static Grid GridOfTexts(string columns, int count)
    {
        int columnCount = columns.Split(',').Length;
        int rowCount = (count + columnCount - 1) / columnCount;
        var grid = new Grid().Columns(columns).Rows(string.Join(",", Enumerable.Repeat("Auto", rowCount)));
        var texts = LayoutTexts.Many(count);
        for (int index = 0; index < texts.Length; index++)
        {
            Grid.SetRow(texts[index], index / columnCount);
            Grid.SetColumn(texts[index], index % columnCount);
            grid.Add(texts[index]);
        }

        return grid;
    }

    private static IEnumerable<MeetingPair> Consecutive(Panel panel, Orientation axis)
    {
        for (int index = 0; index + 1 < panel.Count; index++)
        {
            yield return new MeetingPair(panel[index], panel[index + 1], axis);
        }
    }
}
