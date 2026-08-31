using Xb31.Core.Protocol;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class TandemFrameParserTests
{
    private static readonly byte[] PowerOffFrame =
        Convert.FromHexString("3e0000000000053000000f00443c");

    private static readonly byte[] SoundFrame =
        Convert.FromHexString("3e000000000009921002ff0000000000ac3c");

    [TestMethod]
    public void Parser_ReassemblesFrameSplitAtEveryInteriorByteBoundary()
    {
        for (int split = 1; split < SoundFrame.Length; split++)
        {
            var parser = new TandemFrameParser();
            parser.Append(SoundFrame.AsSpan(0, split));
            Assert.IsFalse(parser.TryRead(out _), $"split {split} completed early");

            parser.Append(SoundFrame.AsSpan(split));

            Assert.IsTrue(parser.TryRead(out TandemFrame? frame), $"split {split} did not complete");
            CollectionAssert.AreEqual(
                Convert.FromHexString("921002ff0000000000"),
                frame!.Payload,
                $"split {split} payload mismatch");
        }
    }

    [TestMethod]
    public void Parser_ReassemblesFrameFedOneByteAtATime()
    {
        var parser = new TandemFrameParser();

        foreach (byte value in PowerOffFrame)
            parser.Append([value]);

        Assert.IsTrue(parser.TryRead(out TandemFrame? frame));
        CollectionAssert.AreEqual(Convert.FromHexString("3000000f00"), frame!.Payload);
        Assert.IsFalse(parser.TryRead(out _));
    }

    [TestMethod]
    public void Parser_WaitsWhenChunkEndsAfterEscapeByte()
    {
        byte[] encoded = Convert.FromHexString("3e0001000000033d2c3d2d3d2ebb3c");
        var parser = new TandemFrameParser();

        parser.Append(encoded.AsSpan(0, 8));
        Assert.IsFalse(parser.TryRead(out _));

        parser.Append(encoded.AsSpan(8));

        Assert.IsTrue(parser.TryRead(out TandemFrame? frame));
        CollectionAssert.AreEqual(new byte[] { 0x3C, 0x3D, 0x3E }, frame!.Payload);
    }

    [TestMethod]
    public void Parser_QueuesCoalescedAcknowledgementAndDataFrames()
    {
        byte[] ack = Convert.FromHexString("3e010000000000013c");
        byte[] coalesced = [.. ack, .. PowerOffFrame, .. SoundFrame];
        var parser = new TandemFrameParser();

        parser.Append(coalesced);

        Assert.IsTrue(parser.TryRead(out TandemFrame? first));
        Assert.AreEqual(TandemFrameType.Acknowledgement, first!.Type);
        Assert.IsTrue(parser.TryRead(out TandemFrame? second));
        CollectionAssert.AreEqual(Convert.FromHexString("3000000f00"), second!.Payload);
        Assert.IsTrue(parser.TryRead(out TandemFrame? third));
        CollectionAssert.AreEqual(Convert.FromHexString("921002ff0000000000"), third!.Payload);
        Assert.IsFalse(parser.TryRead(out _));
    }

    [TestMethod]
    public void Parser_RejectsNestedStartDelimiter() =>
        Assert.ThrowsExactly<FormatException>(() =>
            new TandemFrameParser().Append(Convert.FromHexString("3e00003e")));

    [TestMethod]
    public void Parser_RejectsMalformedCompletedFrame() =>
        Assert.ThrowsExactly<FormatException>(() =>
            new TandemFrameParser().Append(Convert.FromHexString("3e0000000000013d003c")));

    [TestMethod]
    public void Parser_ResetsAfterRejectingMalformedFrame()
    {
        var parser = new TandemFrameParser();
        Assert.ThrowsExactly<FormatException>(() =>
            parser.Append(Convert.FromHexString("3e00003e")));

        parser.Append(PowerOffFrame);

        Assert.IsTrue(parser.TryRead(out TandemFrame? frame));
        CollectionAssert.AreEqual(Convert.FromHexString("3000000f00"), frame!.Payload);
    }

    [TestMethod]
    public void Parser_RejectsFrameBeyondUnescapedInnerCap()
    {
        var parser = new TandemFrameParser();
        byte[] oversizedPrefix = new byte[65_538];
        oversizedPrefix[0] = 0x3E;

        Assert.ThrowsExactly<FormatException>(() => parser.Append(oversizedPrefix));
    }
}
