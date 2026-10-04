using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DlbPrecision.DesktopClock.Updater
{
    internal enum OfferStatus { UpToDate, Available, NotAvailable }

    internal sealed class UpdateDecision
    {
        private UpdateDecision(OfferStatus status, UpdateOffer offer)
        {
            Status = status;
            Offer = offer;
        }

        public OfferStatus Status { get; private set; }
        public UpdateOffer Offer { get; private set; }

        internal static UpdateDecision UpToDate() { return new UpdateDecision(OfferStatus.UpToDate, null); }
        internal static UpdateDecision NotAvailable() { return new UpdateDecision(OfferStatus.NotAvailable, null); }
        internal static UpdateDecision Available(UpdateOffer offer) { return new UpdateDecision(OfferStatus.Available, offer); }
    }

    internal sealed class UpdateOffer
    {
        // Fixed in every installed copy: see the contract in CODE-SIGNING.md.
        public const string ExeName = "DlbPrecision.DesktopClock.exe";
        public const string ChecksumName = ExeName + ".sha256";
        // Release text shown in the window; longer is cut, so the window always fits on the screen.
        private const int MaximumTitleLength = 120;
        private const int MaximumNotesLength = 4000;
        // [0-9], not \d: \d also matches other scripts' digits, which int.Parse rejects. \z, not $: $ allows a final newline.
        private static readonly Regex TagPattern = new Regex(@"^v([0-9]{1,4})\.([0-9]{1,4})\.([0-9]{1,5})\z", RegexOptions.CultureInvariant);

        private UpdateOffer(string versionText, string title, string notes, ReleaseAsset exe, ReleaseAsset checksum)
        {
            VersionText = versionText;
            Title = title;
            Notes = notes;
            Exe = exe;
            Checksum = checksum;
        }

        public string VersionText { get; private set; }
        public string Title { get; private set; }
        public string Notes { get; private set; }
        public ReleaseAsset Exe { get; private set; }
        public ReleaseAsset Checksum { get; private set; }

        public static bool TryParseTag(string tag, out Version version)
        {
            version = new Version(0, 0, 0);
            Match match = TagPattern.Match(tag ?? "");
            if (!match.Success) return false;
            version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
            return true;
        }

        // Compared as four parts, so a local test build such as 1.2.9.9 sorts below the release 1.3.0.
        public static Version Normalize(Version version)
        {
            return new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        // Files must come from this release on DLB's own GitHub; only a local test feed (a file named with --feed)
        // may point anywhere else, including local files.
        public static UpdateDecision Decide(ReleaseInfo release, Version installed, bool allowPrerelease, bool localFeed)
        {
            if (release == null || release.Draft || (release.Prerelease && !allowPrerelease)) return UpdateDecision.UpToDate();
            Version version;
            if (!TryParseTag(release.Tag, out version)) return UpdateDecision.NotAvailable();
            if (Normalize(version) <= Normalize(installed)) return UpdateDecision.UpToDate();

            ReleaseAsset exe = release.Assets.FirstOrDefault(asset => asset.Name == ExeName);
            ReleaseAsset checksum = release.Assets.FirstOrDefault(asset => asset.Name == ChecksumName);
            if (exe == null || checksum == null) return UpdateDecision.NotAvailable();
            if (exe.Size <= 0 || exe.Size > Downloader.MaximumBytes || checksum.Size <= 0 || checksum.Size > ChecksumFile.MaximumBytes)
                return UpdateDecision.NotAvailable();
            if (!AllowedDownload(exe.DownloadUrl, localFeed) || !AllowedDownload(checksum.DownloadUrl, localFeed))
                return UpdateDecision.NotAvailable();
            if (!localFeed && (!ExpectedDownload(exe, release.Tag) || !ExpectedDownload(checksum, release.Tag)))
                return UpdateDecision.NotAvailable();

            string versionText = version.ToString(3);
            string name = release.Name.Length > 0 ? release.Name : "DLB Precision Desktop Clock " + versionText;
            string notes = PlainText.FromMarkdown(release.Body, MaximumNotesLength, name);
            string title = name.Length > MaximumTitleLength ? name.Substring(0, MaximumTitleLength - 1) + "…" : name;
            return UpdateDecision.Available(new UpdateOffer(versionText, title, notes, exe, checksum));
        }

        // Compared after parsing, as the downloader will use it, so "../" segments can't leave DLB's release.
        // GitHub ignores case in owner and repository names, and its replies use whatever case the account has
        // now, so that part ignores case. The tag and the file name must match exactly.
        private static bool ExpectedDownload(ReleaseAsset asset, string tag)
        {
            Uri uri;
            if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out uri)) return false;
            string prefix = ReleaseFeed.DownloadPrefix;
            string actual = uri.AbsoluteUri;
            string release = tag + "/" + asset.Name;
            return actual.Length == prefix.Length + release.Length
                && string.Compare(actual, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0
                && string.CompareOrdinal(actual, prefix.Length, release, 0, release.Length) == 0;
        }

        private static bool AllowedDownload(string url, bool localFeed)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttps || (localFeed && uri.IsFile && !uri.IsUnc));
        }
    }
}
