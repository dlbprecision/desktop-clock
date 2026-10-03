using System;
using System.IO;
using System.Text;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // Release information and the offer rules: what is offered, and from where files may come.
    internal static class FeedTests
    {
        private static readonly Version Installed = new Version(1, 3, 0, 0);

        public static void Run(TestContext t)
        {
            Offers(t);
            Addresses(t);
            Parsing(t);
            HttpStatus(t);
            Sources(t);
        }

        private static ReleaseInfo Release(string tag, bool draft = false, bool prerelease = false, string urlTag = null,
            bool exe = true, bool checksum = true, long size = 61440, long checksumSize = 97, string baseUrl = null,
            string exeName = UpdateOffer.ExeName)
        {
            string root = baseUrl ?? "https://github.com/dlbprecision/desktop-clock/releases/download/" + (urlTag ?? tag) + "/";
            var release = new ReleaseInfo { Tag = tag, Name = tag, Body = "Fixes", Draft = draft, Prerelease = prerelease };
            if (exe) release.Assets.Add(new ReleaseAsset { Name = exeName, Size = size, DownloadUrl = root + exeName });
            if (checksum) release.Assets.Add(new ReleaseAsset { Name = UpdateOffer.ChecksumName, Size = checksumSize, DownloadUrl = root + UpdateOffer.ChecksumName });
            return release;
        }

        private static OfferStatus Status(ReleaseInfo release, Version installed = null, bool prerelease = false, bool files = false, string prefix = null)
        {
            return UpdateOffer.Decide(release, installed ?? Installed, prerelease, files, prefix).Status;
        }

        private static void Offers(TestContext t)
        {
            UpdateDecision newer = UpdateOffer.Decide(Release("v1.3.1"), Installed, false, false, null);
            t.Check(newer.Status == OfferStatus.Available && newer.Offer.VersionText == "1.3.1"
                && newer.Offer.Exe.Name == "DlbPrecision.DesktopClock.exe" && newer.Offer.Checksum.Name == "DlbPrecision.DesktopClock.exe.sha256",
                "A newer Latest release with both files is offered");
            t.Check(Status(Release("v1.3.0")) == OfferStatus.UpToDate, "The running version is up to date");
            t.Check(Status(Release("v1.2.9")) == OfferStatus.UpToDate, "An older release is never offered as a downgrade");
            t.Check(Status(null) == OfferStatus.UpToDate, "No release at all is up to date");
            t.Check(Status(Release("v1.3.10"), new Version(1, 3, 9, 0)) == OfferStatus.Available, "Versions compare as numbers: 1.3.10 is newer than 1.3.9");
            t.Check(Status(Release("v1.3.0"), new Version(1, 2, 9, 9)) == OfferStatus.Available, "A four-part test build sorts below the next release");
            t.Check(Status(Release("v1.3.1", draft: true), prerelease: true) == OfferStatus.UpToDate, "Drafts are never offered");
            t.Check(Status(Release("v1.3.1", prerelease: true)) == OfferStatus.UpToDate, "Pre-releases are not offered on the real channel");
            t.Check(Status(Release("v1.3.1", prerelease: true), prerelease: true) == OfferStatus.Available, "A test feed may offer a pre-release");
            foreach (string tag in new[] { "1.3.1", "v1.3", "V1.3.1", "v1.3.1-beta", "v1.3.1.0", "v\u0661.\u0663.\u0661", "", null })
                t.Check(Status(Release(tag, urlTag: "v1.3.1")) == OfferStatus.NotAvailable,
                    "Tag '" + (tag ?? "(missing)") + "' is not vMAJOR.MINOR.PATCH and is refused");
            t.Check(Status(Release("v1.3.1", checksum: false)) == OfferStatus.NotAvailable, "A release without its checksum file is refused");
            t.Check(Status(Release("v1.3.1", exe: false)) == OfferStatus.NotAvailable, "A release without the clock is refused");
            t.Check(Status(Release("v1.3.1", exeName: "ChevyClock.exe")) == OfferStatus.NotAvailable, "The old Chevy Clock file name is not the clock");
            t.Check(Status(Release("v1.3.1", size: 0)) == OfferStatus.NotAvailable, "An empty exe is refused before downloading");
            t.Check(Status(Release("v1.3.1", size: 16L * 1024 * 1024 + 1)) == OfferStatus.NotAvailable, "An exe over 16 MiB is refused before downloading");
            t.Check(Status(Release("v1.3.1", checksumSize: 4097)) == OfferStatus.NotAvailable, "A checksum file over 4 KiB is refused");

            var titled = Release("v1.3.1");
            titled.Name = "v1.3.1";
            titled.Body = "## v1.3.1\n- **Faster** updates";
            t.Check(UpdateOffer.Decide(titled, Installed, false, false, null).Offer.Notes == "• Faster updates",
                "Notes are plain text and don't repeat the title");
            var untitled = Release("v1.3.1");
            untitled.Name = "";
            t.Check(UpdateOffer.Decide(untitled, Installed, false, false, null).Offer.Title == "DLB Precision Desktop Clock 1.3.1",
                "A release without a title gets one");
        }

        private static void Addresses(TestContext t)
        {
            string prefix = ReleaseFeed.DownloadPrefix;
            t.Check(prefix == "https://github.com/dlbprecision/desktop-clock/releases/download/", "The real feed downloads only from DLB's desktop-clock releases");
            t.Check(Status(Release("v1.3.1"), prefix: prefix) == OfferStatus.Available, "DLB's own release files are accepted");
            t.Check(Status(Release("v1.3.1", baseUrl: "http://github.com/dlbprecision/desktop-clock/releases/download/v1.3.1/")) == OfferStatus.NotAvailable,
                "Unencrypted download addresses are refused");
            t.Check(Status(Release("v1.3.1", baseUrl: "file:///C:/feed/v1.3.1/")) == OfferStatus.NotAvailable, "Local files are refused on the real channel");
            t.Check(Status(Release("v1.3.1", baseUrl: "file:///C:/feed/v1.3.1/"), files: true) == OfferStatus.Available, "A local test feed may use local files");
            t.Check(Status(Release("v1.3.1", baseUrl: "file://server/share/v1.3.1/"), files: true) == OfferStatus.NotAvailable,
                "Network-share downloads are refused even for a local test feed");
            t.Check(Status(Release("v1.3.1", baseUrl: "https://example.test/dlbprecision/desktop-clock/releases/download/v1.3.1/"), prefix: prefix) == OfferStatus.NotAvailable,
                "Another host is refused");
            t.Check(Status(Release("v1.3.1", baseUrl: prefix + "v1.3.1/../v9.9.9/"), prefix: prefix) == OfferStatus.NotAvailable,
                "An address that climbs out of the release with ../ is refused");
            t.Check(Status(Release("v1.3.1", baseUrl: "https://github.com/DLBPrecision/Desktop-Clock/releases/download/v1.3.1/"), prefix: prefix) == OfferStatus.Available,
                "Owner and repository ignore case, as GitHub does");
            t.Check(Status(Release("v1.3.1", baseUrl: prefix + "V1.3.1/"), prefix: prefix) == OfferStatus.NotAvailable, "The tag in the address must match exactly");
            t.Check(Status(Release("v1.3.1", urlTag: "v1.3.0"), prefix: prefix) == OfferStatus.NotAvailable, "Files from another release are refused");
            t.Check(Status(Release("v1.3.1", baseUrl: "https://github.com/dlbprecision/desktop-clock2/releases/download/v1.3.1/"), prefix: prefix) == OfferStatus.NotAvailable,
                "A repository whose name only starts like DLB's is refused");
            var renamed = Release("v1.3.1");
            renamed.Assets[0].DownloadUrl = prefix + "v1.3.1/dlbprecision.desktopclock.exe";
            t.Check(Status(renamed, prefix: prefix) == OfferStatus.NotAvailable, "The file name in the address must match exactly");
        }

        private static void Parsing(TestContext t)
        {
            string json = "{\"url\":\"https://api.github.com/x\",\"tag_name\":\"v1.3.1\",\"name\":\"v1.3.1\",\"body\":null,\"draft\":false,"
                + "\"prerelease\":false,\"author\":{\"login\":\"dlbprecision\"},\"assets\":[{\"name\":\"DlbPrecision.DesktopClock.exe\",\"size\":61440,"
                + "\"browser_download_url\":\"https://github.com/dlbprecision/desktop-clock/releases/download/v1.3.1/DlbPrecision.DesktopClock.exe\",\"uploader\":{\"id\":2}},null]}";
            ReleaseInfo parsed = ReleaseFeed.Parse(Encoding.UTF8.GetBytes(json));
            t.Check(parsed.Tag == "v1.3.1" && parsed.Name == "v1.3.1" && parsed.Body == "" && !parsed.Prerelease && !parsed.Draft
                && parsed.Assets.Count == 1 && parsed.Assets[0].Size == 61440 && parsed.Assets[0].DownloadUrl.EndsWith("/DlbPrecision.DesktopClock.exe"),
                "GitHub's reply is read, unknown fields ignored, null fields made empty and null assets dropped");
            ReleaseInfo bare = ReleaseFeed.Parse(Encoding.UTF8.GetBytes("{\"tag_name\":\"v1.3.1\"}"));
            t.Check(bare.Name == "" && bare.Body == "" && bare.Assets.Count == 0, "Missing fields arrive empty, not null");
            t.Check(Throws<InvalidDataException>(() => ReleaseFeed.Parse(Encoding.UTF8.GetBytes("{not json"))), "Invalid JSON is reported as unreadable");
            t.Check(Throws<InvalidDataException>(() => ReleaseFeed.Parse(new byte[0])), "An empty reply is unreadable");
            t.Check(Throws<InvalidDataException>(() => ReleaseFeed.Parse(new byte[ReleaseFeed.MaximumBytes + 1])), "A reply over 1 MiB is refused");
        }

        private static void HttpStatus(TestContext t)
        {
            t.Check(ReleaseFeed.FromHttpStatus(404, null).Error.Contains("couldn't be found"), "404 says the update information couldn't be found");
            t.Check(ReleaseFeed.FromHttpStatus(403, "0").Error.StartsWith("Too many update checks"), "403 with no requests left is rate limiting");
            t.Check(ReleaseFeed.FromHttpStatus(429, null).Error.StartsWith("Too many update checks"), "429 is rate limiting");
            t.Check(ReleaseFeed.FromHttpStatus(403, "57").Error.Contains("firewall"), "Any other 403 is a refusal, often a network filter");
            t.Check(ReleaseFeed.FromHttpStatus(503, null).Error.Contains("having trouble"), "5xx is a server problem");
            t.Check(ReleaseFeed.FromHttpStatus(418, null).Error.Contains("HTTP 418"), "Anything else names the HTTP status");
        }

        private static void Sources(TestContext t)
        {
            t.Check(ReleaseFeed.Fetch("https://127.0.0.1:1/releases/latest", "DLBPrecisionDesktopClock-Updater/test").Error == ReleaseFeed.NetworkMessage,
                "An unreachable server is a friendly network error");
            t.Check(ReleaseFeed.Fetch("http://127.0.0.1:1/releases/latest", "x").Error.Contains("secure connection"), "Plain HTTP feeds are refused before connecting");
            t.Check(ReleaseFeed.Fetch(@"\\server\share\feed.json", "x").Error.Contains("network share"), "A network-share feed is refused without touching the network");

            string folder = t.NewFolder("feed");
            string file = Path.Combine(folder, "feed.json");
            File.WriteAllText(file, "{\"tag_name\":\"v1.3.1\",\"name\":\"Test\",\"prerelease\":true,\"assets\":[]}");
            FeedResult local = ReleaseFeed.Fetch(file, "x");
            t.Check(local.Error == null && local.Release.Tag == "v1.3.1" && local.Release.Prerelease, "A local test feed file is read");
            t.Check(ReleaseFeed.Fetch(Path.Combine(folder, "missing.json"), "x").Error.Contains("not found"), "A missing test feed file is reported");
            t.Check(ReleaseFeed.IsLocal(file) && !ReleaseFeed.IsLocal(ReleaseFeed.LatestUrl), "Local feeds are told apart from the real one");
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return true; }
            return false;
        }
    }
}
