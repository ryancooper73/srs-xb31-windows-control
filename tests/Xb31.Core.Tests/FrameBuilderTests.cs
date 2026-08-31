using Xb31.Core;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class FrameBuilderTests
{
    [TestMethod]
    public void PowerOffFrame_MatchesProvenBytes() =>
        Assert.AreEqual("3e0000000000053000000f00443c", Hex(Xb31Commands.PowerOffFrame()));

    [TestMethod]
    public void LightOffFrame_MatchesKnownBytes() =>
        Assert.AreEqual("3e000000000006f41110ff00001a3c", Hex(Xb31Commands.LightingFrame(LightingMode.LightOff)));

    [TestMethod]
    public void ChillFrame_MatchesKnownBytes() =>
        Assert.AreEqual("3e000000000006f41112ff00001c3c", Hex(Xb31Commands.LightingFrame(LightingMode.Chill)));

    [TestMethod]
    public void LightingCatalog_ContainsEveryDisclosedNameAndByte()
    {
        (string Name, byte Value)[] expected =
        [
            ("Light Off", 0x10), ("Rave", 0x11), ("Chill", 0x12),
            ("Random Flash Off", 0x13), ("Hot", 0x14), ("Cool", 0x15),
            ("Strobe", 0x16), ("Calm Magenta", 0x17), ("Calm Cyan", 0x18),
            ("Calm Lime", 0x19), ("Calm Cinnabar", 0x1A),
            ("Calm Daylight", 0x1B), ("Calm Light Bulb", 0x1C)
        ];

        CollectionAssert.AreEqual(
            expected,
            LightingModes.All.Select(option => (option.Name, (byte)option.Mode)).ToArray());
    }

    [TestMethod]
    public void EveryLightingFrame_UsesLengthPlusPayloadChecksum()
    {
        foreach (LightingOption option in LightingModes.All)
        {
            byte[] frame = Xb31Commands.LightingFrame(option.Mode);
            byte expectedChecksum = unchecked((byte)(0x06 + 0xF4 + 0x11 + (byte)option.Mode + 0xFF));
            Assert.AreEqual(expectedChecksum, frame[^2], option.Name);
            Assert.AreEqual(0x3E, frame[0], option.Name);
            Assert.AreEqual(0x3C, frame[^1], option.Name);
        }
    }

    [TestMethod]
    public void Builder_RejectsEmptyPayload()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Xb31FrameBuilder.Build([]));
    }

    [TestMethod]
    public void Builder_AcceptsPayloadLargerThanOneByteLength() =>
        Assert.HasCount(265, Xb31FrameBuilder.Build(new byte[256]));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
