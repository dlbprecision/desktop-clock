using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // The checks that decide whether a download is the genuine, DLB-signed clock. Real files: two DLB-signed
    // fixtures in tests\fixtures (made by Make-Fixtures.ps1), the Microsoft-signed C# compiler, and the
    // unsigned stand-in. Windows' revocation check may use the network on a cold certificate cache.
    internal static class VerifierTests
    {
        public static void Run(TestContext t)
        {
            Checksums(t);
            Policy(t);
            RealFiles(t);
            ReleaseCheck(t);
        }

        private static string Fixture(string name)
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(VerifierTests).Assembly.Location), "..", "fixtures", name));
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        private static VerificationResult Verify(string path, string sha256, string version)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return PackageVerifier.Verify(file, path, sha256, version);
        }

        private static void Checksums(TestContext t)
        {
            string hex = new string('A', 32) + new string('b', 32);
            string sha;
            t.Check(ChecksumFile.TryParse(hex + "  DlbPrecision.DesktopClock.exe\n", UpdateOffer.ExeName, out sha) && sha == hex.ToLowerInvariant(),
                "A sha256sum line for the clock is read, in any letter case");
            t.Check(ChecksumFile.TryParse(hex, UpdateOffer.ExeName, out sha), "A bare hash is read");
            t.Check(ChecksumFile.TryParse(hex + " *DlbPrecision.DesktopClock.exe", UpdateOffer.ExeName, out sha), "Binary-mode markers are allowed");
            t.Check(ChecksumFile.TryParse("\uFEFF" + hex + "\r\n", UpdateOffer.ExeName, out sha), "A byte-order mark is ignored");
            t.Check(!ChecksumFile.TryParse(hex + "  Other.exe", UpdateOffer.ExeName, out sha), "A checksum for another file is refused");
            t.Check(!ChecksumFile.TryParse(hex.Substring(1), UpdateOffer.ExeName, out sha), "A short hash is refused");
            t.Check(!ChecksumFile.TryParse(new string('g', 64), UpdateOffer.ExeName, out sha) && !ChecksumFile.TryParse("", UpdateOffer.ExeName, out sha),
                "Malformed or empty content is refused");
        }

        private static SignatureFacts Good()
        {
            return new SignatureFacts
            {
                SingleSigner = true,
                SignatureValid = true,
                SignerCommonName = "DLB Precision, LLC",
                SignerOrganization = "DLB Precision, LLC",
                SignerState = "Arkansas",
                SignerCountry = "US",
                ChainTrusted = true,
                ChainCommonNames = new List<string> { "DLB Precision, LLC", "Microsoft ID Verified CS EOC CA 01", PublisherPolicy.MicrosoftIdentityRoot },
                RootThumbprint = PublisherPolicy.MicrosoftIdentityRootThumbprint,
                CodeSigning = true,
                Timestamped = true
            };
        }

        private static void Policy(TestContext t)
        {
            t.Check(PublisherPolicy.Evaluate(Good()) == null, "DLB's identity through Microsoft's identity-verified root is accepted");
            var cases = new List<KeyValuePair<string, Action<SignatureFacts>>>
            {
                new KeyValuePair<string, Action<SignatureFacts>>("two signers", f => f.SingleSigner = false),
                new KeyValuePair<string, Action<SignatureFacts>>("a signature that doesn't verify", f => f.SignatureValid = false),
                new KeyValuePair<string, Action<SignatureFacts>>("another common name", f => f.SignerCommonName = "DLB Precision LLC"),
                new KeyValuePair<string, Action<SignatureFacts>>("another organization", f => f.SignerOrganization = "Someone Else"),
                new KeyValuePair<string, Action<SignatureFacts>>("a same-named company in another state", f => f.SignerState = "Texas"),
                new KeyValuePair<string, Action<SignatureFacts>>("another country", f => f.SignerCountry = "GB"),
                new KeyValuePair<string, Action<SignatureFacts>>("an untrusted chain", f => f.ChainTrusted = false),
                new KeyValuePair<string, Action<SignatureFacts>>("another root", f => f.ChainCommonNames[2] = "Some Other Root"),
                new KeyValuePair<string, Action<SignatureFacts>>("a look-alike root", f => f.RootThumbprint = new string('0', 40)),
                new KeyValuePair<string, Action<SignatureFacts>>("a certificate not for code signing", f => f.CodeSigning = false),
                new KeyValuePair<string, Action<SignatureFacts>>("no timestamp", f => f.Timestamped = false),
            };
            foreach (KeyValuePair<string, Action<SignatureFacts>> refusal in cases)
            {
                SignatureFacts facts = Good();
                refusal.Value(facts);
                t.Check(PublisherPolicy.Evaluate(facts) != null, "Refused: " + refusal.Key);
            }
        }

        private static void RealFiles(TestContext t)
        {
            string clock = Fixture("dlb-signed-clock.exe");
            string other = Fixture("dlb-signed-other.exe");
            if (!File.Exists(clock) || !File.Exists(other))
            {
                t.Check(false, "The signed fixtures exist (run tests\\fixtures\\Make-Fixtures.ps1)");
                return;
            }
            VerificationResult genuine = Verify(clock, Sha256(clock), "1.2.9.1");
            t.Check(genuine.Ok, "A genuine DLB-signed clock of the offered version is accepted" + (genuine.Ok ? "" : ": " + genuine.Reason));
            VerificationResult version = Verify(clock, Sha256(clock), "1.2.9.2");
            t.Check(!version.Ok && version.Reason.Contains("version 1.2.9.1, not 1.2.9.2"), "A different version than offered is refused");
            t.Check(Verify(clock, new string('0', 64), "1.2.9.1").Reason.Contains("doesn't match its checksum"), "A checksum mismatch is refused");

            string tampered = Path.Combine(t.NewFolder("tampered"), "DlbPrecision.DesktopClock.exe");
            byte[] bytes = File.ReadAllBytes(clock);
            bytes[bytes.Length / 3] ^= 0x5A;
            File.WriteAllBytes(tampered, bytes);
            VerificationResult changed = Verify(tampered, Sha256(tampered), "1.2.9.1");
            t.Check(!changed.Ok && changed.Reason.StartsWith("Windows could not verify"), "A byte-tampered copy is refused by Windows' signature check");

            VerificationResult notClock = Verify(other, Sha256(other), "1.2.9.1");
            t.Check(!notClock.Ok && notClock.Reason.Contains("is not the DLB Precision Desktop Clock"), "A DLB-signed program that isn't the clock is refused");

            string compiler = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            VerificationResult microsoft = Verify(compiler, Sha256(compiler), "1.2.9.1");
            t.Check(!microsoft.Ok && microsoft.Reason.Contains("signed by another publisher"), "A Microsoft-signed program is refused as the wrong publisher");

            VerificationResult unsigned = Verify(t.FakeClock, Sha256(t.FakeClock), "1.2.9.1");
            t.Check(!unsigned.Ok && unsigned.Reason.StartsWith("Windows could not verify"), "An unsigned file is refused");

            t.Check(PackageVerifier.IsRevocationUnavailable(unchecked((int)0x80092013)) && !PackageVerifier.IsRevocationUnavailable(unchecked((int)0x80096010)),
                "Only 'couldn't check revocation' failures are offered a retry");
        }

        // The release build runs these same rules on the signed exe and its .sha256 before publishing.
        private static void ReleaseCheck(TestContext t)
        {
            string folder = t.NewFolder("release");
            string exe = Path.Combine(folder, UpdateOffer.ExeName);
            File.Copy(Fixture("dlb-signed-clock.exe"), exe);
            File.WriteAllText(exe + ".sha256", Sha256(exe) + "  " + UpdateOffer.ExeName + "\n");
            VerificationResult accepted = PackageVerifier.VerifyPackage(exe, "1.2.9.1", true);
            t.Check(accepted.Ok, "The release check accepts a signed test build with its checksum file" + (accepted.Ok ? "" : ": " + accepted.Reason));
            t.Check(PackageVerifier.VerifyPackage(exe, "1.2.9.1", false).Reason.Contains("can never be offered"),
                "A four-part version is refused as a release");

            string renamed = Path.Combine(folder, "Other.exe");
            File.Copy(exe, renamed);
            t.Check(PackageVerifier.VerifyPackage(renamed, "1.2.9.1", true).Reason.Contains("must be named DlbPrecision.DesktopClock.exe"),
                "A release exe must have the clock's file name");
            File.Delete(exe + ".sha256");
            t.Check(PackageVerifier.VerifyPackage(exe, "1.2.9.1", true).Reason.Contains(".sha256 is missing"), "A missing checksum file is refused");
            File.WriteAllText(exe + ".sha256", Sha256(exe) + "  Other.exe\n");
            t.Check(PackageVerifier.VerifyPackage(exe, "1.2.9.1", true).Reason.Contains("couldn't be read"), "A checksum file for another file is refused");
        }
    }
}
