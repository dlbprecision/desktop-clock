using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DlbPrecision.DesktopClock.Updater
{
    // The updater's one window: check, offer, download, verify, replace. Each Show* method puts it in one state.
    internal sealed class UpdaterWindow : Window
    {
        public const string WindowTitle = "DLB Precision Desktop Clock Update";

        internal readonly TextBlock Status = new TextBlock();
        internal readonly TextBlock Detail = new TextBlock();
        internal readonly TextBox Notes = new TextBox();
        internal readonly ProgressBar Progress = new ProgressBar();
        internal readonly TextBlock ProgressText = new TextBlock();
        internal readonly Button Primary = new Button();
        internal readonly Button Secondary = new Button();

        private readonly string exe;
        private readonly string feed;
        private readonly bool localFeed;
        private readonly Version installed = Assembly.GetEntryAssembly().GetName().Version;
        private readonly string userAgent;
        private Action primaryAction, secondaryAction;
        private CancellationTokenSource cancel;
        private bool busy;   // verifying or replacing: can't be interrupted

        // exe: the clock to update (this process's own file). feed: a test feed, or null for GitHub's latest release.
        public UpdaterWindow(string exe, string feed, bool startCheck)
        {
            this.exe = exe;
            this.feed = feed;
            localFeed = feed != null && ReleaseFeed.IsLocal(feed);
            userAgent = "DLBPrecisionDesktopClock-Updater/" + UpdaterProgram.VersionText(installed);

            Title = WindowTitle;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;   // the clock is always on top; this must not open behind it

            var accent = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            accent.GradientStops.Add(new GradientStop(Color.FromRgb(0x2D, 0xA4, 0xF4), 0));
            accent.GradientStops.Add(new GradientStop(Color.FromRgb(0x7B, 0x00, 0xFF), 1));

            Status.FontSize = 16;
            Status.FontWeight = FontWeights.SemiBold;
            Status.TextWrapping = TextWrapping.Wrap;
            Status.Margin = new Thickness(20, 16, 20, 6);
            Detail.TextWrapping = TextWrapping.Wrap;
            Detail.Margin = new Thickness(20, 0, 20, 8);
            Notes.IsReadOnly = true;
            Notes.TextWrapping = TextWrapping.Wrap;
            Notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Notes.MinHeight = 80;
            Notes.MaxHeight = 220;
            Notes.Margin = new Thickness(20, 0, 20, 8);
            Progress.Height = 8;
            Progress.Margin = new Thickness(20, 4, 20, 0);
            ProgressText.Margin = new Thickness(20, 4, 20, 8);
            foreach (Button button in new[] { Primary, Secondary })
            {
                button.MinWidth = 96;
                button.Padding = new Thickness(12, 4, 12, 4);
                button.Margin = new Thickness(8, 0, 0, 0);
            }
            Primary.Click += delegate { if (primaryAction != null) primaryAction(); };
            Secondary.Click += delegate { if (secondaryAction != null) secondaryAction(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 8, 20, 16) };
            buttons.Children.Add(Primary);
            buttons.Children.Add(Secondary);

            var layout = new StackPanel();
            layout.Children.Add(new Border { Height = 4, Background = accent });
            foreach (UIElement part in new UIElement[] { Status, Detail, Notes, Progress, ProgressText, buttons }) layout.Children.Add(part);
            Content = layout;

            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (busy) e.Cancel = true;
                else if (cancel != null) cancel.Cancel();
            };
            ShowChecking();
            if (startCheck) Loaded += delegate { Check(); };
        }

        public void ShowChecking() { SetState("Checking for updates…", null, null, false, null, null, "Close", Close); }

        public void ShowUpToDate(string version) { SetState("You're up to date (" + version + ").", null, null, false, null, null, "Close", Close); }

        public void ShowAvailable(UpdateOffer offer)
        {
            SetState("Version " + offer.VersionText + " is available", offer.Title, offer.Notes, false, "Update now", delegate { UpdateNow(offer); }, "Not now", Close);
            Primary.IsDefault = true;
            Primary.Focus();
        }

        public void ShowDownloading(long done, long total)
        {
            SetState("Downloading the update…", null, null, true, null, null, "Cancel", delegate { if (cancel != null) cancel.Cancel(); });
            ShowProgress(done, total);
        }

        public void ShowVerifying() { SetState("Checking the download is genuine…", null, null, false, null, null, null, null); busy = true; }

        public void ShowRestarting() { SetState("Restarting the clock…", null, null, false, null, null, null, null); busy = true; }

        public void ShowResult(string message) { SetState(message, null, null, false, null, null, "Close", Close); }

        // retry is null when trying again can't help.
        public void ShowError(string message, Action retry) { SetState(message, null, null, false, retry == null ? null : "Try again", retry, "Close", Close); }

        private void SetState(string status, string detail, string notes, bool progress, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            busy = false;
            Status.Text = status;
            Detail.Text = detail ?? "";
            Detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            Notes.Text = notes ?? "";
            Notes.Visibility = string.IsNullOrEmpty(notes) ? Visibility.Collapsed : Visibility.Visible;
            Progress.Visibility = ProgressText.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
            Primary.Content = primary;
            Primary.Visibility = primary == null ? Visibility.Collapsed : Visibility.Visible;
            Primary.IsDefault = false;
            primaryAction = onPrimary;
            Secondary.Content = secondary;
            Secondary.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
            Secondary.IsCancel = secondary != null;
            secondaryAction = onSecondary;
        }

        private void ShowProgress(long done, long total)
        {
            Progress.Maximum = Math.Max(1, total);
            Progress.Value = done;
            ProgressText.Text = Math.Round(done / 1024.0) + " of " + Math.Round(total / 1024.0) + " KB";
        }

        private async void Check()
        {
            ShowChecking();
            string source = feed ?? ReleaseFeed.LatestUrl;
            FeedResult result = await Task.Run(() => ReleaseFeed.Fetch(source, userAgent));
            if (result.Error != null)
            {
                ShowError(result.Error, Check);
                return;
            }
            // A test feed may offer a pre-release; only a local one may point at local files.
            UpdateDecision decision = UpdateOffer.Decide(result.Release, installed, feed != null, localFeed, localFeed ? null : ReleaseFeed.DownloadPrefix);
            if (decision.Status == OfferStatus.UpToDate) ShowUpToDate(UpdaterProgram.VersionText(installed));
            else if (decision.Status == OfferStatus.NotAvailable) ShowError("This update isn't available yet. Try again later.", null);
            else ShowAvailable(decision.Offer);
        }

        private async void UpdateNow(UpdateOffer offer)
        {
            string fresh = ClockSwapper.NewPath(exe);
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            Action retry = delegate { UpdateNow(offer); };
            ShowDownloading(0, offer.Exe.Size);
            try
            {
                ClockSwapper.RemoveLeftovers(exe);
                string sha256 = await Task.Run(() => DownloadChecksum(offer, token));
                await Task.Run(() =>
                {
                    using (var file = new FileStream(fresh, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        Downloader.Download(new Uri(offer.Exe.DownloadUrl), file, offer.Exe.Size, localFeed,
                            done => Dispatcher.BeginInvoke(new Action(() => ShowProgress(done, offer.Exe.Size))), token, userAgent);
                });
                cancel = null;

                ShowVerifying();
                VerificationResult verdict = await Task.Run(() =>
                {
                    using (var file = new FileStream(fresh, FileMode.Open, FileAccess.Read, FileShare.Read))
                        return PackageVerifier.Verify(file, fresh, sha256, offer.VersionText);
                });
                if (!verdict.Ok)
                {
                    Discard(fresh);
                    ShowError(verdict.Reason, verdict.Retryable ? retry : null);
                    return;
                }

                ShowRestarting();
                SwapResult swap = await Task.Run(() => new ClockSwapper().Replace(exe));
                busy = false;
                if (swap.Outcome == SwapOutcome.Updated) Close();
                else ShowResult(swap.Message);
            }
            catch (OperationCanceledException) { Discard(fresh); ShowAvailable(offer); }
            catch (InvalidDataException error) { Discard(fresh); ShowError(error.Message, retry); }
            catch (WebException) { Discard(fresh); ShowError(ReleaseFeed.NetworkMessage, retry); }
            catch (IOException error) { Discard(fresh); ShowError("Couldn't save the update next to the clock (" + error.Message.TrimEnd('.') + ").", retry); }
            catch (UnauthorizedAccessException error) { Discard(fresh); ShowError("Couldn't save the update next to the clock (" + error.Message.TrimEnd('.') + ").", retry); }
            finally { cancel = null; }
        }

        private string DownloadChecksum(UpdateOffer offer, CancellationToken token)
        {
            using (var text = new MemoryStream())
            {
                Downloader.Download(new Uri(offer.Checksum.DownloadUrl), text, offer.Checksum.Size, localFeed, null, token, userAgent);
                string sha256;
                if (!ChecksumFile.TryParse(Encoding.UTF8.GetString(text.ToArray()), UpdateOffer.ExeName, out sha256))
                    throw new InvalidDataException("The update's checksum file couldn't be read.");
                return sha256;
            }
        }

        private static void Discard(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { /* Removed by the next update. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
