using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Test;

[TestClass]
public class JpegScaledDecodeTests
{
    // 16x16 solid RGB(200, 150, 100) JPEG. Scaled decoding routes through the reduced
    // 4x4 / 2x2 / 1x1 IDCTs, which once skipped the range-limit center bias and returned
    // all-zero samples (rendered as solid green).
    private const string SOLID_JPEG_BASE64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD1Oiiivyc/TT//2Q==";

    [TestMethod]
    public void ScaledDecodeMatchesFullSizeColor()
    {
        byte[] encoded = Convert.FromBase64String(SOLID_JPEG_BASE64);
        var decoder = new JpegDecoder();

        Assert.IsTrue(decoder.TryDecode(encoded, out var full));
        Assert.AreEqual(16, full.WidthPx);
        AssertSolidColor(full, "full");

        foreach (int target in new[] { 8, 4, 2 })
        {
            Assert.IsTrue(decoder.TryDecode(encoded, target, target, out var scaled));
            Assert.AreEqual(target, scaled.WidthPx, $"target {target}");
            AssertSolidColor(scaled, $"target {target}");
        }
    }

    private static void AssertSolidColor(Bgra32PixelBuffer buffer, string label)
    {
        long sumB = 0, sumG = 0, sumR = 0;
        int pixels = buffer.WidthPx * buffer.HeightPx;
        var data = buffer.Data;
        for (int offset = 0; offset < pixels * 4; offset += 4)
        {
            sumB += data[offset];
            sumG += data[offset + 1];
            sumR += data[offset + 2];
        }

        long avgB = sumB / pixels;
        long avgG = sumG / pixels;
        long avgR = sumR / pixels;
        Assert.IsTrue(
            Math.Abs(avgB - 100) <= 12 && Math.Abs(avgG - 150) <= 12 && Math.Abs(avgR - 200) <= 12,
            $"{label}: avgBGR=({avgB},{avgG},{avgR}) expected ~(100,150,200)");
    }
}
