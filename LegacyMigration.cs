using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DlbPrecision.DesktopClock
{
    // One-time carry-over from Chevy Clock (1.2 and older, ChevyClock.exe) to DLB Precision Desktop Clock.
    // It runs at every start, but each step acts only when something is left to carry over.
    internal static class LegacyMigration
    {
        public const string OldProcessName = "ChevyClock";
        public const string OldRunValueName = "ChevyClock";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const int CloseWaitMilliseconds = 5000;

        public static string OldSettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChevyClock", "settings.ini"); }
        }

        // The old clock closes first, so the settings copied next are the ones it saved on its way out.
        public static void Run(string newSettingsPath)
        {
            CloseOldClocks(OldProcessName, CloseWaitMilliseconds);
            CopySettings(OldSettingsPath, newSettingsPath);
            RemoveRunValue(RunKeyPath, OldRunValueName);
        }

        // Asks every old clock in this Windows session to close the normal way, so it saves its settings.
        // One still running at the deadline is stopped, so two clocks never stay on screen. Returns how many ran.
        public static int CloseOldClocks(string processName, int waitMilliseconds)
        {
            int session = Process.GetCurrentProcess().SessionId;
            int count = 0;
            foreach (Process process in Process.GetProcessesByName(processName))
                using (process)
                {
                    if (process.SessionId != session) continue;
                    count++;
                    foreach (IntPtr window in NativeMethods.WindowsOf(process.Id, null))
                        NativeMethods.PostMessage(window, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    if (process.WaitForExit(waitMilliseconds)) continue;
                    try
                    {
                        process.Kill();
                        process.WaitForExit(waitMilliseconds);
                    }
                    catch (InvalidOperationException) { /* It exited just now. */ }
                    catch (System.ComponentModel.Win32Exception) { /* It can't be stopped; the new clock still starts. */ }
                }
            return count;
        }

        // Copies only while the new file doesn't exist, so settings the new clock has saved are never replaced.
        public static bool CopySettings(string oldPath, string newPath)
        {
            if (File.Exists(newPath) || !File.Exists(oldPath)) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                File.Copy(oldPath, newPath, false);
                return true;
            }
            catch (IOException) { return false; }                     // Locked or vanished: the clock starts with defaults.
            catch (UnauthorizedAccessException) { return false; }
        }

        public static bool RemoveRunValue(string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                {
                    if (key == null || key.GetValue(valueName) == null) return false;
                    key.DeleteValue(valueName, false);
                    return true;
                }
            }
            catch (UnauthorizedAccessException) { return false; }      // Policy-locked; the old exe is gone anyway.
            catch (System.Security.SecurityException) { return false; }
        }
    }
}
