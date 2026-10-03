// DLB Precision Desktop Clock: a transparent, rectangular, always-on-top clock widget for Windows 11
// in Rapid Blue, purple, or DLB Precision colors. Drag to move, drag edges to resize, right-click for
// options. Remembers position/size/color in %LOCALAPPDATA%\DLBPrecision\DesktopClock\settings.ini.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DlbPrecision.DesktopClock
{
    public class ClockWindow : Window
    {
        // ---------- win32 ----------
        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDBLCLK = 0x00A3;
        const int WM_NCRBUTTONUP = 0x00A5;
        const int WM_SIZING = 0x0214;
        const int WM_EXITSIZEMOVE = 0x0232;
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
                  WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
        const int HTCLIENT = 1;
        const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                  HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetWindowLong(IntPtr hWnd, int i);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int SetWindowLong(IntPtr hWnd, int i, int v);

        // ---------- look ----------
        static readonly Color RapidBlue = Color.FromRgb(0x2E, 0x6B, 0xE6);
        // DLB Precision palette: the dlbprecision.com hero gradient, plus the logo's violet for the
        // solid purple. The gradient's own #7B00FF end is too dim as a full digit color on a dark
        // wallpaper; #8B43FF matches Rapid Blue's contrast on both dark and light backgrounds.
        static readonly Color DlbSky = Color.FromRgb(0x2D, 0xA4, 0xF4);
        static readonly Color DlbBlue = Color.FromRgb(0x4A, 0x66, 0xF7);
        static readonly Color DlbViolet = Color.FromRgb(0x7B, 0x00, 0xFF);
        static readonly Color LogoViolet = Color.FromRgb(0x8B, 0x43, 0xFF);

        class Theme
        {
            public string Key, Name;
            public Color Edge;                   // tint of the hover outline
            public Func<double, Brush> Fill;     // digit brush, given the width of the text it paints
        }

        static readonly Theme[] Themes =
        {
            new Theme { Key = "rapid", Name = "Rapid Blue", Edge = RapidBlue, Fill = w => Solid(RapidBlue) },
            new Theme { Key = "purple", Name = "Purple", Edge = LogoViolet, Fill = w => Solid(LogoViolet) },
            new Theme { Key = "dlb", Name = "DLB Precision", Edge = DlbBlue, Fill = DlbGradient },
        };

        static Brush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // One continuous sweep from the first digit to the end of AM/PM: sky blue to blue, then
        // solid violet from the middle on. Absolute, not relative, mapping: AM/PM is a separate
        // run, and a relative brush would restart the sweep on it.
        static Brush DlbGradient(double width)
        {
            LinearGradientBrush b = new LinearGradientBrush();
            b.MappingMode = BrushMappingMode.Absolute;
            b.StartPoint = new Point(0, 0);
            b.EndPoint = new Point(width, 0);
            b.GradientStops.Add(new GradientStop(DlbSky, 0.0));
            b.GradientStops.Add(new GradientStop(DlbBlue, 0.22));
            b.GradientStops.Add(new GradientStop(DlbViolet, 0.55));
            b.GradientStops.Add(new GradientStop(DlbViolet, 1.0));
            b.Freeze();
            return b;
        }

        const int GRIP = 10;                 // resize grab band, pixels
        const double DEF_W = 340;
        const double MIN_W = 130, MAX_W = 3000;
        const double STEP = 1.12;            // one wheel notch / one "Bigger" click

        // ---------- startup registration ----------
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string RunValueName = "DLBPrecisionDesktopClock";

        // Window positions belong to this PC's monitors, so settings are local rather than roaming.
        internal static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DLBPrecision", "DesktopClock", "settings.ini");

        readonly TextBlock text = new TextBlock();
        readonly Run timeRun = new Run("12:00:00");
        readonly Run ampmRun = new Run(" AM");
        readonly Grid holder = new Grid();
        readonly Border frame = new Border();
        readonly DispatcherTimer tick = new DispatcherTimer(DispatcherPriority.Normal);
        readonly DispatcherTimer saveDebounce = new DispatcherTimer();
        readonly DispatcherTimer replace = new DispatcherTimer(DispatcherPriority.Background);
        MenuItem lockItem, startupItem, colorItem;
        Theme theme = Themes[0];
        Brush hoverEdge = Brushes.Transparent;
        double readoutWidth;                 // width reserved for the widest reading
        double aspect = 3.0;                 // width : height of the readout, held while resizing
        // "Home" is where the user last put the clock. Only a user action (drag, edge resize,
        // wheel, menu) changes it. Windows shoving the window around when a monitor drops out,
        // or us parking it because home is momentarily off-screen, does not.
        double homeL = double.NaN, homeT = double.NaN, homeW = DEF_W;
        bool startup = true;                 // wants the clock at login; the Run entry is rebuilt from this
        bool locked;

        public ClockWindow()
        {
            Title = "DLB Precision Desktop Clock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;   // transparent, but still catches the mouse
            ResizeMode = ResizeMode.CanResize;  // keeps the sizing frame; we hit-test it ourselves
            ShowInTaskbar = false;
            ShowActivated = false;              // starting at sign-in or after an update never steals focus
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            Cursor = Cursors.SizeAll;

            BuildContent();
            BuildMenu();
            LoadSettings();
            ApplyStartup();

            saveDebounce.Interval = TimeSpan.FromMilliseconds(700);
            saveDebounce.Tick += delegate { saveDebounce.Stop(); Save(); };
            replace.Interval = TimeSpan.FromMilliseconds(1500);   // let the display topology settle
            replace.Tick += delegate { replace.Stop(); Place(); };
            SystemEvents.SessionEnding += delegate { Save(); };

            MouseLeftButtonDown += OnLeftDown;
            MouseWheel += OnWheel;
            MouseEnter += delegate { frame.BorderBrush = locked ? Brushes.Transparent : hoverEdge; };
            MouseLeave += delegate { frame.BorderBrush = Brushes.Transparent; };

            tick.Tick += OnTick;
            UpdateTime();
            Schedule();

            // After an update the previous version stays behind as <exe>.old until the updater, which runs from
            // it, has exited. Try again shortly after starting.
            DispatcherTimer tidy = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            tidy.Tick += delegate { tidy.Stop(); DeleteUpdateBackup(); };
            tidy.Start();
        }

        static void DeleteUpdateBackup()
        {
            try { File.Delete(ExePath() + ".old"); }
            catch (IOException) { /* Still in use; the next start or update removes it. */ }
            catch (UnauthorizedAccessException) { }
        }

        void BuildContent()
        {
            text.FontFamily = new FontFamily("Segoe UI");
            text.FontWeight = FontWeights.Light;
            text.FontSize = 100;
            // Segoe UI Light's default "1" is narrower than the other digits, so the centered
            // readout slid sideways whenever a 1 came or went. Tabular figures are all one width.
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            text.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.7,
                RenderingBias = RenderingBias.Performance
            };
            ampmRun.FontSize = 42;
            text.Inlines.Add(timeRun);
            text.Inlines.Add(ampmRun);

            // Reserve the width of the widest possible reading so the Viewbox scale never changes
            // -- otherwise the digits would jump in size at 10:00 and 1:00. With tabular figures
            // every two-digit hour reads the same width, but "AM" is wider than "PM", so measure
            // both. Layout rounding later rounds the text up to whole pixels; reserve that too.
            Size widest = new Size(0, 0);
            foreach (string half in new[] { " AM", " PM" })
            {
                timeRun.Text = "10:00:00"; ampmRun.Text = half;
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                widest = new Size(Math.Max(widest.Width, text.DesiredSize.Width),
                                  Math.Max(widest.Height, text.DesiredSize.Height));
            }
            widest = new Size(Math.Ceiling(widest.Width), Math.Ceiling(widest.Height));
            readoutWidth = widest.Width;

            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            // The digit brush is sized to the text, so a gradient always ends on AM/PM. Rebuild it
            // when the text's width changes: one- vs two-digit hour, AM vs PM.
            text.SizeChanged += (s, e) => { if (e.WidthChanged) text.Foreground = theme.Fill(TextWidth()); };
            holder.Width = widest.Width;
            holder.Height = widest.Height;
            holder.Children.Add(text);

            Viewbox box = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = holder,
                Margin = new Thickness(8, 5, 8, 5)   // breathing room for the shadow
            };

            // A faint outline appears on hover so the drag-to-resize edges are findable
            // on an otherwise invisible window.
            frame.Background = Brushes.Transparent;
            frame.BorderBrush = Brushes.Transparent;
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(6);
            frame.Child = box;
            Content = frame;

            aspect = (widest.Width + 16) / (widest.Height + 10);
            MinWidth = MIN_W;
            MinHeight = MIN_W / aspect;
        }

        // ---------- sizing ----------
        // Every route to a new size funnels through here, so the widget can only ever grow or
        // shrink proportionally -- it stays centred on where it already was.
        void SetWidth(double w)
        {
            if (locked) return;
            w = Clamp(w, MIN_W, MAX_W);
            double h = w / aspect;
            double curW = ActualWidth > 0 ? ActualWidth : Width;
            double curH = ActualHeight > 0 ? ActualHeight : Height;
            Left += (curW - w) / 2;
            Top += (curH - h) / 2;
            Width = w;
            Height = h;
            homeL = Left; homeT = Top; homeW = w;
            SaveSoon();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (locked) return;
            double f = e.Delta > 0 ? STEP : 1 / STEP;
            SetWidth((ActualWidth > 0 ? ActualWidth : Width) * f);
            e.Handled = true;
        }

        void BuildMenu()
        {
            ContextMenu menu = new ContextMenu();

            lockItem = new MenuItem { Header = "Lock position", IsCheckable = true };
            lockItem.Click += delegate { locked = lockItem.IsChecked; ApplyLock(); Save(); };

            startupItem = new MenuItem { IsCheckable = true };
            startupItem.Click += delegate { startup = startupItem.IsChecked; ApplyStartup(); Save(); };

            MenuItem bigger = new MenuItem { Header = "Bigger", InputGestureText = "scroll up" };
            bigger.Click += delegate { SetWidth((ActualWidth > 0 ? ActualWidth : Width) * STEP); };

            MenuItem smaller = new MenuItem { Header = "Smaller", InputGestureText = "scroll down" };
            smaller.Click += delegate { SetWidth((ActualWidth > 0 ? ActualWidth : Width) / STEP); };

            MenuItem reset = new MenuItem { Header = "Reset size" };
            reset.Click += delegate { SetWidth(DEF_W); };

            colorItem = new MenuItem { Header = "Color" };
            foreach (Theme t in Themes)
            {
                Theme pick = t;
                MenuItem mi = new MenuItem { Header = t.Name, Tag = t, IsCheckable = true };
                mi.Click += delegate { ApplyTheme(pick); Save(); };
                colorItem.Items.Add(mi);
            }

            MenuItem version = new MenuItem { Header = "Version " + Updater.UpdaterProgram.VersionText(Assembly.GetEntryAssembly().GetName().Version), IsEnabled = false };
            MenuItem update = new MenuItem { Header = "Check for updates…" };
            update.Click += delegate
            {
                string problem = UpdateLauncher.Start(ExePath(), delegate(ProcessStartInfo info) { using (Process.Start(info)) { } });
                if (problem != null) MessageBox.Show(problem, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            };

            MenuItem quit = new MenuItem { Header = "Exit" };
            quit.Click += delegate { Close(); };

            menu.Items.Add(bigger);
            menu.Items.Add(smaller);
            menu.Items.Add(reset);
            menu.Items.Add(new Separator());
            menu.Items.Add(colorItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(lockItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(version);
            menu.Items.Add(update);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);
            ContextMenu = menu;
        }

        void ApplyLock()
        {
            Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
            if (locked) frame.BorderBrush = Brushes.Transparent;
        }

        // Before the first layout the text has no width yet; the reserved width stands in until
        // SizeChanged supplies the real one.
        double TextWidth() { return text.ActualWidth > 0 ? text.ActualWidth : readoutWidth; }

        void ApplyTheme(Theme t)
        {
            theme = t;
            text.Foreground = t.Fill(TextWidth());
            hoverEdge = Solid(Color.FromArgb(0x70, t.Edge.R, t.Edge.G, t.Edge.B));
            if (IsMouseOver && !locked) frame.BorderBrush = hoverEdge;
            // Set every item explicitly: clicking a checkable item toggles it, which would otherwise
            // let a second click on the current color uncheck it.
            foreach (MenuItem mi in colorItem.Items) mi.IsChecked = mi.Tag == t;
        }

        // ---------- clock ----------
        void OnTick(object sender, EventArgs e) { UpdateTime(); Schedule(); }

        void UpdateTime()
        {
            DateTime n = DateTime.Now;
            timeRun.Text = n.ToString("h:mm:ss", CultureInfo.InvariantCulture);
            ampmRun.Text = " " + n.ToString("tt", CultureInfo.InvariantCulture);
        }

        void Schedule()
        {
            // Wake once per second, aligned to the second boundary, then go back to sleep.
            int ms = 1000 - DateTime.Now.Millisecond;
            if (ms < 40) ms += 1000;
            tick.Interval = TimeSpan.FromMilliseconds(ms);
            tick.Start();
        }

        // ---------- move / resize ----------
        void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            if (locked || e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW); // hide from Alt+Tab
            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null) src.AddHook(WndProc);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCLBUTTONDBLCLK)      // a double-click on an edge must not maximize us
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_NCRBUTTONUP)          // right-click landed on the resize band
            {
                handled = true;
                if (ContextMenu != null)
                {
                    ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    ContextMenu.IsOpen = true;
                }
                return IntPtr.Zero;
            }

            if (msg == WM_EXITSIZEMOVE)         // the user finished a drag or an edge resize
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CommitHome));
                return IntPtr.Zero;
            }

            if (msg == WM_DISPLAYCHANGE)        // a monitor appeared, vanished, or changed mode
            {
                replace.Stop();
                replace.Start();
                return IntPtr.Zero;
            }

            if (msg == WM_SIZING)               // hold the readout's proportions while dragging an edge
            {
                RECT s = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));
                int w = s.Right - s.Left, h = s.Bottom - s.Top;
                switch (wParam.ToInt32())
                {
                    case WMSZ_TOP:
                    case WMSZ_BOTTOM:
                        s.Right = s.Left + (int)Math.Round(h * aspect);
                        break;
                    case WMSZ_TOPLEFT:
                    case WMSZ_TOPRIGHT:
                        s.Top = s.Bottom - (int)Math.Round(w / aspect);
                        break;
                    default:                     // left, right, and the bottom corners
                        s.Bottom = s.Top + (int)Math.Round(w / aspect);
                        break;
                }
                Marshal.StructureToPtr(s, lParam, false);
                handled = true;
                return new IntPtr(1);
            }

            if (msg == WM_NCHITTEST)
            {
                if (locked) { handled = true; return new IntPtr(HTCLIENT); }

                RECT r;
                if (!GetWindowRect(hwnd, out r)) return IntPtr.Zero;
                long lp = lParam.ToInt64();
                int x = (short)(lp & 0xFFFF);
                int y = (short)((lp >> 16) & 0xFFFF);

                bool l = x < r.Left + GRIP, rt = x >= r.Right - GRIP;
                bool t = y < r.Top + GRIP, b = y >= r.Bottom - GRIP;

                int ht = HTCLIENT;                       // interior -> DragMove handles it
                if (t && l) ht = HTTOPLEFT;
                else if (t && rt) ht = HTTOPRIGHT;
                else if (b && l) ht = HTBOTTOMLEFT;
                else if (b && rt) ht = HTBOTTOMRIGHT;
                else if (l) ht = HTLEFT;
                else if (rt) ht = HTRIGHT;
                else if (t) ht = HTTOP;
                else if (b) ht = HTBOTTOM;

                handled = true;
                return new IntPtr(ht);
            }
            return IntPtr.Zero;
        }

        // ---------- footprint ----------
        // Startup JIT leaves ~90 MB of one-shot pages resident. Hand them back once the UI has
        // settled; the clock then idles at a fraction of that.
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            Trim();
            DispatcherTimer settle = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
            settle.Interval = TimeSpan.FromSeconds(20);
            settle.Tick += delegate { settle.Stop(); Trim(); };
            settle.Start();
        }

        static void Trim()
        {
            try { EmptyWorkingSet(GetCurrentProcess()); } catch { }
        }

        // ---------- position memory ----------
        void CommitHome()
        {
            homeL = Left;
            homeT = Top;
            homeW = ActualWidth > 0 ? ActualWidth : Width;
            SaveSoon();
        }

        // Put the clock at home if home is on a monitor right now. If it is not (the monitor is
        // still waking up, or is unplugged), leave the window wherever it is if that is visible,
        // otherwise park it top-right on the primary screen. Home itself is never touched here,
        // so the clock goes back the moment that monitor returns.
        void Place()
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            if (!double.IsNaN(homeL) && OnScreen(homeL, homeT, w, h))
            {
                // Left/Top are NaN until the window has been shown once; NaN compares false to
                // everything, so test for it explicitly or the first placement is silently skipped.
                bool unplaced = double.IsNaN(Left) || double.IsNaN(Top);
                if (unplaced || Math.Abs(Left - homeL) > 0.5 || Math.Abs(Top - homeT) > 0.5) { Left = homeL; Top = homeT; }
            }
            else if (!OnScreen(Left, Top, w, h))
            {
                Rect wa = SystemParameters.WorkArea;
                Left = Math.Max(wa.Left, wa.Right - w - 40);
                Top = wa.Top + 60;
            }
        }

        // ---------- settings ----------
        void SaveSoon() { saveDebounce.Stop(); saveDebounce.Start(); }

        void LoadSettings()
        {
            double l = double.NaN, t = double.NaN, w = double.NaN, h = double.NaN;
            string themeKey = null;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    foreach (string line in File.ReadAllLines(SettingsPath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "theme") { themeKey = v; continue; }
                        double d;
                        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) continue;
                        if (k == "left") l = d;
                        else if (k == "top") t = d;
                        else if (k == "width") w = d;
                        else if (k == "height") h = d;
                        else if (k == "locked") locked = d != 0;
                        else if (k == "startup") startup = d != 0;
                    }
                }
            }
            catch { }

            if (double.IsNaN(w)) w = double.IsNaN(h) ? DEF_W : h * aspect;
            homeW = Clamp(w, MIN_W, MAX_W);
            Width = homeW;
            Height = homeW / aspect;
            if (!double.IsNaN(l) && !double.IsNaN(t)) { homeL = l; homeT = t; }

            Place();
            if (double.IsNaN(homeL)) { homeL = Left; homeT = Top; }   // first run: adopt the parking spot

            lockItem.IsChecked = locked;
            ApplyLock();

            Theme saved = Themes[0];             // missing or unknown theme: Rapid Blue
            foreach (Theme th in Themes)
                if (string.Equals(th.Key, themeKey, StringComparison.OrdinalIgnoreCase)) saved = th;
            ApplyTheme(saved);
        }

        static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // True if a usable chunk of the saved rectangle still lands on some monitor.
        static bool OnScreen(double l, double t, double w, double h)
        {
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
            double ol = Math.Max(l, vl), ot = Math.Max(t, vt);
            double orr = Math.Min(l + w, vr), ob = Math.Min(t + h, vb);
            return (orr - ol) >= 60 && (ob - ot) >= 24;
        }

        void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, string.Format(CultureInfo.InvariantCulture,
                    "left={0:0.##}\r\ntop={1:0.##}\r\nwidth={2:0.##}\r\nheight={3:0.##}\r\nlocked={4}\r\nstartup={5}\r\ntheme={6}\r\n",
                    homeL, homeT, homeW, homeW / aspect, locked ? 1 : 0, startup ? 1 : 0, theme.Key));
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            saveDebounce.Stop();
            Save();
            base.OnClosing(e);
        }

        // ---------- run-at-login ----------
        static string ExePath()
        {
            try
            {
                Assembly a = Assembly.GetEntryAssembly();
                return a == null ? null : a.Location;
            }
            catch { return null; }
        }

        static string GetRunValue()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return k == null ? null : k.GetValue(RunValueName) as string;
            }
            catch { return null; }
        }

        static void SetRunValue(string value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null) return;
                    if (value != null) k.SetValue(RunValueName, value, RegistryValueKind.String);
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        // Re-assert the user's choice on every launch. Wanted but missing (a registry cleaner, a
        // profile restore, or it was written from a sandbox that redirected HKCU) or pointing at
        // an old location after the .exe moved: put it back. Not wanted: make sure it is gone.
        void ApplyStartup()
        {
            string want = "\"" + ExePath() + "\"";
            string cur = GetRunValue();
            if (startup) { if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) SetRunValue(want); }
            else if (cur != null) SetRunValue(null);
            startupItem.Header = startup ? "Start with Windows  (on)" : "Start with Windows  (off)";
            startupItem.IsChecked = startup;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (Updater.UpdaterProgram.IsUpdaterCommand(args)) return Updater.UpdaterProgram.Run(args);
            bool fresh;
            using (Mutex one = new Mutex(true, "DLBPrecision.DesktopClock.Widget", out fresh))
            {
                if (!fresh) return 0;               // already running (e.g. launched twice at login)
                LegacyMigration.Run(SettingsPath);  // before loading settings, so Chevy Clock's carry over
                DeleteUpdateBackup();
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnLastWindowClose;
                app.Run(new ClockWindow());
                GC.KeepAlive(one);
            }
            return 0;
        }
    }

    // The clock only starts the updater; all internet, download and install work happens in that separate process.
    internal static class UpdateLauncher
    {
        public static string Start(string exe, Action<ProcessStartInfo> start)
        {
            try
            {
                start(new ProcessStartInfo(exe, "--update") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                return null;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                return "The updater could not start: " + error.Message;
            }
        }
    }
}
