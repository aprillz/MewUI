using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Controls;

/// <summary>
/// A star column takes its share of the final width in arrange, so the offered width must not reach
/// the desired size. A WPF DataGrid with the same columns reports the same width whether it is
/// measured against a finite width or an unbounded one.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GridViewStarWidthMeasureTests
{
    private const double OFFERED_WIDTH = 320;

    private sealed record Row(int Id, string Name);

    private static readonly Row[] _rows =
    [
        new(1, "User 01"),
        new(2, "User 02"),
    ];

    [TestMethod]
    public void StarColumnDoesNotClaimTheOfferedWidth()
    {
        RunWithGdi(() =>
        {
            double bounded = MeasureWidth(minWidth: 100, OFFERED_WIDTH);
            double unbounded = MeasureWidth(minWidth: 100, double.PositiveInfinity);

            Assert.AreEqual(unbounded, bounded, 0.5,
                $"The offered width reached the desired size: bounded={bounded}, unbounded={unbounded}.");
        });
    }

    [TestMethod]
    public void StarColumnKeepsItsMinimumWhenNothingPushesItWider()
    {
        RunWithGdi(() =>
        {
            double narrow = MeasureWidth(minWidth: 60, OFFERED_WIDTH);
            double wide = MeasureWidth(minWidth: 100, OFFERED_WIDTH);

            Assert.AreEqual(40, wide - narrow, 0.5,
                $"The star minimum did not carry into the desired width: narrow={narrow}, wide={wide}.");
        });
    }

    private static double MeasureWidth(double minWidth, double offeredWidth)
    {
        var view = CreateGridView(minWidth);
        view.Measure(new Size(offeredWidth, double.PositiveInfinity));
        return view.DesiredSize.Width;
    }

    private static GridView CreateGridView(double minWidth)
        => new GridView()
            .ItemsSource(_rows)
            .Columns(
                new GridViewColumn<Row>().Header("#").Width(44).Text(row => row.Id.ToString()),
                new GridViewColumn<Row>().Header("Name").StarWidth(minWidth: minWidth).Text(row => row.Name));

    private static void RunWithGdi(Action body)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            body();
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
