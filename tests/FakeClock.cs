// Stand-in clock for the tests, copied under whatever exe name a test needs. It reads its mode from
// mode.<its own file size>.txt beside it, else mode.txt: "ok" shows a window until it is closed, "hang"
// shows one that refuses to close, "nowindow" runs without a window, "crash" exits at once with code 3.
// (Swap tests tell an "old" and a "new" copy apart by appending bytes to one, which Windows ignores.)
// Every start appends "<pid> <mode>" to started.txt beside it, and a normal close appends "<pid>" to
// closed.txt. Windows open far off-screen and never take focus, and every mode exits by itself after two
// minutes, so a failed test can't leave one behind.
// "--swap <close ms> <start ms>" instead replaces this exe with <exe>.new the way the updater does, from the
// clock's own file, and writes the outcome to swap.txt.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    internal static class FakeClock
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string self = Assembly.GetEntryAssembly().Location;
            string folder = Path.GetDirectoryName(self);
            if (args.Length == 3 && args[0] == "--swap")
            {
                var swapper = new ClockSwapper { CloseWaitMilliseconds = int.Parse(args[1]), StartWaitMilliseconds = int.Parse(args[2]) };
                File.WriteAllText(Path.Combine(folder, "swap.txt"), swapper.Replace(self).Outcome.ToString());
                return 0;
            }
            string ownModeFile = Path.Combine(folder, "mode." + new FileInfo(self).Length + ".txt");
            string modeFile = File.Exists(ownModeFile) ? ownModeFile : Path.Combine(folder, "mode.txt");
            string mode = File.Exists(modeFile) ? File.ReadAllText(modeFile).Trim() : "ok";
            int pid = Process.GetCurrentProcess().Id;
            File.AppendAllText(Path.Combine(folder, "started.txt"), pid + " " + mode + Environment.NewLine);
            if (mode == "crash") return 3;
            if (mode == "nowindow")
            {
                Thread.Sleep(TimeSpan.FromMinutes(2));
                return 0;
            }

            var app = new Application();
            var window = new Window
            {
                Title = ClockSwapper.WindowTitle,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                Width = 200,
                Height = 60
            };
            if (mode == "hang") window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = true; };
            var limit = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            limit.Tick += delegate { Environment.Exit(0); };
            limit.Start();
            app.Run(window);
            File.AppendAllText(Path.Combine(folder, "closed.txt"), pid + Environment.NewLine);
            return 0;
        }
    }
}
