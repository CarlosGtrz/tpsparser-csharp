# TpsParser for C#

`TpsParser` is a read-only C#/.NET parser for Clarion TopSpeed (`.TPS`) files. It exposes an idiomatic object model for tables, fields, records, MEMO values, and BLOB values, including encrypted files that require an owner/password.

## Install/build

The library targets `net9.0`.

```powershell
dotnet build TpsParser.sln -c Release
dotnet test TpsParser.sln -c Release
```

## Command-line tool

The read-only `tps` CLI is designed for both people and coding agents. It opens
files with read/write sharing, so it can read a TPS file while a Clarion
application has it open. Build and run it directly from the repository:

```powershell
dotnet run --project src\TpsInspector -c Release -- schema C:\data\CUSTOMER.TPS
```

Or create and install the .NET tool package:

```powershell
dotnet pack src\TpsInspector -c Release -o artifacts\packages
dotnet tool install --global TpsParser.Tool --version 0.2.0 --add-source artifacts\packages
tps --help
```

A self-contained Windows executable can be built without requiring .NET on the
destination machine:

```powershell
dotnet publish src\TpsInspector -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Inspect and discover schema

`inspect` reports human-readable counts and optionally scans directories.
`schema` emits a stable JSON document with `formatVersion: 1` and all available
tables, fields, MEMO/BLOB definitions, indexes, and record counts.

```powershell
tps inspect C:\data --recursive
tps inspect C:\data\CUSTOMER.TPS --details
tps schema C:\data\CUSTOMER.TPS
tps schema C:\data\CUSTOMER.TPS --table CUS
```

The legacy path-first form remains supported:

```powershell
tps C:\data\CUSTOMER.TPS --details
tps C:\data\CUSTOMER.TPS --csv
```

### Read and filter records

`rows` writes JSON to stdout. It returns at most 100 records by default; use
`--limit`, paging with `--skip`, or the explicit `--all` option. A table is
selected automatically only when the file contains one table.

```powershell
tps rows CUSTOMER.TPS --table CUS --fields CUSTNUMBER,COMPANY --limit 20
tps rows CUSTOMER.TPS --table CUS --where STATE eq AZ --where CUSTNUMBER ge 100
tps rows CUSTOMER.TPS --table CUS --record 16 --format jsonl
```

Each JSON row contains `recordNumber` and a nested `values` object. JSON mode
also reports matched/returned counts and whether more matches are available.
JSONL emits one row per line and reports truncation on stderr.

`--where` takes a field, an operator, and (except for the null operators) a
value. Repeated predicates are combined with AND:

| Value type | Operators |
| --- | --- |
| All scalar fields | `eq`, `ne`, `is-null`, `is-not-null` |
| Number, DECIMAL, DATE, TIME | `lt`, `le`, `gt`, `ge` |
| STRING, CSTRING, PSTRING, MEMO | `contains`, `starts-with`, `ends-with` |
| GROUP | `eq`, `ne` with a `0x` hex value |
| BLOB | `is-null`, `is-not-null` |

Use `@recordNumber` as a numeric pseudo-field. Array predicates require a
one-based element such as `PHONE[2]`. Text predicates are case-insensitive and
fixed-width padding is ignored; add `--case-sensitive` for exact casing.
DATE literals use `yyyy-MM-dd`; TIME accepts `HH:mm:ss`, `HH:mm:ss.f`, or
`HH:mm:ss.ff`. DECIMAL values are compared without loss of precision.

JSON preserves DECIMAL values as strings, dates/times as ISO text, GROUP values
as `0x` hex, and BLOBs as byte length plus SHA-256 metadata. Use
`--blob-mode base64` only when the complete BLOB is needed.

### Export CSV

`export` requires an output directory and supports the same projection and
filter options as `rows`. Unlike `rows`, export has no default record limit.

```powershell
tps export CUSTOMER.TPS --output C:\export
tps export CUSTOMER.TPS --table CUS --fields CUSTNUMBER,COMPANY --where STATE eq AZ --output C:\export
```

A single selected table produces `<file>.csv`; exporting every table from a
multi-table file produces `<file>-<table>.csv`. Text MEMOs are CSV columns.
BLOBs are separate `.blob` files referenced from their CSV column. Writes are
atomic and existing export files are overwritten.

### Encrypted and damaged files

Use repeatable `--owner <password>` options for encrypted files. Prefer
`--owner-env <variable>` in scripts and agent sessions so the secret is not
placed in command history or process arguments:

```powershell
$env:TPS_OWNER = 'secret'
tps rows encrypted.tps --owner-env TPS_OWNER --limit 10
```

Use `--ignore-errors` to recover readable pages from a damaged file. This can
produce incomplete results and should be used only when partial recovery is
intended.

Exit codes are `0` for success, `1` for invalid arguments/path/query, and `2`
for parsing or export failures. `schema` and `rows` keep errors on stderr so
stdout remains valid JSON or JSONL; `export` writes exported file paths to stdout.

## Migrating to 0.2.0

The main loaded-file type was renamed from `TpsParser.TpsParser` to
`TpsParser.TpsFile`. Replace calls to the old type with `TpsFile`; its `Open`,
`TryOpen`, `Tables`, and `GetTable` behavior is unchanged.

## Basic usage

```csharp
using TpsParser;

