using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DlbPrecision.DesktopClock.Updater
{
    internal enum SwapOutcome { Updated, RolledBack, NotReplaced, NotRestored }

    internal sealed class SwapResult
    {
        public SwapResult(SwapOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }

        public SwapOutcome Outcome { get; private set; }
        public string Message { get; private set; }
    }

    // Puts the verified new exe (downloaded next to the clock as <exe>.new) in place of the clock and starts it.
    // Windows lets a running exe be renamed, so this works while the updater runs from the clock's own file;
    // that file ends up as <exe>.old, which the new clock deletes once the updater has exited.
    internal sealed class ClockSwapper
    {
        public const string WindowTitle = "DLB Precision Desktop Clock";

        public ClockSwapper()
        {
            CloseWaitMilliseconds = 10000;
            StartWaitMilliseconds = 15000;
        }

        public int CloseWaitMilliseconds { get; set; }
        public int StartWaitMilliseconds { get; set; }

        public static string NewPath(string exe) { return exe + ".new"; }
        public static string OldPath(string exe) { return exe + ".old"; }

        // Before an update: remove what an interrupted one left behind.
        public static void RemoveLeftovers(string exe)
        {
            TryDelete(NewPath(exe));
            TryDelete(OldPath(exe));
        }

        // Never throws: every outcome comes back as a message to show.
        public SwapResult Replace(string exe)
        {
            string fresh = NewPath(exe), backup = OldPath(exe);
            bool wasRunning = CloseClock(exe);
            string problem = Rename(exe, fresh, backup);
            if (problem != null)
            {
                if (!File.Exists(exe)) return NotRestored("Couldn't replace the clock (" + problem.TrimEnd('.') + ").", exe, backup);
                TryDelete(fresh);
                if (wasRunning) Start(exe);
                return new SwapResult(SwapOutcome.NotReplaced, "Couldn't replace the clock (" + problem.TrimEnd('.') + "). Nothing was changed.");
            }

            Process started = Start(exe);
            if (started != null && WaitForWindow(started))
            {
                TryDelete(backup);   // Succeeds unless the updater is running from it; then the new clock deletes it.
                return new SwapResult(SwapOutcome.Updated, "Updated.");
            }

            // Safety net: the new version didn't start, so the previous one goes back. The new file is renamed aside,
            // not deleted, which works even while the stopped process or a virus scan still has it open.
            Stop(started);
            problem = Rename(exe, backup, fresh);
            if (problem != null)
                return NotRestored("The new version didn't start, and the previous version couldn't be put back (" + problem.TrimEnd('.') + ").", exe, backup);
            TryDelete(fresh);
            return new SwapResult(SwapOutcome.RolledBack, Start(exe) != null
                ? "The new version didn't start, so the previous version was put back."
                : "The new version didn't start, so the previous version was put back. Start it again from its folder.");
        }

        // The previous clock is left as <exe>.old. Starting a clock deletes that file, so it must be renamed back first.
        private static SwapResult NotRestored(string what, string exe, string backup)
        {
            return new SwapResult(SwapOutcome.NotRestored, what + " Before starting the clock again, rename " + Path.GetFileName(backup)
                + " to " + Path.GetFileName(exe) + (File.Exists(exe) ? ", replacing the new file." : "."));
        }

        // Moves exe aside, then replacement into its place. Returns null, or why it couldn't, after putting exe back.
        private static string Rename(string exe, string replacement, string aside)
        {
            string problem = Move(exe, aside);
            if (problem != null) return problem;
            problem = Move(replacement, exe);
            if (problem != null) Move(aside, exe);
            return problem;
        }

        private static string Move(string from, string to)
        {
            try
            {
                File.Move(from, to);
                return null;
            }
            catch (IOException error) { return error.Message; }
            catch (UnauthorizedAccessException error) { return error.Message; }
        }

        // Asks every clock started from this exe to close the normal way, so it saves its settings, and stops any
        // that haven't closed in time. Returns whether one was running.
        private bool CloseClock(string exe)
        {
            bool found = false;
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                using (process)
                {
                    if (process.Id == Process.GetCurrentProcess().Id || !StartedFrom(process, exe)) continue;
                    found = true;
                    foreach (IntPtr window in NativeMethods.WindowsOf(process.Id, null))
                        NativeMethods.PostMessage(window, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    if (!process.WaitForExit(CloseWaitMilliseconds)) Stop(process);
                }
            return found;
        }

        private static bool StartedFrom(Process process, string exe)
        {
            try
            {
                return string.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (System.ComponentModel.Win32Exception) { return false; }   // Another user's or an elevated process: not this clock.
            catch (InvalidOperationException) { return false; }               // Already exited.
        }

        private bool WaitForWindow(Process process)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < StartWaitMilliseconds)
            {
                if (process.HasExited) return false;
                if (NativeMethods.WindowsOf(process.Id, WindowTitle).Count > 0) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        private static Process Start(string exe)
        {
            try
            {
                return Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        private static void Stop(Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException) { /* Already exited. */ }
            catch (System.ComponentModel.Win32Exception) { /* Exiting already. */ }
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { /* In use; removed next time. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
