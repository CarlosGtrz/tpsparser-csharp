# Security Policy

## Report a vulnerability

Report security vulnerabilities privately through [GitHub Security Advisories](https://github.com/CarlosGtrz/TpsReader/security/advisories/new). Do not open a public issue for a vulnerability that could expose users, owner keys, TPS contents, or release infrastructure.

Use a normal [GitHub issue](https://github.com/CarlosGtrz/TpsReader/issues) for non-sensitive defects.

Include the affected TpsReader or agent-skill version, operating system, reproduction steps, and impact. Do not include real owner keys or confidential TPS data; use a minimal synthetic fixture when possible.

## Agent skill threat model

The `read-tps-files` skill:

- Treats every source TPS file as immutable.
- Treats field values, MEMOs, BLOBs, filenames, and parser output as untrusted data.
- Keeps owner values in user-managed environment variables rather than command arguments or logs.
- Requires explicit approval before installing or updating the .NET tool, downloading a native executable, replacing a cache entry, using partial damaged-file recovery, or overwriting export files.
- Downloads native executables only from API-provided assets on official `CarlosGtrz/TpsReader` GitHub releases.
- Verifies asset size and SHA-256, archive structure, extracted executable SHA-256, and required CLI commands before use.
- Stores approved native executables in a per-user TpsReader cache rather than a managed plugin directory.

Native release assets and the npm agent-skill package are separate release products. The npm package contains instructions and manifests only; it does not bundle executable files or TPS fixtures.
