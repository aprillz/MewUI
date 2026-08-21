using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class GdiTextAdvancesTests
{
    [TestMethod]
    [DataRow("i")]
    [DataRow("ii")]
    [DataRow("iii")]
    [DataRow("iiii")]
    public void GetUtf16PrefixAdvances_PreservesOverlappingBufferValues(string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(144);
        using var font = (GdiFont)factory.CreateFont("Segoe UI", 16, 144);

        var cumulative = context.GetUtf16PrefixAdvances(text, font)!;

        Assert.HasCount(text.Length, cumulative);
        for (int i = 1; i < cumulative.Length; i++)
        {
            Assert.IsGreaterThan(cumulative[i - 1], cumulative[i]);
        }
        Assert.AreEqual(context.Measure(text, font).Width, cumulative[^1], 1.0);
    }

    [TestMethod]
    public void GetUtf16PrefixAdvances_ReturnsMonotonicDrawMetrics()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        const string text = "office 한글 😀";
        using var factory = new GdiGraphicsFactory();
        using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(144);
        using var font = (GdiFont)factory.CreateFont("Segoe UI", 16, 144);

        var cumulative = context.GetUtf16PrefixAdvances(text, font)!;

        Assert.HasCount(text.Length, cumulative);
        Assert.IsGreaterThan(0, cumulative[^1]);
        for (int i = 1; i < cumulative.Length; i++)
        {
            Assert.IsGreaterThanOrEqualTo(cumulative[i - 1], cumulative[i]);
        }

        var measured = context.Measure(text, font);
        Assert.AreEqual(measured.Width, cumulative[^1], 1.0,
            "Prefix extents and the GDI draw measurement must use the same horizontal metric source.");
    }

    [TestMethod]
    public void GetUtf16PrefixAdvances_AllocatesOnlyResultArray()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        string text = new('m', 257);
        using var factory = new GdiGraphicsFactory();
        using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(96);
        using var font = (GdiFont)factory.CreateFont("Segoe UI", 16, 96);

        for (int i = 0; i < 4; i++)
        {
            GC.KeepAlive(context.GetUtf16PrefixAdvances(text, font));
        }
        GC.KeepAlive(GC.AllocateUninitializedArray<double>(text.Length));

        long resultBefore = GC.GetAllocatedBytesForCurrentThread();
        var result = context.GetUtf16PrefixAdvances(text, font)!;
        long resultBytes = GC.GetAllocatedBytesForCurrentThread() - resultBefore;
        GC.KeepAlive(result);

        long arrayBefore = GC.GetAllocatedBytesForCurrentThread();
        var expected = GC.AllocateUninitializedArray<double>(text.Length);
        long arrayBytes = GC.GetAllocatedBytesForCurrentThread() - arrayBefore;
        GC.KeepAlive(expected);

        Assert.AreEqual(arrayBytes, resultBytes,
            "Prefix measurement must allocate only its returned double array.");
    }
}