var file = TpsFile.Open(@"C:\data\CUSTOMER.TPS");

foreach (var table in file.Tables)
{
    Console.WriteLine($"{table.Name}: {table.Records.Count} records");

    foreach (var record in table.Records)
    {
        var customerNumber = record.GetInt32("CUS:CUSTNUMBER");
        var company = record.GetString("COMPANY");
    }
}
```

## Encrypted files

```csharp
var file = TpsFile.Open(
    @"C:\data\encrypted.tps",
    new TpsOpenOptions { Owner = "owner-password" });
```

## Stream and byte-array input

TPS data already held in memory can be parsed directly. The parser treats the
array as read-only and does not retain or modify it, including for encrypted
files:

```csharp
var file = TpsFile.Open(tpsBytes);
```

Use the stream overload when TPS data comes from a network response or another
streaming source:

```csharp
using var stream = new MemoryStream(tpsBytes);
var file = TpsFile.Open(stream);
```

The stream must be readable. Parsing starts at its current position and consumes
through EOF, including for non-seekable streams. `TpsFile` never disposes or
rewinds the supplied stream; ownership remains with the caller. The same contract
applies to the `TpsFile.TryOpen(Stream, ...)` overload.

## Damaged files / partial recovery

Set `IgnoreErrors` to discard unreadable pages and continue with later valid pages. For a truncated BLOB, it returns the available payload bytes after the BLOB length header. Records from a failed page are never returned partially.

```csharp
var file = TpsFile.Open(
    @"C:\data\damaged.tps",
    new TpsOpenOptions { IgnoreErrors = true });
```

## API notes

- `TpsFile.Open` throws `TpsParseException` when the file cannot be opened or parsed.
- `TpsFile.TryOpen` returns a `TpsParseError` instead of throwing.
- Both methods accept a file path, a readable `Stream`, or a complete `byte[]`.
- Field lookups accept full field names like `CUS:CUSTNUMBER` and unambiguous short names like `CUSTNUMBER`. When a short name occurs more than once, use its table-qualified name.
- BCD/DECIMAL values are preserved losslessly as strings through `GetDecimalString`; `TryGetDecimal` is available when the value fits .NET `decimal`.
- `StringEncoding` applies to field values, MEMO text, and table/schema names.
- TIME values preserve hours, minutes, seconds, and hundredths of a second.
- `GetBlob` returns a new byte array each call.
- The TPS parser remains read-only; CSV export is provided by the CLI and
  never modifies the source TPS file.

## Attribution and license

This parser adapts logic from the original Java project `ctrl-alt-dev/tps-parse`.

- Project: `ctrl-alt-dev/tps-parse`
- URL: https://github.com/ctrl-alt-dev/tps-parse
- Author / copyright: (C) 2012-2021 E. Hooijmeijer / Erik Hooijmeijer
- Organization / site: ctrl-alt-dev, http://www.ctrl-alt-dev.nl/
- License: Apache License 2.0
- Local license copy: `Apache-2.0.txt`

The original project describes itself as reverse-engineered TPS parsing software. TPS parsing may be incomplete and may misinterpret data; verify output before relying on it.
