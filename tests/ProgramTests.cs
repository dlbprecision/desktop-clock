using System;
using System.IO;
using System.Security.Cryptography;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // Which arguments start the updater, and the release build's --verify-package exit codes.
    internal static class ProgramTests
    {
        public static void Run(TestContext t)
        {
            t.Check(UpdaterProgram.IsUpdaterCommand(new[] { "--update" }) && UpdaterProgram.IsUpdaterCommand(new[] { "--update", "--feed", "x" })
                && UpdaterProgram.IsUpdaterCommand(new[] { "--verify-package", "a", "b", "c" }),
                "--update and --verify-package start the updater side");
            t.Check(!UpdaterProgram.IsUpdaterCommand(new string[0]) && !UpdaterProgram.IsUpdaterCommand(new[] { "--feed", "x" }),
                "No arguments, or anything else, starts the clock");

            string folder = t.NewFolder("verify-package");
            string exe = Path.Combine(folder, UpdateOffer.ExeName);
            string report = Path.Combine(folder, "report.txt");
            File.Copy(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(ProgramTests).Assembly.Location), "..", "fixtures", "dlb-signed-clock.exe")), exe);
            WriteChecksum(exe);
            t.Check(UpdaterProgram.Run(new[] { "--verify-package", exe, "1.2.9.1", report, "--test-build" }) == 0 && File.ReadAllText(report).StartsWith("ACCEPTED"),
                "A signed test build passes the release check (exit 0)");

            File.Copy(t.FakeClock, exe, true);
            WriteChecksum(exe);
            t.Check(UpdaterProgram.Run(new[] { "--verify-package", exe, "1.3.0", report }) == 2 && File.ReadAllText(report).StartsWith("REJECTED"),
                "An unsigned exe fails the release check (exit 2)");
            t.Check(UpdaterProgram.Run(new[] { "--verify-package", exe, "1.3.0", Path.Combine(folder, "missing", "report.txt") }) == 3,
                "A check that can't write its report fails as a check error (exit 3)");
        }

        private static void WriteChecksum(string exe)
        {
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(exe))
                File.WriteAllText(exe + ".sha256", BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() + "  " + UpdateOffer.ExeName + "\n");
        }
    }
}
