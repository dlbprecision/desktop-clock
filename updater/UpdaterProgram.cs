using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace DlbPrecision.DesktopClock.Updater
{
    // The updater runs as its own short-lived process, so the clock itself never runs network code:
    //   --update [--feed <https URL or file>]                       check, offer, download, verify, replace
    //   --verify-package <exe> <version> <report> [--test-build]    the release build's check; never shows a window
    internal static class UpdaterProgram
    {
        public static bool IsUpdaterCommand(string[] args)
        {
            return args.Length > 0 && (args[0] == "--update" || args[0] == "--verify-package");
        }

        public static int Run(string[] args)
        {
            if (args[0] == "--verify-package") return VerifyForRelease(args);
            int feedAt = Array.IndexOf(args, "--feed");
            string feed = feedAt >= 0 && feedAt + 1 < args.Length ? args[feedAt + 1] : null;
            bool first;
            using (var instance = new Mutex(true, @"Local\DLBPrecision.DesktopClock.Updater", out first))
            {
                if (!first)
                {
                    // A second click brings the open updater forward instead of starting another.
                    IntPtr open = NativeMethods.FindWindow(null, UpdaterWindow.WindowTitle);
                    if (open != IntPtr.Zero) NativeMethods.SetForegroundWindow(open);
                    return 0;
                }
                ReleaseFeed.UseSystemTls();
                var window = new UpdaterWindow(Assembly.GetEntryAssembly().Location, feed);
                window.Loaded += delegate { window.Check(); };
                new Application().Run(window);
            }
            return 0;
        }

        // "1.3.0", or all four parts for a local test build such as 1.2.9.9.
        public static string VersionText(Version version)
        {
            return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }

        // Exit 0: accepted. 2: installed clocks would refuse it. 3: the check itself failed.
        private static int VerifyForRelease(string[] args)
        {
            if (args.Length < 4) return 3;
            string outcome;
            int code;
            try
            {
                VerificationResult result = PackageVerifier.VerifyPackage(Path.GetFullPath(args[1]), args[2], args.Contains("--test-build"));
                outcome = result.Ok ? "ACCEPTED " + Path.GetFileName(args[1]) + " as version " + args[2] : "REJECTED " + result.Reason;
                code = result.Ok ? 0 : 2;
            }
            catch (Exception error)   // The build reads the report; a crash dialog would hang it.
            {
                outcome = "ERROR " + error.GetType().Name + ": " + error.Message;
                code = 3;
            }
            try
            {
                File.WriteAllText(args[3], outcome);
            }
            catch (IOException) { return 3; }
            catch (UnauthorizedAccessException) { return 3; }
            return code;
        }
    }
}
