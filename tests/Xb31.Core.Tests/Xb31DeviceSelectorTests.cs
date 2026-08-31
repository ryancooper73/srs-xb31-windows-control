using Xb31.Core.Transport;

namespace Xb31.Core.Tests;

[TestClass]
public sealed class Xb31DeviceSelectorTests
{
    [TestMethod]
    public void SelectId_MatchesThePairedModelNameCaseInsensitively()
    {
        Xb31DeviceCandidate[] candidates =
        [
            new("speaker", "srs-xb31")
        ];

        string? selected = Xb31DeviceSelector.SelectId(candidates);

        Assert.AreEqual("speaker", selected);
    }

    [TestMethod]
    public void SelectId_RejectsWrongModelNames()
    {
        Xb31DeviceCandidate[] candidates =
        [
            new("wrong-model", "SRS-XB41")
        ];

        string? selected = Xb31DeviceSelector.SelectId(candidates);

        Assert.IsNull(selected);
    }

    [TestMethod]
    public void SelectId_UsesStableIdOrderingWhenSeveralSpeakersMatch()
    {
        Xb31DeviceCandidate[] candidates =
        [
            new("device-z", "SRS-XB31"),
            new("device-a", "SRS-XB31")
        ];

        string? selected = Xb31DeviceSelector.SelectId(candidates);

        Assert.AreEqual("device-a", selected);
    }

    [TestMethod]
    public void SelectId_ReturnsNullForAnEmptyCandidateSet()
    {
        string? selected = Xb31DeviceSelector.SelectId([]);

        Assert.IsNull(selected);
    }
}
