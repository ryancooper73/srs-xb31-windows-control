using Microsoft.Win32;
using Xb31.Control;

namespace Xb31.Control.Tests;

[TestClass]
public sealed class UserSettingsTests
{
    [TestMethod]
    public void SyncLightingWithDisplay_MissingValueDefaultsOnAndChangesPersist()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\Program Files\XB31 Control\Xb31.Control.exe";
            var settings = new UserSettings(testRoot, exePath);

            Assert.IsTrue(settings.SyncLightingWithDisplay);

            settings.SyncLightingWithDisplay = false;
            Assert.IsFalse(new UserSettings(testRoot, exePath).SyncLightingWithDisplay);

            settings.SyncLightingWithDisplay = true;
            Assert.IsTrue(new UserSettings(testRoot, exePath).SyncLightingWithDisplay);
        });
    }

    [TestMethod]
    public void SyncLightingWithDisplay_MalformedValueDefaultsOn()
    {
        WithTestRoot(testRoot =>
        {
            using RegistryKey applicationKey = testRoot.CreateSubKey(UserSettings.ApplicationKeyPath);
            applicationKey.SetValue("SyncLightingWithDisplay", "invalid", RegistryValueKind.String);

            var settings = new UserSettings(testRoot, @"C:\XB31\Xb31.Control.exe");

            Assert.IsTrue(settings.SyncLightingWithDisplay);
        });
    }

    [TestMethod]
    public void StartWithWindows_TracksOnlyQuotedCurrentExecutable()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\Program Files\XB31 Control\Xb31.Control.exe";
            var settings = new UserSettings(testRoot, exePath);

            Assert.IsFalse(settings.StartWithWindows);

            settings.StartWithWindows = true;
            Assert.IsTrue(settings.StartWithWindows);
            Assert.AreEqual($"\"{exePath}\" --startup", ReadRunValue(testRoot));

            using (RegistryKey runKey = testRoot.CreateSubKey(UserSettings.RunKeyPath))
            {
                runKey.SetValue(UserSettings.RunValueName, $"\"{exePath.ToUpperInvariant()}\"");
            }

            Assert.IsTrue(settings.StartWithWindows);

            using (RegistryKey runKey = testRoot.CreateSubKey(UserSettings.RunKeyPath))
            {
                runKey.SetValue(UserSettings.RunValueName, "\"C:\\Other\\Xb31.Control.exe\"");
            }

            Assert.IsFalse(settings.StartWithWindows);
        });
    }

    [TestMethod]
    public void StartWithWindows_DisablingDeletesOnlyApplicationRunValue()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\XB31\Xb31.Control.exe";
            const string unrelatedValueName = "Unrelated Application";
            using (RegistryKey runKey = testRoot.CreateSubKey(UserSettings.RunKeyPath))
            {
                runKey.SetValue(unrelatedValueName, @"C:\Other\Other.exe");
            }

            var settings = new UserSettings(testRoot, exePath)
            {
                StartWithWindows = true
            };

            settings.StartWithWindows = false;

            Assert.IsNull(ReadRunValue(testRoot));
            using RegistryKey? remainingRunKey = testRoot.OpenSubKey(UserSettings.RunKeyPath);
            Assert.AreEqual(@"C:\Other\Other.exe", remainingRunKey?.GetValue(unrelatedValueName));
        });
    }

    [TestMethod]
    public void StartWithWindows_EnablingWritesTheQuotedExecutableWithTheStartupSwitch()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\Program Files\XB31 Control\Xb31.Control.exe";
            var settings = new UserSettings(testRoot, exePath);

            settings.StartWithWindows = true;

            Assert.AreEqual($"\"{exePath}\" --startup", ReadRunValue(testRoot));
            Assert.AreEqual($"\"{exePath}\" --startup", settings.StartupCommand);
        });
    }

    [TestMethod]
    public void StartWithWindows_DisablingThenReEnablingKeepsTheStartupSwitch()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\Program Files\XB31 Control\Xb31.Control.exe";
            var settings = new UserSettings(testRoot, exePath);

            settings.StartWithWindows = true;
            settings.StartWithWindows = false;
            Assert.IsNull(ReadRunValue(testRoot));

            settings.StartWithWindows = true;

            Assert.IsTrue(settings.StartWithWindows);
            Assert.AreEqual($"\"{exePath}\" --startup", ReadRunValue(testRoot));
        });
    }

    [TestMethod]
    public void StartWithWindows_LegacyCommandWithoutTheSwitchStillReadsAsEnabled()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\Program Files\XB31 Control\Xb31.Control.exe";
            using (RegistryKey runKey = testRoot.CreateSubKey(UserSettings.RunKeyPath))
            {
                runKey.SetValue(UserSettings.RunValueName, $"\"{exePath}\"");
            }

            Assert.IsTrue(new UserSettings(testRoot, exePath).StartWithWindows);
        });
    }

    [TestMethod]
    public void RepairStartWithWindowsCommand_UpgradesALegacyEntryInPlace()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\Program Files\XB31 Control\Xb31.Control.exe";
            using (RegistryKey runKey = testRoot.CreateSubKey(UserSettings.RunKeyPath))
            {
                runKey.SetValue(UserSettings.RunValueName, $"\"{exePath}\"");
            }

            new UserSettings(testRoot, exePath).RepairStartWithWindowsCommand();

            Assert.AreEqual($"\"{exePath}\" --startup", ReadRunValue(testRoot));
        });
    }

    [TestMethod]
    public void RepairStartWithWindowsCommand_LeavesForeignAndMissingEntriesAlone()
    {
        WithTestRoot(testRoot =>
        {
            const string exePath = @"C:\XB31\Xb31.Control.exe";
            const string foreignCommand = @"""C:\Other\Xb31.Control.exe""";
            var settings = new UserSettings(testRoot, exePath);

            settings.RepairStartWithWindowsCommand();
            Assert.IsNull(ReadRunValue(testRoot));

            using (RegistryKey runKey = testRoot.CreateSubKey(UserSettings.RunKeyPath))
            {
                runKey.SetValue(UserSettings.RunValueName, foreignCommand);
            }

            settings.RepairStartWithWindowsCommand();

            Assert.AreEqual(foreignCommand, ReadRunValue(testRoot));
        });
    }

    private static void WithTestRoot(Action<RegistryKey> test)
    {
        string testKeyPath = $@"Software\SRS-XB31\Tests\{Guid.NewGuid():N}";

        try
        {
            using RegistryKey testRoot = Registry.CurrentUser.CreateSubKey(testKeyPath, writable: true);
            test(testRoot);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testKeyPath, throwOnMissingSubKey: false);
        }
    }

    private static object? ReadRunValue(RegistryKey testRoot)
    {
        using RegistryKey? runKey = testRoot.OpenSubKey(UserSettings.RunKeyPath);
        return runKey?.GetValue(UserSettings.RunValueName);
    }
}
