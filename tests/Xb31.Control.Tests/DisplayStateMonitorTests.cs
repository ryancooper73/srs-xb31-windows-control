using Xb31.Control;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class DisplayStateMonitorTests
{
#pragma warning disable MSTEST0044 // The approved brief requires DataTestMethod.
    [DataTestMethod]
    [DataRow(0u, DisplayState.Off)]
    [DataRow(1u, DisplayState.On)]
    [DataRow(2u, DisplayState.Dim)]
    public void TryMapState_KnownValue_ReturnsTypedState(uint raw, object expected)
    {
        Assert.IsTrue(DisplayStateMonitor.TryMapState(raw, out DisplayState actual));
        Assert.AreEqual((DisplayState)expected, actual);
    }
#pragma warning restore MSTEST0044

    [TestMethod]
    public void TryMapState_UnknownValue_IsIgnored() =>
        Assert.IsFalse(DisplayStateMonitor.TryMapState(3, out _));

    [TestMethod]
    public void SessionDisplayStatus_UsesInteractiveSessionGuid() =>
        Assert.AreEqual(
            new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5"),
            DisplayStateMonitor.SessionDisplayStatus);
}
