using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DlbPrecision.DesktopClock.Tests
{
    // Carry-over from Chevy Clock. Never uses the real names: a stand-in process name, a test registry key
    // and temporary folders, so a run can't close David's clock or edit his startup entries.
    internal static class MigrationTests
    {
        private const string TestRoot = @"Software\DLBPrecision\DesktopClockTests";
        private const string TestRunKey = TestRoot + @"\Run";

        public static void Run(TestContext t)
        {
            Settings(t);
            RunValue(t);
            OldClock(t);
            ShippedNames(t);
        }

        private static void Settings(TestContext t)
        {
            string folder = t.NewFolder("migrate-settings");
            string oldPath = Path.Combine(folder, "old", "settings.ini");
            string newPath = Path.Combine(folder, "new", "nested", "settings.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath));
            File.WriteAllText(oldPath, "left=3453\r\ntop=-509\r\ntheme=dlb\r\n");

            t.Check(LegacyMigration.CopySettings(oldPath, newPath) && File.ReadAllText(newPath) == File.ReadAllText(oldPath),
                "Old settings are copied, creating the new folders, when the new file doesn't exist");
            File.WriteAllText(newPath, "left=1\r\n");
            t.Check(!LegacyMigration.CopySettings(oldPath, newPath) && File.ReadAllText(newPath) == "left=1\r\n",
                "An existing new settings file is never overwritten");
            t.Check(File.ReadAllText(oldPath).Contains("theme=dlb"), "The old settings file is left in place");

            string freshPath = Path.Combine(folder, "fresh", "settings.ini");
            t.Check(!LegacyMigration.CopySettings(Path.Combine(folder, "absent", "settings.ini"), freshPath) && !File.Exists(freshPath),
                "Nothing happens when there are no old settings");
            using (new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.None))
                t.Check(!LegacyMigration.CopySettings(oldPath, freshPath) && !File.Exists(freshPath),
                    "An unreadable old file is skipped without an error or a half-written new file");
        }

        private static void RunValue(TestContext t)
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, false);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(TestRunKey))
            {
                key.SetValue("ChevyClock", "\"C:\\old\\ChevyClock.exe\"");
                key.SetValue("Other", "\"C:\\other.exe\"");
            }
            t.Check(LegacyMigration.RemoveRunValue(TestRunKey, "ChevyClock"), "The legacy startup entry is removed");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TestRunKey))
                t.Check(key.GetValue("ChevyClock") == null && (string)key.GetValue("Other") == "\"C:\\other.exe\"",
                    "Other startup entries are kept");
            t.Check(!LegacyMigration.RemoveRunValue(TestRunKey, "ChevyClock"), "Removing it again is a no-op");
            t.Check(!LegacyMigration.RemoveRunValue(TestRoot + @"\Missing", "ChevyClock"), "A missing key is a no-op");
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, false);
        }

        private static void OldClock(TestContext t)
        {
            const string standIn = "ChevyClockStandIn";   // Never ChevyClock: that would close David's real clock.
            t.Check(LegacyMigration.CloseOldClocks(standIn, 5000) == 0, "With no old clock running there is nothing to close");

            string folder = t.NewFolder("old-clock");
            using (Process old = TestContext.StartAndWaitForWindow(t.PlaceFakeClock(folder, standIn + ".exe", "ok"), 10000))
            {
                int closed = LegacyMigration.CloseOldClocks(standIn, 5000);
                t.Check(closed == 1 && old.HasExited && File.Exists(Path.Combine(folder, "closed.txt")),
                    "A running old clock is asked to close and closes normally, so it saves its settings");
            }

            folder = t.NewFolder("old-clock-hang");
            using (Process hung = TestContext.StartAndWaitForWindow(t.PlaceFakeClock(folder, standIn + ".exe", "hang"), 10000))
            {
                int closed = LegacyMigration.CloseOldClocks(standIn, 1500);
                t.Check(closed == 1 && hung.WaitForExit(5000), "An old clock that won't close is stopped, so two clocks never stay on screen");
            }
        }

        // The values that ship must name Chevy Clock's real leftovers and the new clock's own locations.
        private static void ShippedNames(TestContext t)
        {
            t.Check(LegacyMigration.OldProcessName == "ChevyClock" && LegacyMigration.OldRunValueName == "ChevyClock",
                "The shipped migration targets ChevyClock.exe and its ChevyClock startup entry");
            t.Check(LegacyMigration.OldSettingsPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
                && LegacyMigration.OldSettingsPath.EndsWith(@"\ChevyClock\settings.ini", StringComparison.OrdinalIgnoreCase),
                @"Old settings are read from %APPDATA%\ChevyClock\settings.ini");
            t.Check(ClockWindow.SettingsPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
                && ClockWindow.SettingsPath.EndsWith(@"\DLBPrecision\DesktopClock\settings.ini", StringComparison.OrdinalIgnoreCase),
                @"New settings live in %LOCALAPPDATA%\DLBPrecision\DesktopClock\settings.ini");
            t.Check(ClockWindow.RunValueName == "DLBPrecisionDesktopClock", "The new startup entry is named DLBPrecisionDesktopClock");
        }
    }
}
