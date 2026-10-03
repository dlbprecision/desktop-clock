using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DlbPrecision.DesktopClock.Updater
{
    // Only the GitHub release fields the updater uses; every other field in the reply is ignored.
    [DataContract]
    internal sealed class ReleaseInfo
    {
        public ReleaseInfo()
        {
            Tag = "";
            Name = "";
            Body = "";
            Assets = new List<ReleaseAsset>();
        }

        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<ReleaseAsset> Assets { get; set; }
    }

    [DataContract]
    internal sealed class ReleaseAsset
    {
        public ReleaseAsset()
        {
            Name = "";
            DownloadUrl = "";
        }

        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
    }

    // Either the release, or a message to show.
    internal sealed class FeedResult
    {
        private FeedResult(ReleaseInfo release, string error)
        {
            Release = release;
            Error = error;
        }

        public ReleaseInfo Release { get; private set; }
        public string Error { get; private set; }

        public static FeedResult Found(ReleaseInfo release) { return new FeedResult(release, null); }
        public static FeedResult Failed(string error) { return new FeedResult(null, error); }
    }

    internal static class ReleaseFeed
    {
        public const string Repository = "dlbprecision/desktop-clock";
        public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
        public const string NetworkMessage = "Couldn't reach the update server. Check your internet connection.";
        internal const int MaximumBytes = 1024 * 1024;
        private const int TimeoutMilliseconds = 15000;
        private const string Unreadable = "The update information could not be read.";

        // The in-box compiler doesn't stamp a target framework, so .NET would fall back to TLS 1.0, which
        // GitHub refuses. Let Windows choose instead: TLS 1.2 and 1.3 on Windows 10 and 11.
        public static void UseSystemTls()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
        }

        public static bool IsLocal(string source)
        {
            Uri uri;
            return !Uri.TryCreate(source, UriKind.Absolute, out uri) || uri.IsFile;
        }

        // One request per check. A local file is accepted only as an explicit --feed for testing.
        public static FeedResult Fetch(string source)
        {
            try
            {
                if (IsLocal(source)) return FetchLocal(source);
                var uri = new Uri(source);
                if (uri.Scheme != Uri.UriSchemeHttps) return FeedResult.Failed("Updates are only checked over a secure connection.");
                HttpWebRequest request = Downloader.CreateRequest(uri, TimeoutMilliseconds);
                request.Accept = "application/vnd.github+json";
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var body = new MemoryStream())
                {
                    if (response.ResponseUri.Scheme != Uri.UriSchemeHttps) return FeedResult.Failed("The update server redirected to an insecure address.");
                    using (Stream input = response.GetResponseStream())
                        Downloader.Copy(input, body, 0, MaximumBytes, null, System.Threading.CancellationToken.None);
                    return FeedResult.Found(Parse(body.ToArray()));
                }
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null)
                    using (response)
                        return FromHttpStatus((int)response.StatusCode, response.Headers["X-RateLimit-Remaining"]);
                if (error.Status == WebExceptionStatus.TrustFailure || error.Status == WebExceptionStatus.SecureChannelFailure)
                    return FeedResult.Failed("Couldn't make a secure connection to the update server. "
                        + "Check your internet connection and that this PC's date and time are correct.");
                return FeedResult.Failed(NetworkMessage);
            }
            catch (InvalidDataException) { return FeedResult.Failed(Unreadable); }
            catch (IOException) { return FeedResult.Failed(Unreadable); }
            catch (UnauthorizedAccessException) { return FeedResult.Failed(Unreadable); }
            catch (UriFormatException) { return FeedResult.Failed(Unreadable); }
        }

        private static FeedResult FetchLocal(string source)
        {
            Uri uri;
            bool absolute = Uri.TryCreate(source, UriKind.Absolute, out uri);
            // A network share would make Windows send this PC's sign-in to that server.
            if (source.StartsWith(@"\\", StringComparison.Ordinal) || (absolute && uri.IsUnc))
                return FeedResult.Failed("A network share can't be used as a test update feed.");
            var file = new FileInfo(absolute ? uri.LocalPath : Path.GetFullPath(source));
            if (!file.Exists) return FeedResult.Failed("The test update feed file was not found.");
            if (file.Length > MaximumBytes) return FeedResult.Failed("The test update feed file is too large.");
            return FeedResult.Found(Parse(File.ReadAllBytes(file.FullName)));
        }

        // GitHub allows 60 anonymous checks an hour per internet address, which a shared network can use up.
        public static FeedResult FromHttpStatus(int status, string remaining)
        {
            if (status == 404) return FeedResult.Failed("Update information couldn't be found (HTTP 404). Try again later.");
            if (status == 429 || (status == 403 && remaining != null && remaining.Trim() == "0"))
                return FeedResult.Failed("Too many update checks from this network. Try again in an hour.");
            if (status == 403) return FeedResult.Failed("The update server refused the request (HTTP 403). A firewall or network filter may be blocking GitHub.");
            if (status >= 500) return FeedResult.Failed("The update server is having trouble. Try again later.");
            return FeedResult.Failed("The update server gave an unexpected reply (HTTP " + status + ").");
        }

        public static ReleaseInfo Parse(byte[] json)
        {
            if (json.Length == 0 || json.Length > MaximumBytes) throw new InvalidDataException("The release description has an invalid size.");
            try
            {
                using (var stream = new MemoryStream(json, false))
                {
                    var release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(stream);
                    if (release == null) throw new InvalidDataException("The release description is empty.");
                    // The serializer skips constructors, so absent or null fields arrive as null.
                    release.Tag = release.Tag ?? "";
                    release.Name = release.Name ?? "";
                    release.Body = release.Body ?? "";
                    release.Assets = release.Assets ?? new List<ReleaseAsset>();
                    release.Assets.RemoveAll(asset => asset == null);
                    foreach (ReleaseAsset asset in release.Assets)
                    {
                        asset.Name = asset.Name ?? "";
                        asset.DownloadUrl = asset.DownloadUrl ?? "";
                    }
                    return release;
                }
            }
            catch (SerializationException error) { throw new InvalidDataException("The release description is not valid.", error); }
            catch (System.Xml.XmlException error) { throw new InvalidDataException("The release description is not valid.", error); }
        }
    }
}
