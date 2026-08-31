using Xb31.Core.Protocol;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class TandemFrameCodecTests
{
    [TestMethod]
    [DataRow((byte)0, "3e010000000000013c")]
    [DataRow((byte)1, "3e010100000000023c")]
    public void EncodeAck_ProducesExactFrame(byte sequence, string expected) =>
        Assert.AreEqual(expected, Hex(TandemFrameCodec.EncodeAck(sequence)));

    [TestMethod]
    public void EncodeData_EscapesReservedPayloadBytes() =>
        Assert.AreEqual(
            "3e0001000000033d2c3d2d3d2ebb3c",
            Hex(TandemFrameCodec.EncodeData([0x3C, 0x3D, 0x3E], 1)));

    [TestMethod]
    public void EncodeData_EscapesReservedChecksum() =>
        Assert.AreEqual(
            "3e0000000000013b3d2c3c",
            Hex(TandemFrameCodec.EncodeData([0x3B], 0)));

    [TestMethod]
    public void EncodeData_WritesFourByteBigEndianLength()
    {
        byte[] frame = TandemFrameCodec.EncodeData(new byte[256], 0);

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x01, 0x00 }, frame[3..7]);
    }

    [TestMethod]
    [DataRow("3e0000000000053000000f00443c", "3000000f00")]
    [DataRow("3e000000000009921002ff0000000000ac3c", "921002ff0000000000")]
    [DataRow("3e000000000009f3121fff00000101012f3c", "f3121fff0000010101")]
    public void Decode_AcceptsIndependentKnownDataFixtures(string encoded, string expectedPayload)
    {
        TandemFrame frame = TandemFrameCodec.Decode(Convert.FromHexString(encoded));

        Assert.AreEqual(TandemFrameType.Data, frame.Type);
        Assert.AreEqual(0, frame.Sequence);
        CollectionAssert.AreEqual(Convert.FromHexString(expectedPayload), frame.Payload);
    }

    [TestMethod]
    [DataRow("00000000000000003c")]
    [DataRow("3e000000000000003e")]
    public void Decode_RejectsInvalidDelimiters(string encoded) =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString(encoded)));

    [TestMethod]
    public void Decode_RejectsInvalidEscapeSequence() =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString("3e0000000000013d003c")));

    [TestMethod]
    public void Decode_RejectsUnknownFrameType() =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString("3e020000000000023c")));

    [TestMethod]
    public void Decode_RejectsSequenceOutsideZeroAndOne() =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString("3e000200000000023c")));

    [TestMethod]
    public void Decode_RejectsDeclaredLengthMismatch() =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString("3e0000000000063000000f00443c")));

    [TestMethod]
    public void Decode_RejectsIncorrectChecksum() =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString("3e0000000000053000000f00453c")));

    [TestMethod]
    public void Decode_RejectsAcknowledgementWithPayload() =>
        Assert.ThrowsExactly<FormatException>(() =>
            TandemFrameCodec.Decode(Convert.FromHexString("3e01000000000100023c")));

    [TestMethod]
    public void EncodeData_RejectsUnescapedInnerOverflow() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TandemFrameCodec.EncodeData(new byte[65_530], 0));

    [TestMethod]
    public void Decode_RejectsUnescapedInnerOverflow()
    {
        byte[] encoded = new byte[65_539];
        encoded[0] = 0x3E;
        encoded[3] = 0x00;
        encoded[4] = 0x00;
        encoded[5] = 0xFF;
        encoded[6] = 0xFA;
        encoded[^2] = 0xF9;
        encoded[^1] = 0x3C;

        Assert.ThrowsExactly<FormatException>(() => TandemFrameCodec.Decode(encoded));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
