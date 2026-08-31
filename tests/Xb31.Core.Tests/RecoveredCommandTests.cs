using Xb31.Core;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class RecoveredCommandTests
{
    [TestMethod]
    public void LightingModeReadFrame_MatchesCapturedBytes() =>
        Assert.AreEqual("3e000000000004f2111fff253c", Hex(Xb31Commands.LightingModeReadFrame()));

    [TestMethod]
    public void SoundModeReadFrame_MatchesRecoveredBytes() =>
        Assert.AreEqual("3e00000000000591100fff00b43c", Hex(Xb31Commands.SoundModeReadFrame()));

    [TestMethod]
    [DataRow(SoundMode.Standard, "3e000000000006931000ff0000a83c")]
    [DataRow(SoundMode.ExtraBass, "3e000000000006931001ff0000a93c")]
    [DataRow(SoundMode.LiveSound, "3e000000000006931002ff0000aa3c")]
    public void SoundModeFrame_MatchesRecoveredBytes(SoundMode mode, string expected) =>
        Assert.AreEqual(expected, Hex(Xb31Commands.SoundModeFrame(mode)));

    [TestMethod]
    public void SoundModeFrame_RejectsUnknownMode() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Xb31Commands.SoundModeFrame((SoundMode)0x03));

    [TestMethod]
    public void SoundModes_ExposeRecoveredNamesInWireOrder()
    {
        (string Name, SoundMode Mode)[] expected =
        [
            ("Standard", SoundMode.Standard),
            ("Extra Bass", SoundMode.ExtraBass),
            ("Live Sound", SoundMode.LiveSound)
        ];

        CollectionAssert.AreEqual(
            expected,
            SoundModes.All.Select(option => (option.Name, option.Mode)).ToArray());
        foreach ((string name, SoundMode mode) in expected)
            Assert.AreEqual(name, SoundModes.GetName(mode));
    }

    [TestMethod]
    public void AutoStandbyReadFrame_MatchesRecoveredBytes() =>
        Assert.AreEqual("3e000000000004f2121fff263c", Hex(Xb31Commands.AutoStandbyReadFrame()));

    [TestMethod]
    [DataRow(false, "3e000000000007f4121fff0101002d3c")]
    [DataRow(true, "3e000000000007f4121fff0101012e3c")]
    public void AutoStandbyFrame_MatchesRecoveredBytes(bool isOn, string expected) =>
        Assert.AreEqual(expected, Hex(Xb31Commands.AutoStandbyFrame(isOn)));

    [TestMethod]
    public void AutoStandbyOptions_ExposeOffThenOn()
    {
        (string Name, bool IsOn)[] expected = [("Off", false), ("On", true)];

        CollectionAssert.AreEqual(
            expected,
            AutoStandbyOptions.All.Select(option => (option.Name, option.IsOn)).ToArray());
    }

    [TestMethod]
    public void BatteryLabelReadFrame_MatchesRecoveredBytes() =>
        Assert.AreEqual("3e000000000004f2123fff463c", Hex(Xb31Commands.BatteryLabelReadFrame()));

    [TestMethod]
    public void QueryResultSuccess_CarriesTypedValue()
    {
        Xb31QueryResult<SoundMode> result = Xb31QueryResult<SoundMode>.Success(SoundMode.LiveSound);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(SoundMode.LiveSound, result.Value);
        Assert.IsNull(result.Diagnostic);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
