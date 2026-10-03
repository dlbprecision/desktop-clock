using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // Replacing a clock, with stand-in clocks in temporary folders. The "new" exe is the stand-in with bytes
    // appended (Windows ignores them), so the two files differ and each can be given its own mode.
    internal static class SwapTests
    {
        private static readonly byte[] Marker = Encoding.ASCII.GetBytes(new string('N', 512));

        public static void Run(TestContext t)
        {
            Setup s = Prepare(t, "updated", "ok", "ok", true);
            SwapResult result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.Updated && Sha256(s.Exe) == s.NewSha && NoLeftovers(s), "A normal update puts the new file in place and leaves nothing behind");
            t.Check(s.Old.HasExited && File.ReadAllText(Path.Combine(s.Folder, "closed.txt")).Contains(s.Old.Id.ToString()),
                "The old clock was closed the normal way, so it saved its settings");
            t.Check(ClocksRunningFrom(s.Exe) == 1, "Exactly one clock, the new one, is running with its window");

            s = Prepare(t, "crash", "ok", "crash", true);
            result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.RolledBack && result.Message.Contains("didn't start") && Sha256(s.Exe) == s.OldSha && NoLeftovers(s),
                "A new version that crashes is rolled back to the previous file");
            t.Check(ClocksRunningFrom(s.Exe) == 1, "...and the previous clock is running again");

            s = Prepare(t, "nowindow", "ok", "nowindow", true);
            result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.RolledBack && Sha256(s.Exe) == s.OldSha && ClocksRunningFrom(s.Exe) == 1,
                "A new version that never shows a window is stopped and rolled back");

            s = Prepare(t, "hang", "hang", "ok", true);
            result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.Updated && s.Old.HasExited && Sha256(s.Exe) == s.NewSha,
                "An old clock that won't close is stopped and the update continues");

            s = Prepare(t, "locked", "ok", "ok", true);
            using (new FileStream(s.Exe, FileMode.Open, FileAccess.Read, FileShare.Read))   // no delete sharing: renaming fails
                result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.NotReplaced && result.Message.StartsWith("Couldn't replace the clock")
                && Sha256(s.Exe) == s.OldSha && NoLeftovers(s) && ClocksRunningFrom(s.Exe) == 1,
                "A clock file that can't be moved is reported, nothing changes and the clock runs again");

            s = Prepare(t, "notrunning", "ok", "ok", false);
            result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.Updated && Sha256(s.Exe) == s.NewSha && ClocksRunningFrom(s.Exe) == 1,
                "A clock that isn't running is replaced and started");

            s = Prepare(t, "custom", "ok", "ok", true, "MyClock.exe");
            result = Swap(s);
            t.Check(result.Outcome == SwapOutcome.Updated && Path.GetFileName(s.Exe) == "MyClock.exe" && Sha256(s.Exe) == s.NewSha,
                "A clock renamed by its owner keeps its file name");

            string folder = t.NewFolder("leftovers");
            string exe = Path.Combine(folder, "DlbPrecision.DesktopClock.exe");
            File.WriteAllText(exe, "clock");
            File.WriteAllText(ClockSwapper.NewPath(exe), "half a download");
            File.WriteAllText(ClockSwapper.OldPath(exe), "an old backup");
            ClockSwapper.RemoveLeftovers(exe);
            t.Check(File.ReadAllText(exe) == "clock" && !File.Exists(ClockSwapper.NewPath(exe)) && !File.Exists(ClockSwapper.OldPath(exe)),
                "Leftovers from an interrupted update are removed");
            File.Move(exe, ClockSwapper.OldPath(exe));
            ClockSwapper.RemoveLeftovers(exe);
            t.Check(File.ReadAllText(exe) == "clock", "An update interrupted between its two renames gets its clock back");

            // The updater runs from the clock's own file and renames it out of the way, so Windows must allow that.
            s = Prepare(t, "running", "nowindow", "ok", false);
            using (Process running = Process.Start(new ProcessStartInfo(s.Exe) { UseShellExecute = false }))
            {
                TestContext.WaitFor(() => File.Exists(Path.Combine(s.Folder, "started.txt")), 5000);
                bool renamed = true;
                try
                {
                    File.Move(s.Exe, ClockSwapper.OldPath(s.Exe));
                    File.Move(ClockSwapper.NewPath(s.Exe), s.Exe);
                }
                catch (IOException) { renamed = false; }
                t.Check(renamed && !running.HasExited && Sha256(s.Exe) == s.NewSha, "A running exe's file can be renamed and replaced");
                running.Kill();
                running.WaitForExit(5000);
            }
        }

        private sealed class Setup
        {
            public string Folder, Exe, NewSha, OldSha;
            public Process Old;
        }

        // Installs a stand-in clock, and its newer version already downloaded and verified as <exe>.new.
        private static Setup Prepare(TestContext t, string name, string oldMode, string newMode, bool startOld, string exeName = "DlbPrecision.DesktopClock.exe")
        {
            var s = new Setup();
            s.Folder = t.NewFolder("swap-" + name);
            s.Exe = t.PlaceFakeClock(s.Folder, exeName, oldMode);
            s.OldSha = Sha256(s.Exe);
            string fresh = ClockSwapper.NewPath(s.Exe);
            File.Copy(t.FakeClock, fresh);
            using (var append = new FileStream(fresh, FileMode.Append)) append.Write(Marker, 0, Marker.Length);
            s.NewSha = Sha256(fresh);
            File.WriteAllText(Path.Combine(s.Folder, "mode." + new FileInfo(fresh).Length + ".txt"), newMode);
            if (startOld) s.Old = TestContext.StartAndWaitForWindow(s.Exe, 10000);
            return s;
        }

        private static SwapResult Swap(Setup s)
        {
            return new ClockSwapper { CloseWaitMilliseconds = 2000, StartWaitMilliseconds = 4000 }.Replace(s.Exe);
        }

        private static bool NoLeftovers(Setup s)
        {
            return !File.Exists(ClockSwapper.NewPath(s.Exe)) && !File.Exists(ClockSwapper.OldPath(s.Exe));
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        // Clock processes started from exactly this file that show the clock's window.
        private static int ClocksRunningFrom(string exe)
        {
            int count = 0;
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                using (process)
                {
                    string path;
                    try { path = process.MainModule.FileName; }
                    catch (System.ComponentModel.Win32Exception) { continue; }
                    catch (InvalidOperationException) { continue; }
                    if (string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)
                        && TestContext.WaitFor(() => NativeMethods.WindowsOf(process.Id, ClockSwapper.WindowTitle).Count > 0, 5000))
                        count++;
                }
            return count;
        }
    }
}
