# Code signing and releases

Releases are signed by **DLB Precision, LLC** through DLB's Azure Artifact Signing account, the same
identity as DLB Precision Monitor. Installed clocks only ever install a build signed that way.

## What installed clocks accept

**Check for updates…** asks GitHub for the release marked **Latest** on `dlbprecision/desktop-clock`
(drafts and pre-releases are ignored) and offers it only if its tag `vX.Y.Z` is newer than the
running clock. **Update now** downloads `DlbPrecision.DesktopClock.exe` and its `.sha256` from
`https://github.com/dlbprecision/desktop-clock/releases/download/vX.Y.Z/` (the exact tag and file
names), then installs it only if all of these hold:

- its SHA-256 matches the `.sha256` file;
- Windows verifies the Authenticode signature, including revocation;
- there is exactly one signer, whose common name and organization are both `DLB Precision, LLC`,
  state `Arkansas`, country `US`;
- the chain builds to a trusted root that is `Microsoft Identity Verification Root Certificate
  Authority 2020` (thumbprint `F40042E2E5F7E8EF8189FED15519AECE42C3BFA2`); the short-lived leaf is
  not pinned;
- the certificate is for code signing and the signature is timestamped;
- the file's product and description are `DLB Precision Desktop Clock` and its product version
  equals the release version.

The swap: the new file is downloaded next to the clock as `<exe>.new`, the clock is asked to close,
`<exe>` becomes `<exe>.old`, `<exe>.new` becomes `<exe>`, and the new clock is started. If its
window doesn't appear within 15 seconds, the old file goes back and the old clock starts again.
Windows lets a running exe be renamed, so the updater (which runs from the clock's own file) needs
no temporary copy; the new clock deletes `<exe>.old` once the updater has exited.

## Signing setup

Signing uses SignTool, the Artifact Signing client library and the account metadata, as in the
monitor's `docs/CODE-SIGNING.md`. Point the build at them with a `signing.local.json` beside
`build.ps1` (it is git-ignored and holds paths only, never credentials):

```json
{
  "SignToolPath": "C:\\...\\x64\\signtool.exe",
  "DlibPath": "C:\\...\\x64\\Azure.CodeSigning.Dlib.dll",
  "MetadataPath": "C:\\...\\dlbprecision-metadata.json",
  "AzureCliPath": "C:\\...\\azure-cli\\bin"
}
```

The signing identity comes from the Azure CLI sign-in (`az login` in a browser, as for the monitor).

## Releasing

1. Set the version in `VERSION` and commit.
2. Build into a new folder (the build refuses to reuse one):

   ```powershell
   .\build.ps1 -Sign -OutputDirectory .\artifacts\release-1.3.1-attempt-1
   ```

   It signs, checks the signature, writes the `.sha256`, then runs the new exe's
   `--verify-package` check, which applies every rule above. The build fails if installed clocks
   would refuse the file.
3. On GitHub, publish tag `v1.3.1` as a **pre-release** with exactly those two files.
4. Test it from a clock: `DlbPrecision.DesktopClock.exe --update --feed https://api.github.com/repos/dlbprecision/desktop-clock/releases/tags/v1.3.1`
5. Edit the release: untick pre-release and tick **Set as the latest release**. That is when clocks
   are offered it. To pull back a bad release, mark it as a pre-release again and fix forward.

A local test build (`-Version 1.2.9.9 -TestBuild`) has a four-part version, so it sorts below the
next release and can never be offered as one.

## Contract with installed copies (do not change)

Installed clocks keep the rules they shipped with, so every future release must keep:

- the `dlbprecision/desktop-clock` repository: never renamed, re-cased, transferred, made private
  or deleted;
- tags `vX.Y.Z` (lowercase `v`) and the asset names `DlbPrecision.DesktopClock.exe` and
  `DlbPrecision.DesktopClock.exe.sha256`;
- the publisher identity (DLB Precision, LLC; Arkansas; US) and the Microsoft Identity Verification
  root;
- the product name and description `DLB Precision Desktop Clock`, and the clock's window title
  `DLB Precision Desktop Clock`, which the updater waits for after a swap;
- the `--update [--feed …]` and `--verify-package` arguments.

If the publisher identity or signing service ever changes, installed clocks will refuse the new
signature: publish that release with instructions for a one-time manual update.
