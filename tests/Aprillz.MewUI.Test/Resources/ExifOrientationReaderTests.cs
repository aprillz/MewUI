using Aprillz.MewUI.Resources;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class ExifOrientationReaderTests
{
    [DataTestMethod]
    [DataRow(1, ImageOrientation.Normal)]
    [DataRow(2, ImageOrientation.MirrorHorizontal)]
    [DataRow(3, ImageOrientation.Rotate180)]
    [DataRow(4, ImageOrientation.MirrorVertical)]
    [DataRow(5, ImageOrientation.Transpose)]
    [DataRow(6, ImageOrientation.Rotate90)]
    [DataRow(7, ImageOrientation.Transverse)]
    [DataRow(8, ImageOrientation.Rotate270)]
    public void ReadsLittleEndianOrientation(int value, ImageOrientation expected)
    {
        var jpeg = BuildExifJpeg(little: true, 0x0112, type: 3, count: 1, ShortField(value, little: true));

        Assert.AreEqual(expected, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void ReadsBigEndianOrientation()
    {
        var jpeg = BuildExifJpeg(little: false, 0x0112, type: 3, count: 1, ShortField(6, little: false));

        Assert.AreEqual(ImageOrientation.Rotate90, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void NotJpeg_ReturnsNormal()
    {
        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation([1, 2, 3, 4]));
        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void NoExifSegment_ReturnsNormal()
    {
        // SOI, a DQT-style segment, EOI - no APP1.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x04, 0x11, 0x22, 0xFF, 0xD9];

        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void OrientationTagMissing_ReturnsNormal()
    {
        // Exif APP1 carrying a Make tag (0x010F) instead of Orientation.
        var jpeg = BuildExifJpeg(little: true, 0x010F, type: 3, count: 1, ShortField(1, little: true));

        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void WrongType_ReturnsNormal()
    {
        // Orientation tag but type LONG (4) instead of SHORT (3).
        var jpeg = BuildExifJpeg(little: true, 0x0112, type: 4, count: 1, [6, 0, 0, 0]);

        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void OutOfRangeValue_ReturnsNormal()
    {
        var jpeg = BuildExifJpeg(little: true, 0x0112, type: 3, count: 1, ShortField(9, little: true));

        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void BadTiffMagic_ReturnsNormal()
    {
        var jpeg = BuildExifJpeg(little: true, 0x0112, type: 3, count: 1, ShortField(6, little: true));
        // TIFF base = SOI(2) + APP1 marker(2) + length(2) + "Exif\0\0"(6) = 12; magic at +2.
        jpeg[14] = 0x00;
        jpeg[15] = 0x00;

        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void TruncatedSegment_ReturnsNormal()
    {
        var jpeg = BuildExifJpeg(little: true, 0x0112, type: 3, count: 1, ShortField(6, little: true));
        // Declare an APP1 length larger than the available bytes.
        jpeg[4] = 0xFF;
        jpeg[5] = 0xFF;

        Assert.AreEqual(ImageOrientation.Normal, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    [TestMethod]
    public void SkipsNonExifApp1_ThenReadsExif()
    {
        // First APP1 is XMP-like (non-Exif), second is a valid Exif orientation.
        byte[] xmp = [.. "http://ns.adobe.com/xap/1.0/"u8, 0x00, 0x01, 0x02];
        var exif = ExifPayload(little: true, 0x0112, type: 3, count: 1, ShortField(8, little: true));

        var jpeg = WrapJpegApp1s(xmp, exif);

        Assert.AreEqual(ImageOrientation.Rotate270, ExifOrientationReader.ReadJpegOrientation(jpeg));
    }

    // --- builders ---

    private static byte[] ShortField(int value, bool little) =>
        little
            ? [(byte)(value & 0xFF), (byte)(value >> 8), 0, 0]
            : [(byte)(value >> 8), (byte)(value & 0xFF), 0, 0];

    private static byte[] ExifPayload(bool little, ushort tag, ushort type, uint count, byte[] valueField)
    {
        var tiff = new List<byte>();
        tiff.AddRange(little ? (byte[])[0x49, 0x49] : [0x4D, 0x4D]);
        AddU16(tiff, 42, little);
        AddU32(tiff, 8, little);             // IFD0 at offset 8
        AddU16(tiff, 1, little);             // one entry
        AddU16(tiff, tag, little);
        AddU16(tiff, type, little);
        AddU32(tiff, count, little);
        tiff.AddRange(valueField);           // 4-byte value/offset field
        AddU32(tiff, 0, little);             // next IFD = none

        var payload = new List<byte>();
        payload.AddRange("Exif"u8.ToArray());
        payload.Add(0);
        payload.Add(0);
        payload.AddRange(tiff);
        return payload.ToArray();
    }

    private static byte[] BuildExifJpeg(bool little, ushort tag, ushort type, uint count, byte[] valueField) =>
        WrapJpegApp1s(ExifPayload(little, tag, type, count, valueField));

    private static byte[] WrapJpegApp1s(params byte[][] app1Payloads)
    {
        var jpeg = new List<byte> { 0xFF, 0xD8 };   // SOI
        foreach (var payload in app1Payloads)
        {
            jpeg.Add(0xFF);
            jpeg.Add(0xE1);                          // APP1
            int len = payload.Length + 2;
            jpeg.Add((byte)(len >> 8));
            jpeg.Add((byte)(len & 0xFF));            // big-endian length
            jpeg.AddRange(payload);
        }
        jpeg.Add(0xFF);
        jpeg.Add(0xD9);                              // EOI
        return jpeg.ToArray();
    }

    private static void AddU16(List<byte> data, int value, bool little)
    {
        if (little)
        {
            data.Add((byte)(value & 0xFF));
            data.Add((byte)((value >> 8) & 0xFF));
        }
        else
        {
            data.Add((byte)((value >> 8) & 0xFF));
            data.Add((byte)(value & 0xFF));
        }
    }

    private static void AddU32(List<byte> data, uint value, bool little)
    {
        if (little)
        {
            data.Add((byte)(value & 0xFF));
            data.Add((byte)((value >> 8) & 0xFF));
            data.Add((byte)((value >> 16) & 0xFF));
            data.Add((byte)((value >> 24) & 0xFF));
        }
        else
        {
            data.Add((byte)((value >> 24) & 0xFF));
            data.Add((byte)((value >> 16) & 0xFF));
            data.Add((byte)((value >> 8) & 0xFF));
            data.Add((byte)(value & 0xFF));
        }
    }
}
