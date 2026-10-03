using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DlbPrecision.DesktopClock.Updater
{
    internal enum SwapOutcome { Updated, RolledBack, NotReplaced }

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

        // Before an update: tidy up after an interrupted one. If it stopped between its two renames, the backup is the clock.
        public static void RemoveLeftovers(string exe)
        {
            if (!File.Exists(exe) && File.Exists(OldPath(exe))) File.Move(OldPath(exe), exe);
            TryDelete(NewPath(exe));
            TryDelete(OldPath(exe));
        }

        public SwapResult Replace(string exe)
        {
            string fresh = NewPath(exe), backup = OldPath(exe);
            bool wasRunning = CloseClock(exe);
            string problem = Rename(exe, fresh, backup);
            if (problem != null)
            {
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

            // Safety net: the new version didn't start, so the previous one goes back.
            Stop(started);
            for (int attempt = 0; attempt < 20 && File.Exists(exe); attempt++) { TryDelete(exe); if (File.Exists(exe)) Thread.Sleep(100); }
            File.Move(backup, exe);
            Start(exe);
            return new SwapResult(SwapOutcome.RolledBack, "The new version didn't start, so the previous version was put back.");
        }

        // exe -> backup, then fresh -> exe. Returns null, or why it couldn't, with exe back as it was.
        private static string Rename(string exe, string fresh, string backup)
        {
            try { File.Move(exe, backup); }
            catch (IOException error) { return error.Message; }
            catch (UnauthorizedAccessException error) { return error.Message; }
            try
            {
                File.Move(fresh, exe);
                return null;
            }
            catch (IOException error) { File.Move(backup, exe); return error.Message; }
            catch (UnauthorizedAccessException error) { File.Move(backup, exe); return error.Message; }
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

        private static void TryDelete(string path)
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
