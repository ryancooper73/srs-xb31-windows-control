using System.Runtime.ExceptionServices;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class TrayApplicationControllerTests
{
    [TestMethod]
    public void ShowWindowAfterDisposalDoesNotRestoreWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? application = null;
            MainWindow? window = null;
            try
            {
                application = new App();
                application.InitializeComponent();
                window = new MainWindow(new OfflineStartupClient());
                var controller = new TrayApplicationController(
                    application,
                    window,
                    window.ViewModel,
                    UserSettings.CreateForCurrentUser("unused-by-this-test.exe"),
                    new DisplayStateMonitor());

                controller.Dispose();
                controller.ShowWindow();

                Assert.IsFalse(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.AllowApplicationExit();
                window?.Close();
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
