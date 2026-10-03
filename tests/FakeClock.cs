// Stand-in clock for the tests, copied under whatever exe name a test needs. It reads mode.txt beside
// itself: "ok" shows a window until it is closed, "hang" shows one that refuses to close, "nowindow" runs
// without a window, "crash" exits at once with code 3. Every start appends "<pid> <mode>" to started.txt
// beside it, and a normal close appends "<pid>" to closed.txt. Windows open far off-screen and never take
// focus, and every mode exits by itself after two minutes, so a failed test can't leave one behind.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DlbPrecision.DesktopClock.Tests
{
    internal static class FakeClock
    {
        [STAThread]
        private static int Main()
        {
            string folder = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string modeFile = Path.Combine(folder, "mode.txt");
            string mode = File.Exists(modeFile) ? File.ReadAllText(modeFile).Trim() : "ok";
            int self = Process.GetCurrentProcess().Id;
            File.AppendAllText(Path.Combine(folder, "started.txt"), self + " " + mode + Environment.NewLine);
            if (mode == "crash") return 3;
            if (mode == "nowindow")
            {
                Thread.Sleep(TimeSpan.FromMinutes(2));
                return 0;
            }

            var app = new Application();
            var window = new Window
            {
                Title = "DLB Precision Desktop Clock",
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
            File.AppendAllText(Path.Combine(folder, "closed.txt"), self + Environment.NewLine);
            return 0;
        }
    }
}
