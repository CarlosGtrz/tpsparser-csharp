# Resolve and Cache the Native TpsReader CLI

Use this fallback only after the main workflow cannot use a compatible global command or .NET global tool. Never write inside the skill or plugin directory.

## Map the supported platform

- 64-bit Windows: `win-x64`, executable `tps.exe`.
- 64-bit glibc Linux: `linux-x64`, executable `tps`.
- Apple silicon macOS: `osx-arm64`, executable `tps`.
- Treat Windows ARM, Intel macOS, Linux ARM, musl Linux, and unknown combinations as unsupported. Confirm glibc rather than assuming every x64 Linux system uses it.

Use the RID-specific directory under:

- Windows: `%LOCALAPPDATA%\TpsReader\agent-tools\<rid>\`
- Linux: `${XDG_CACHE_HOME:-$HOME/.cache}/tpsreader/agent-tools/<rid>/`
- macOS: `$HOME/Library/Caches/TpsReader/agent-tools/<rid>/`

The directory contains the executable and `metadata.json`.

## Validate an existing cache entry

Require `metadata.json` to be valid JSON with:

- `schemaVersion`: integer `1`
- `repository`: exact string `CarlosGtrz/TpsReader`
- `rid`: the detected RID
- `releaseTag`: the GitHub release tag
- `assetName`: exact release asset name
- `assetSize`: positive byte count for the ZIP
- `assetDigest`: `sha256:<hex>` digest for the ZIP
- `executableSha256`: lowercase SHA-256 hex for the extracted executable
- `sourceUrl`: the API-provided HTTPS download URL

Reject the cache entry if metadata is missing or inconsistent, the executable is missing, its recomputed SHA-256 differs, or its `--help` output does not list `inspect`, `schema`, `rows`, and `export`. Do not silently repair, replace, or execute a rejected entry.

Use a valid cached executable without querying for updates. Check for a newer release only when the user explicitly requests an update or the cached executable is incompatible.

## Select a verified release asset

1. Query `https://api.github.com/repos/CarlosGtrz/TpsReader/releases`, following pagination when necessary. Consider only non-draft, non-prerelease releases.
2. In newest-first order, select the first release containing the exact asset `tps-<tag>-<rid>.zip`.
3. Use only that asset's API-provided `browser_download_url`, `size`, and `digest`; never guess a URL or substitute a RID.
4. Require an HTTPS URL, a positive size, and a `sha256:<hex>` digest. If the API is unavailable or the metadata is incomplete, report that no verified binary is available and stop.
5. Before writing, show the release tag, asset name, byte size, source URL, and absolute cache destination. Ask for confirmation. If any cache files exist, explicitly ask to replace the complete entry.

## Stage and install after approval

1. Download the ZIP to a unique operating-system temporary directory.
2. Verify its byte size and SHA-256 against GitHub's asset metadata.
3. Require exactly one top-level ZIP entry named `tps.exe` on Windows or `tps` on Unix. Reject path components, extra entries, symlinks, and malformed archives.
4. Extract into a unique staging directory outside the final cache path. Set user-executable permission on Unix.
5. Compute the extracted executable's SHA-256 and run it with `--help`. Require `inspect`, `schema`, `rows`, and `export`.
6. Create `metadata.json` with the validated values and extracted executable hash.
7. Recheck the absolute destination and atomically place the completed staging directory as the RID cache entry only after every validation succeeds. Preserve an existing entry until the replacement is ready; never expose a partially written executable or metadata file.
8. Re-read the final metadata, recompute the installed executable hash, and rerun `--help` before using it.

On any failure, remove temporary and staging files, preserve any previous cache entry, and report the failed check. Never bypass macOS Gatekeeper, remove quarantine metadata, disable operating-system security, or replace cache files without permission.
