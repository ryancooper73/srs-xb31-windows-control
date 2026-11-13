namespace Xb31.Control.Tests;

[TestClass]
public sealed class AppTraceTests
{
    [TestMethod]
    public void GetVersionForTrace_InvalidExecutablePathFallsBackToUnknown()
    {
        Assert.AreEqual("unknown", App.GetVersionForTrace("Z:\\missing\\Xb31.Control.exe"));
    }

    [TestMethod]
    public void Write_AppendsTimestampedNamedEventsInOrder()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(directory, "activity.log");

        try
        {
            var trace = new AppTrace(logPath);

            trace.Write("Startup", "version=1.0.0");
            trace.Write("DisplayState", "ON");

            string[] lines = File.ReadAllLines(logPath);
            Assert.HasCount(2, lines);
            StringAssert.Contains(lines[0], "Startup version=1.0.0");
            StringAssert.Contains(lines[1], "DisplayState ON");
            Assert.IsTrue(DateTimeOffset.TryParse(lines[0].Split(' ', 2)[0], out _));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
