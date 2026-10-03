using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// The clock and its updater run from a folder the user can write to; Windows libraries must only ever come from System32.
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr window);

        private const uint WtdUiNone = 2, WtdRevokeWholeChain = 1, WtdChoiceFile = 1, WtdStateActionVerify = 1, WtdStateActionClose = 2;
        private const uint WtdRevocationCheckChain = 0x40;
        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo
        {
            public uint Size;
            public IntPtr FilePath;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr window, [In] ref Guid action, [In, Out] ref WinTrustData data);

        // Windows' own Authenticode check of the open file, including revocation of the whole chain.
        // Returns 0 when the signature is valid and trusted.
        public static int VerifyEmbeddedSignature(string path, SafeFileHandle file)
        {
            IntPtr pathText = Marshal.StringToCoTaskMemUni(path);
            IntPtr fileInfo = IntPtr.Zero;
            try
            {
                var info = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)), FilePath = pathText, File = file.DangerousGetHandle() };
                fileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(info, fileInfo, false);
                var data = new WinTrustData
                {
                    Size = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = WtdUiNone,
                    RevocationChecks = WtdRevokeWholeChain,
                    UnionChoice = WtdChoiceFile,
                    FileInfo = fileInfo,
                    StateAction = WtdStateActionVerify,
                    ProviderFlags = WtdRevocationCheckChain
                };
                Guid action = GenericVerifyV2;
                int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                data.StateAction = WtdStateActionClose;
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return result;
            }
            finally
            {
                if (fileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfo);
                Marshal.FreeCoTaskMem(pathText);
                GC.KeepAlive(file);
            }
        }

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
