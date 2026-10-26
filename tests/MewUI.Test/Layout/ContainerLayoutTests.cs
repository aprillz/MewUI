using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Layout;

/// <summary>Containers, alignment and nesting keep wrapped text inside its box and edges on device pixels at every scale and width.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ContainerLayoutTests
{
    [TestMethod]
    [LayoutDpis]
    public void BordersWithFractionalThickness(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => new Border
        {
            BorderThickness = 1.5,
            Padding = new Thickness(2.5, 1.5, 3.5, 0.5),
            Child = new StackPanel().Vertical().Children(
                LayoutTexts.Wrapped(0),
                new Border { BorderThickness = 1, Padding = new Thickness(3), Child = LayoutTexts.Wrapped(1) },
                LayoutTexts.Wrapped(2)),
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void ScrollViewersWithAndWithoutBars(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => new StackPanel().Vertical().Children(
            new ScrollViewer
            {
                Height = 120.4,
                VerticalScroll = ScrollMode.Auto,
                HorizontalScroll = ScrollMode.Disabled,
                Content = new StackPanel().Vertical().Children(LayoutTexts.Many(10)),
            },
            new ScrollViewer
            {
                VerticalScroll = ScrollMode.Auto,
                HorizontalScroll = ScrollMode.Disabled,
                Padding = new Thickness(9),
                Content = new StackPanel().Vertical().Children(LayoutTexts.Many(2, 10)),
            },
            new ScrollViewer
            {
                Height = 90.6,
                VerticalScroll = ScrollMode.Visible,
                HorizontalScroll = ScrollMode.Disabled,
                Content = new StackPanel().Vertical().Children(LayoutTexts.Many(3, 12)),
            }),
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void ContentControlWithPadding(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () => new ContentControl
        {
            Padding = new Thickness(4.5, 2, 3.25, 2),
            Content = new StackPanel().Vertical().Children(LayoutTexts.Many(3)),
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void AlignedAndSizedChildren(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var texts = LayoutTexts.Many(8);
            texts[0].HorizontalAlignment = HorizontalAlignment.Center;
            texts[1].HorizontalAlignment = HorizontalAlignment.Right;
            texts[2].Width = 100.3;
            texts[2].HorizontalAlignment = HorizontalAlignment.Center;
            texts[3].Width = 150.7;
            texts[3].HorizontalAlignment = HorizontalAlignment.Right;
            texts[4].Margin = new Thickness(0.6, 0.3, 0.45, 0.2);
            texts[5].Margin = new Thickness(3, 2, 5, 1);
            texts[6].MaxWidth = 120.4;
            texts[7].MinWidth = 210.2;
            texts[7].HorizontalAlignment = HorizontalAlignment.Center;
            return new StackPanel().Vertical().Children(texts);
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void NestedGridStackBorder(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var grid = new Grid().Columns("*,*").Rows("Auto,Auto");
            for (int cell = 0; cell < 4; cell++)
            {
                var stack = new StackPanel().Vertical().Spacing(1.5).Children(
                    new Border { Padding = new Thickness(3), BorderThickness = 1, Child = LayoutTexts.Wrapped(cell * 2) },
                    new Border { Padding = new Thickness(2.5, 0, 2.5, 0), Child = LayoutTexts.Wrapped(cell * 2 + 1) });
                Grid.SetRow(stack, cell / 2);
                Grid.SetColumn(stack, cell % 2);
                grid.Add(stack);
            }

            return new Border { Padding = new Thickness(5), Child = grid };
        },
    }, dpi);

    [TestMethod]
    [LayoutDpis]
    public void GridInsideGridStars(uint dpi) => LayoutSweep.Run(new LayoutCase
    {
        Build = () =>
        {
            var outer = new Grid().Columns("*,2*").Rows("Auto");
            var label = new StackPanel().Vertical().Children(LayoutTexts.Many(2, 20));
            outer.Add(label);
            var inner = new Grid().Columns("*,*").Rows("Auto,Auto");
            var texts = LayoutTexts.Many(4);
            for (int index = 0; index < texts.Length; index++)
            {
                Grid.SetRow(texts[index], index / 2);
                Grid.SetColumn(texts[index], index % 2);
                inner.Add(texts[index]);
            }

            Grid.SetColumn(inner, 1);
            outer.Add(inner);
            return outer;
        },
    }, dpi);
}
