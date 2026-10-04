using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

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

            MethodBase[] callers = Callers(typeof(UpdateLauncher).GetMethod("Start"));
            t.Check(callers.Length == 1 && callers[0].Name.StartsWith("<BuildMenu>")
                && (callers[0].DeclaringType.DeclaringType ?? callers[0].DeclaringType) == typeof(ClockWindow),
                "Only one menu handler built in BuildMenu starts the updater, nothing at startup (callers: "
                + string.Join(", ", callers.Select(method => method.DeclaringType.Name + "." + method.Name)) + ")");
        }

        // Product methods whose compiled code refers to the target method, found by its metadata token in their IL.
        // A plain byte scan: a stray match adds a caller and fails loudly, it can't hide one.
        private static MethodBase[] Callers(MethodBase target)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            return target.Module.GetTypes()
                .Where(type => type.Namespace != typeof(WidgetTests).Namespace)
                .SelectMany(type => type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
                .Where(method => method.GetMethodBody() != null && Refers(method.GetMethodBody().GetILAsByteArray(), target.MetadataToken))
                .ToArray();
        }

        private static bool Refers(byte[] code, int token)
        {
            for (int i = 0; i + 4 <= code.Length; i++)
                if (BitConverter.ToInt32(code, i) == token) return true;
            return false;
        }
    }
}
