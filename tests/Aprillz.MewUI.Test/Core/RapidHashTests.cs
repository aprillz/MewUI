using System.Text;

using Aprillz.MewUI;

namespace MewUI.Test.Core;

[TestClass]
public sealed class RapidHashTests
{
    [TestMethod]
    public void SameInputHashesEqual_AcrossEveryLengthBranch()
    {
        for (int length = 0; length <= 300; length++)
        {
            byte[] data = Enumerable.Range(0, length).Select(static index => (byte)(index * 31 + 7)).ToArray();
            Assert.AreEqual(RapidHash.Hash(data), RapidHash.Hash(data.ToArray()), $"length {length}");
            Assert.AreNotEqual(RapidHash.Hash(data), RapidHash.Hash(data, seed: 1), $"seed ignored at length {length}");
        }
    }

    [TestMethod]
    public void DistinctShortInputsDoNotCollide()
    {
        var seen = new HashSet<ulong>();
        for (int value = 0; value < 20_000; value++)
        {
            byte[] data = Encoding.UTF8.GetBytes(value.ToString());
            Assert.IsTrue(seen.Add(RapidHash.Hash(data)), $"collision at {value}");
        }

        for (int length = 0; length <= 300; length++)
        {
            Assert.IsTrue(seen.Add(RapidHash.Hash(new byte[length])), $"zero-filled collision at length {length}");
        }
    }

    [TestMethod]
    public void Builder_IsSensitiveToOrderAndSplit()
    {
        var first = new RapidHashBuilder();
        first.Add("ab");
        first.Add("c");

        var second = new RapidHashBuilder();
        second.Add("a");
        second.Add("bc");

        var third = new RapidHashBuilder();
        third.Add("c");
        third.Add("ab");

        var nullThenEmpty = new RapidHashBuilder();
        nullThenEmpty.Add((string?)null);
        nullThenEmpty.Add(string.Empty);

        var emptyThenNull = new RapidHashBuilder();
        emptyThenNull.Add(string.Empty);
        emptyThenNull.Add((string?)null);

        Assert.AreNotEqual(first.Hash, second.Hash);
        Assert.AreNotEqual(first.Hash, third.Hash);
        Assert.AreNotEqual(nullThenEmpty.Hash, emptyThenNull.Hash);
    }

    [TestMethod]
    public void Builder_ScalarsSurviveZeroValues()
    {
        var withZero = new RapidHashBuilder();
        withZero.Add(17);
        withZero.Add(0.0);
        withZero.Add(false);

        var without = new RapidHashBuilder();
        without.Add(17);

        var zeroOnly = new RapidHashBuilder();
        zeroOnly.Add(0.0);
        zeroOnly.Add(false);

        Assert.AreNotEqual(withZero.Hash, without.Hash);
        Assert.AreNotEqual(withZero.Hash, zeroOnly.Hash);
        Assert.AreNotEqual(0UL, zeroOnly.Hash);
    }
}
