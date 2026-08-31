namespace Xb31.Control.Tests;

[TestClass]
public sealed class SingleInstanceCoordinatorTests
{
    [TestMethod]
    public void FirstAcquisitionSucceeds()
    {
        string instanceName = CreateInstanceName();

        bool acquired = SingleInstanceCoordinator.TryAcquire(instanceName, out var coordinator);

        Assert.IsTrue(acquired);
        Assert.IsNotNull(coordinator);
        coordinator.Dispose();
    }

    [TestMethod]
    public void SecondAcquisitionFailsAndSignalsFirst()
    {
        string instanceName = CreateInstanceName();
        Assert.IsTrue(SingleInstanceCoordinator.TryAcquire(instanceName, out var first));
        Assert.IsNotNull(first);
        using (first)
        using (var activated = new ManualResetEventSlim())
        {
            first.ListenForActivation(activated.Set);

            bool acquired = SingleInstanceCoordinator.TryAcquire(instanceName, out var second);

            Assert.IsFalse(acquired);
            Assert.IsNull(second);
            Assert.IsTrue(activated.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    [TestMethod]
    public void DisposalAllowsLaterCoordinatorToBecomeFirst()
    {
        string instanceName = CreateInstanceName();
        Assert.IsTrue(SingleInstanceCoordinator.TryAcquire(instanceName, out var first));
        Assert.IsNotNull(first);
        first.Dispose();
        first.Dispose();

        bool acquired = SingleInstanceCoordinator.TryAcquire(instanceName, out var later);

        Assert.IsTrue(acquired);
        Assert.IsNotNull(later);
        later.Dispose();
    }

    [TestMethod]
    public void ActivationSignaledBeforeListenerRegistrationIsDelivered()
    {
        string instanceName = CreateInstanceName();
        Assert.IsTrue(SingleInstanceCoordinator.TryAcquire(instanceName, out var first));
        Assert.IsNotNull(first);
        using (first)
        using (var activated = new ManualResetEventSlim())
        {
            Assert.IsFalse(SingleInstanceCoordinator.TryAcquire(instanceName, out var second));
            Assert.IsNull(second);

            first.ListenForActivation(activated.Set);

            Assert.IsTrue(activated.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    private static string CreateInstanceName() =>
        $@"Local\SRS-XB31.Tests.{Guid.NewGuid():N}";
}
