// Ported from Windows Terminal src/terminal/parser/ut_parser/Base64Test.cpp (MIT).

using System.Text;

using Aprillz.MewUI.Terminal.Parser;

namespace MewUI.Terminal.Test.Parser;

[TestClass]
public sealed class Base64Tests
{
    [TestMethod]
    public void DecodeFuzz()
    {
        const int TEST_ROUNDS = 8;
        const int REFERENCE_SIZE = 128;
        var random = new Random();

        // ASCII only, since the decoder converts UTF-8 and that conversion is not what is tested here.
        var referenceData = new byte[REFERENCE_SIZE];
        for (int index = 0; index < referenceData.Length; index++)
        {
            referenceData[index] = (byte)(random.Next() & 0x7f);
        }

        for (int round = 0; round < TEST_ROUNDS; round++)
        {
            int referenceLength = random.Next(REFERENCE_SIZE);
            string reference = Encoding.ASCII.GetString(referenceData, 0, referenceLength);

            string encoded = string.Empty;
            if (referenceLength != 0)
            {
                encoded = Convert.ToBase64String(referenceData, 0, referenceLength);
            }

            // Test whether the decoder handles strings with and without trailing "=".
            if (random.Next(2) != 0)
            {
                encoded = encoded.TrimEnd('=');
            }

            // Test whether the decoder handles an empty (default) span.
            ReadOnlySpan<char> encodedSpan = encoded;
            if (encoded.Length == 0 && random.Next(2) != 0)
            {
                encodedSpan = default;
            }

            Assert.IsTrue(Base64.TryDecode(encodedSpan, out string decoded), encoded);
            Assert.AreEqual(reference, decoded);
        }
    }

    [TestMethod]
    public void DecodeUTF8()
    {
        // U+306b U+307b U+3093 U+3054 U+6c49 U+8bed U+d55c U+ad6d
        Assert.IsTrue(Base64.TryDecode("44Gr44G744KT44GU5rGJ6K+t7ZWc6rWt", out string result));
        Assert.AreEqual("にほんご汉语한국", result);

        // U+d83d U+dc4d U+d83d U+dc4d U+d83c U+dffb U+d83d U+dc4d U+d83c U+dffc U+d83d
        // U+dc4d U+d83c U+dffd U+d83d U+dc4d U+d83c U+dffe U+d83d U+dc4d U+d83c U+dfff
        Assert.IsTrue(Base64.TryDecode("8J+RjfCfkY3wn4+78J+RjfCfj7zwn5GN8J+PvfCfkY3wn4++8J+RjfCfj78=", out result));
        Assert.AreEqual(
            "👍👍🏻👍🏼👍🏽👍🏾👍🏿",
            result);
    }
}
