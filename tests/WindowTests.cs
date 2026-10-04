using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // Every state of the updater window, off-screen, plus a PNG of each in tests\bin\previews for a visual check.
    internal static class WindowTests
    {
        public static void Run(TestContext t)
        {
            var window = new UpdaterWindow(@"C:\nowhere\DlbPrecision.DesktopClock.exe", null)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000
            };
            window.Show();
            string previews = Path.Combine(Path.GetDirectoryName(typeof(WindowTests).Assembly.Location), "previews");
            Directory.CreateDirectory(previews);

            window.ShowChecking();
            Save(window, previews, "checking");
            t.Check(!Visible(window.Primary) && (string)window.Secondary.Content == "Close", "Checking can be closed and offers nothing to install");

            window.ShowUpToDate("1.3.0");
            Save(window, previews, "up-to-date");
            t.Check(window.Status.Text == "You're up to date (1.3.0).", "Up to date names the running version");

            var release = new ReleaseInfo { Tag = "v1.3.1", Name = "v1.3.1", Body = "## v1.3.1\nAdds a **Color** menu.\n\n- Rapid Blue\n- Purple\n- DLB Precision" };
            release.Assets.Add(new ReleaseAsset { Name = UpdateOffer.ExeName, Size = 61440, DownloadUrl = ReleaseFeed.DownloadPrefix + "v1.3.1/" + UpdateOffer.ExeName });
            release.Assets.Add(new ReleaseAsset { Name = UpdateOffer.ChecksumName, Size = 97, DownloadUrl = ReleaseFeed.DownloadPrefix + "v1.3.1/" + UpdateOffer.ChecksumName });
            UpdateOffer offer = UpdateOffer.Decide(release, new Version(1, 3, 0, 0), false, false).Offer;
            window.ShowAvailable(offer);
            Save(window, previews, "available");
            t.Check(window.Status.Text == "Version 1.3.1 is available" && Visible(window.Notes) && window.Notes.Text.Contains("• Purple")
                && (string)window.Primary.Content == "Update now" && window.Primary.IsDefault && (string)window.Secondary.Content == "Not now",
                "An offer shows its version and notes, and Enter means Update now");

            window.ShowDownloading(41 * 1024, 63 * 1024);
            Save(window, previews, "downloading");
            t.Check(Visible(window.Progress) && window.ProgressText.Text == "41 of 63 KB" && !Visible(window.Primary)
                && (string)window.Secondary.Content == "Cancel" && !window.Secondary.IsDefault,
                "Downloading shows progress, can be cancelled, and Enter can't cancel it");

            window.ShowVerifying();
            Save(window, previews, "verifying");
            window.Close();
            t.Check(window.IsVisible && !Visible(window.Primary) && !Visible(window.Secondary), "Verifying can't be interrupted, not even by closing the window");

            window.ShowRestarting();
            Save(window, previews, "restarting");
            window.Close();
            t.Check(window.IsVisible, "Restarting can't be interrupted");

            window.ShowError("Couldn't reach the update server. Check your internet connection.", delegate { });
            Save(window, previews, "error");
            t.Check((string)window.Primary.Content == "Try again" && Visible(window.Primary), "Errors that may pass offer Try again");
            window.ShowError("The download couldn't be verified, so it wasn't installed.", null);
            t.Check(!Visible(window.Primary), "Other errors only offer Close");

            window.ShowResult("The new version didn't start, so the previous version was put back.");
            Save(window, previews, "rolled-back");
            window.Close();
            t.Check(!window.IsVisible, "A result can be closed");
        }

        private static bool Visible(UIElement element)
        {
            return element.Visibility == Visibility.Visible;
        }

        private static void Save(Window window, string folder, string name)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(Path.Combine(folder, "updater-" + name + ".png"))) png.Save(file);
        }
    }
}
