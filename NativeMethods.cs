using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

// The updater runs from a temporary folder; Windows libraries must only ever come from System32.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace DlbPrecision.DesktopClock
{
    internal static class NativeMethods
    {
        public const int WM_CLOSE = 0x0010;

        public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        // Visible top-level windows of one process; with a title, only windows with exactly that title.
        public static List<IntPtr> WindowsOf(int processId, string title)
        {
            var found = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner != (uint)processId || !IsWindowVisible(window)) return true;
                if (title != null)
                {
                    var text = new StringBuilder(title.Length + 2);
                    GetWindowText(window, text, text.Capacity);
                    if (!string.Equals(text.ToString(), title, StringComparison.Ordinal)) return true;
                }
                found.Add(window);
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
