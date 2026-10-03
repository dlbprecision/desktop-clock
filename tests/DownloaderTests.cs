using System;
using System.IO;
using System.Threading;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // Downloads with local sources only: the copy loop, its limits, cancelling, and refused addresses.
    internal static class DownloaderTests
    {
        public static void Run(TestContext t)
        {
            byte[] data = new byte[200000];
            new Random(7).NextBytes(data);
            long last = 0;
            bool increasing = true;
            var output = new MemoryStream();
            Downloader.Copy(new MemoryStream(data), output, data.Length, Downloader.MaximumBytes,
                delegate(long total) { increasing &= total > last; last = total; }, CancellationToken.None);
            t.Check(output.Length == data.Length && last == data.Length && increasing, "Bytes are copied with steadily increasing progress");
            t.Check(Fails(() => Downloader.Copy(new MemoryStream(data), new MemoryStream(), data.Length + 1, Downloader.MaximumBytes, null, CancellationToken.None), "ended early"),
                "A download shorter than the release says is refused");
            t.Check(Fails(() => Downloader.Copy(new MemoryStream(data), new MemoryStream(), data.Length - 1, Downloader.MaximumBytes, null, CancellationToken.None), "larger than the release says"),
                "A download longer than the release says is refused");
            t.Check(Fails(() => Downloader.Copy(new MemoryStream(data), new MemoryStream(), 0, 1000, null, CancellationToken.None), "larger than the updater accepts"),
                "A download over the hard limit is refused");

            string source = Path.Combine(t.NewFolder("download"), "source.exe");
            File.WriteAllBytes(source, new byte[] { 77, 90, 1, 2, 3 });
            var local = new Uri(source);
            var copy = new MemoryStream();
            Downloader.Download(local, copy, 5, true, null, CancellationToken.None, "test");
            t.Check(copy.Length == 5, "A local test feed's file is downloaded");
            t.Check(Fails(() => Downloader.Download(local, new MemoryStream(), 5, false, null, CancellationToken.None, "test"), "local test feed"),
                "Local files are refused outside a local test feed");
            t.Check(Fails(() => Downloader.Download(new Uri("http://127.0.0.1:1/x.exe"), new MemoryStream(), 5, false, null, CancellationToken.None, "test"), "secure connection"),
                "Plain HTTP is refused");
            t.Check(Fails(() => Downloader.Download(new Uri("file://server/share/x.exe"), new MemoryStream(), 5, true, null, CancellationToken.None, "test"), "network share"),
                "Network shares are refused");

            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool stopped = false;
            try { Downloader.Download(local, new MemoryStream(), 5, true, null, cancelled.Token, "test"); }
            catch (OperationCanceledException) { stopped = true; }
            t.Check(stopped, "Cancelling stops the download");
        }

        private static bool Fails(Action action, string reason)
        {
            try { action(); }
            catch (InvalidDataException error) { return error.Message.Contains(reason); }
            return false;
        }
    }
}
