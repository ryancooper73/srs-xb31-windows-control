using Xb31.Core;
using Xb31.Core.Protocol;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class ResponseParserTests
{
    [TestMethod]
    [DataRow("f31110ff00000000", LightingMode.LightOff)]
    [DataRow("f31112ff00000000", LightingMode.Chill)]
    [DataRow("f3111cff00000000", LightingMode.CalmLightBulb)]
    public void ParseLightingMode_AcceptsAdvertisedCandidate(string payload, LightingMode expected) =>
        Assert.AreEqual(expected, Xb31ResponseParser.ParseLightingMode(Hex(payload)));

    [TestMethod]
    [DataRow("f21112ff00000000")]
    [DataRow("f31212ff00000000")]
    [DataRow("f3111dff00000000")]
    [DataRow("f31112ff000000")]
    public void ParseLightingMode_RejectsWrongHeaderUnknownCandidateOrTruncation(string payload) =>
        Assert.ThrowsExactly<FormatException>(() => Xb31ResponseParser.ParseLightingMode(Hex(payload)));

    [TestMethod]
    [DataRow("921000ff0000000000", SoundMode.Standard)]
    [DataRow("921001ff1234", SoundMode.ExtraBass)]
    [DataRow("921002ff", SoundMode.LiveSound)]
    public void ParseSoundMode_AcceptsKnownCandidatePrefix(string payload, SoundMode expected) =>
        Assert.AreEqual(expected, Xb31ResponseParser.ParseSoundMode(Hex(payload)));

    [TestMethod]
    [DataRow("931000ff")]
    [DataRow("921100ff")]
    [DataRow("92100000")]
    [DataRow("921003ff")]
    [DataRow("921000")]
    public void ParseSoundMode_RejectsWrongHeaderValueOrTruncation(string payload) =>
        Assert.ThrowsExactly<FormatException>(() => Xb31ResponseParser.ParseSoundMode(Hex(payload)));

    [TestMethod]
    [DataRow("f3121fff0000010100", false)]
    [DataRow("f3121fff0000010101", true)]
    public void ParseAutoStandby_AcceptsBooleanValue(string payload, bool expected) =>
        Assert.AreEqual(expected, Xb31ResponseParser.ParseAutoStandby(Hex(payload)));

    [TestMethod]
    [DataRow("f2121fff0000010100")]
    [DataRow("f3131fff0000010100")]
    [DataRow("f3122fff0000010100")]
    [DataRow("f3121f000000010100")]
    [DataRow("f3121fff0000020100")]
    [DataRow("f3121fff0000010200")]
    [DataRow("f3121fff0000010102")]
    [DataRow("f3121fff00000101")]
    public void ParseAutoStandby_RejectsWrongHeaderTypeLengthValueOrTruncation(string payload) =>
        Assert.ThrowsExactly<FormatException>(() => Xb31ResponseParser.ParseAutoStandby(Hex(payload)));

    [TestMethod]
    public void ParseBatteryLabel_DecodesDeclaredLocalizedUtf8AndTrimsTrailingNulOnly()
    {
        byte[] payload = Hex("f3123fff112299084368617267c3a900aabb");

        Assert.AreEqual("Chargé", Xb31ResponseParser.ParseBatteryLabel(payload));
    }

    [TestMethod]
    public void ParseBatteryLabel_PreservesLeadingWhitespace()
    {
        byte[] payload = Hex("f3123fff00000004204f4b00");

        Assert.AreEqual(" OK", Xb31ResponseParser.ParseBatteryLabel(payload));
    }

    [TestMethod]
    [DataRow("f2123fff0000000141")]
    [DataRow("f3133fff0000000141")]
    [DataRow("f3122fff0000000141")]
    [DataRow("f3123f000000000141")]
    [DataRow("f3123fff0000000241")]
    [DataRow("f3123fff00000000")]
    [DataRow("f3123fff0000000100")]
    [DataRow("f3123fff00000002c328")]
    [DataRow("f3123fff000000")]
    public void ParseBatteryLabel_RejectsWrongHeaderOverflowEmptyInvalidUtf8OrTruncation(string payload) =>
        Assert.ThrowsExactly<FormatException>(() => Xb31ResponseParser.ParseBatteryLabel(Hex(payload)));

    private static byte[] Hex(string value) => Convert.FromHexString(value);
}
