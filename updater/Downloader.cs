using System;
using System.IO;
using System.Net;
using System.Threading;

namespace DlbPrecision.DesktopClock.Updater
{
    internal static class Downloader
    {
        // The clock is well under 100 KB; anything near this limit is not the clock.
        public const long MaximumBytes = 16L * 1024 * 1024;
        // A download that makes no progress for this long is treated as stalled.
        private const int StallTimeoutMilliseconds = 30000;

        // Writes the file at source into output. The caller owns output, and deletes it if this throws.
        public static void Download(Uri source, Stream output, long expectedSize, bool allowFile, Action<long> progress,
            CancellationToken token, string userAgent)
        {
            if (source.IsFile && !allowFile) throw new InvalidDataException("Local files can only be used by a local test feed.");
            if (source.IsUnc) throw new InvalidDataException("Updates are never downloaded from a network share.");
            if (!source.IsFile && source.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Updates are only downloaded over a secure connection.");
            if (source.IsFile)
            {
                using (var input = new FileStream(source.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Copy(input, output, expectedSize, MaximumBytes, progress, token);
                return;
            }
            var request = (HttpWebRequest)WebRequest.Create(source);
            request.UserAgent = userAgent;
            request.Timeout = StallTimeoutMilliseconds;
            request.ReadWriteTimeout = StallTimeoutMilliseconds;
            UseSignInForProxy(request);
            try
            {
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    // GitHub redirects release files to its download host; never accept a downgrade to plain HTTP.
                    if (response.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("The download was redirected to an insecure address.");
                    using (Stream input = response.GetResponseStream())
                        Copy(input, output, expectedSize, MaximumBytes, progress, token);
                }
            }
            catch (WebException)
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                throw;
            }
        }

        // Office networks often require Windows sign-in at the proxy; without it every request fails with 407.
        internal static void UseSignInForProxy(HttpWebRequest request)
        {
            if (request.Proxy != null) request.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
        }

        public static void Copy(Stream input, Stream output, long expectedSize, long maximumBytes, Action<long> progress, CancellationToken token)
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                total += read;
                if (total > maximumBytes) throw new InvalidDataException("The download is larger than the updater accepts.");
                if (expectedSize > 0 && total > expectedSize) throw new InvalidDataException("The download is larger than the release says it should be.");
                output.Write(buffer, 0, read);
                if (progress != null) progress(total);
            }
            token.ThrowIfCancellationRequested();
            if (expectedSize > 0 && total != expectedSize) throw new InvalidDataException("The download ended early. Try again.");
        }
    }
}
