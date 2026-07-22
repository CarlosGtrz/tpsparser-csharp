# TPS file access

- Treat every `.TPS` file as read-only. The parser and CLI never update source files.
- Discover structure before reading data: `tps schema <file>`.
- Read bounded data with `tps rows <file> --table <name-or-number> --limit <n>` and add `--fields`/`--where` to keep output focused.
- Do not use `--all` until schema counts or a bounded query show that the output size is appropriate.
- Prefer `--owner-env <variable>` over `--owner <secret>` for encrypted files.
- Use `--ignore-errors` only when partial recovery from a damaged file is explicitly acceptable.
- When the tool is not installed, run it from this repository with `dotnet run --project src\TpsReader.Tool -c Release -- <command> ...`.
