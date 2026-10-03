using System.ComponentModel;
using System.Diagnostics;

namespace DlbPrecision.DesktopClock.Tests
{
    // The clock only starts the updater. Never constructs the clock window: that writes the real startup entry.
    internal static class WidgetTests
    {
        public static void Run(TestContext t)
        {
            ProcessStartInfo started = null;
            int starts = 0;
            string problem = UpdateLauncher.Start(@"C:\Clock\DlbPrecision.DesktopClock.exe", delegate(ProcessStartInfo info) { started = info; starts++; });
            t.Check(problem == null && starts == 1 && started.FileName == @"C:\Clock\DlbPrecision.DesktopClock.exe"
                && started.Arguments == "--update" && !started.UseShellExecute && started.WorkingDirectory == @"C:\Clock",
                "Check for updates starts exactly one updater, from the running clock's own file");
            string failed = UpdateLauncher.Start(@"C:\Clock\DlbPrecision.DesktopClock.exe", delegate { throw new Win32Exception(2); });
            t.Check(failed != null && failed.StartsWith("The updater could not start"), "A launch failure becomes a message, not a crash");
        }
    }
}
