using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace DlbPrecision.DesktopClock.Tests
{
    // Runs every class in this namespace that has a static Run(TestContext) method, in name order, and
    // exits with the number of failed checks so test.ps1 and the release build can gate on it.
    internal static class TestRunner
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("usage: Tests.exe <FakeClock.exe> [suite name filter]");
                return 100;
            }
            string filter = args.Length > 1 ? args[1] : null;
            List<MethodInfo> suites = typeof(TestRunner).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(TestRunner).Namespace)
                .Select(type => type.GetMethod("Run", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(TestContext) }, null))
                .Where(method => method != null)
                .OrderBy(method => method.DeclaringType.Name, StringComparer.Ordinal)
                .ToList();
            using (var context = new TestContext(Path.GetFullPath(args[0])))
            {
                foreach (MethodInfo suite in suites)
                {
                    string name = suite.DeclaringType.Name;
                    if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine();
                    Console.WriteLine("== " + name);
                    try
                    {
                        suite.Invoke(null, new object[] { context });
                    }
                    catch (TargetInvocationException error)
                    {
                        context.Check(false, "the suite finished without crashing: " + error.InnerException);
                    }
                }
                Console.WriteLine();
                Console.WriteLine("{0} passed, {1} failed", context.Passed, context.Failed);
                return context.Failed;
            }
        }
    }

    internal sealed class TestContext : IDisposable
    {
        private readonly HashSet<string> fakeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public TestContext(string fakeClock)
        {
            FakeClock = fakeClock;
            // Never the updater's own DLBPrecision-DesktopClock-Update- prefix, so the code under test can't mistake it.
            Scratch = Path.Combine(Path.GetTempPath(), "DLBPrecision-DesktopClock-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Scratch);
        }

        public string FakeClock { get; private set; }
        public string Scratch { get; private set; }
        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public void Check(bool ok, string description)
        {
            if (ok) Passed++;
            else Failed++;
            Console.WriteLine((ok ? "  PASS " : "  FAIL ") + description);
        }

        // A fresh folder for one test, inside this run's scratch folder.
        public string NewFolder(string name)
        {
            string path = Path.Combine(Scratch, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(path);
            return path;
        }

        // Copies the stand-in clock into a folder under the given exe name, with mode.txt beside it.
        public string PlaceFakeClock(string folder, string exeName, string mode)
        {
            string path = Path.Combine(folder, exeName);
            File.Copy(FakeClock, path, true);
            File.WriteAllText(Path.Combine(folder, "mode.txt"), mode);
            fakeNames.Add(Path.GetFileNameWithoutExtension(exeName));
            return path;
        }

        public static Process StartAndWaitForWindow(string exe, int milliseconds)
        {
            Process process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
            WaitFor(() => NativeMethods.WindowsOf(process.Id, "DLB Precision Desktop Clock").Count > 0 || process.HasExited, milliseconds);
            return process;
        }

        public static bool WaitFor(Func<bool> condition, int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                if (condition()) return true;
                Thread.Sleep(50);
            }
            return condition();
        }

        public void Dispose()
        {
            // Stand-ins can't outlive the run, but only copies inside this run's scratch folder are touched.
            foreach (string name in fakeNames)
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        string path = null;
                        try { path = process.MainModule.FileName; }
                        catch (System.ComponentModel.Win32Exception) { continue; } // Another user's process: not ours.
                        catch (InvalidOperationException) { continue; }            // Already exited.
                        if (!path.StartsWith(Scratch, StringComparison.OrdinalIgnoreCase)) continue;
                        process.Kill();
                        process.WaitForExit(5000);
                    }
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(Scratch)) Directory.Delete(Scratch, true);
                    return;
                }
                catch (IOException) { Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            }
            Console.WriteLine("  note: couldn't remove " + Scratch);
        }
    }
}
