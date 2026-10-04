using System.Diagnostics;
using System.IO;

namespace DlbPrecision.DesktopClock.Tests
{
    // The stand-in clock behaves as the swap and migration tests rely on.
    internal static class HarnessTests
    {
        public static void Run(TestContext t)
        {
            string folder = t.NewFolder("fake-ok");
            string exe = t.PlaceFakeClock(folder, "StandInOk.exe", "ok");
            using (Process ok = TestContext.StartAndWaitForWindow(exe, 10000))
            {
                t.Check(!ok.HasExited && NativeMethods.WindowsOf(ok.Id, "DLB Precision Desktop Clock").Count == 1,
                    "An ok stand-in shows one window titled DLB Precision Desktop Clock");
                foreach (var window in NativeMethods.WindowsOf(ok.Id, null)) NativeMethods.PostMessage(window, NativeMethods.WM_CLOSE, System.IntPtr.Zero, System.IntPtr.Zero);
                t.Check(ok.WaitForExit(5000) && File.Exists(Path.Combine(folder, "closed.txt")), "It closes on WM_CLOSE and records it");
            }

            folder = t.NewFolder("fake-hang");
            exe = t.PlaceFakeClock(folder, "StandInHang.exe", "hang");
            using (Process hang = TestContext.StartAndWaitForWindow(exe, 10000))
            {
                foreach (var window in NativeMethods.WindowsOf(hang.Id, null)) NativeMethods.PostMessage(window, NativeMethods.WM_CLOSE, System.IntPtr.Zero, System.IntPtr.Zero);
                t.Check(!hang.WaitForExit(1500), "A hang stand-in ignores WM_CLOSE");
                hang.Kill();
                hang.WaitForExit(5000);
            }

            folder = t.NewFolder("fake-crash");
            exe = t.PlaceFakeClock(folder, "StandInCrash.exe", "crash");
            using (Process crash = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }))
                t.Check(crash.WaitForExit(10000) && crash.ExitCode == 3 && File.ReadAllText(Path.Combine(folder, "started.txt")).Contains(" crash"),
                    "A crash stand-in exits at once with code 3 and records its start");
        }
    }
}
