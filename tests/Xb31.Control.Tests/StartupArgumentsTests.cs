using Xb31.Control;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class StartupArgumentsTests
{
    [TestMethod]
    public void IsStartupLaunch_OnlyWhenTheStartupSwitchIsPresent()
    {
        Assert.IsFalse(StartupArguments.IsStartupLaunch([]));
        Assert.IsFalse(StartupArguments.IsStartupLaunch(["--offline-startup-test"]));
        Assert.IsFalse(StartupArguments.IsStartupLaunch(["--Startup"]));
        Assert.IsTrue(StartupArguments.IsStartupLaunch(["--startup"]));
        Assert.IsTrue(
            StartupArguments.IsStartupLaunch(["--offline-startup-test", "--startup"]));
    }

    [TestMethod]
    public void IsOfflineStartupTest_TolerationsMatchTheExistingContract()
    {
        Assert.IsFalse(StartupArguments.IsOfflineStartupTest([]));
        Assert.IsFalse(StartupArguments.IsOfflineStartupTest(["--startup"]));
        Assert.IsFalse(
            StartupArguments.IsOfflineStartupTest(["--offline-startup-test", "extra"]));
        Assert.IsTrue(StartupArguments.IsOfflineStartupTest(["--offline-startup-test"]));
        Assert.IsTrue(
            StartupArguments.IsOfflineStartupTest(["--offline-startup-test", "--startup"]));
    }
}
