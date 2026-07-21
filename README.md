# TpsParser for C#

`TpsParser` is a read-only C#/.NET parser for Clarion TopSpeed (`.TPS`) files. It exposes an idiomatic object model for tables, fields, records, MEMO values, and BLOB values, including encrypted files that require an owner/password.

## Install/build

The library targets `net9.0`.

```powershell
dotnet build TpsParser.sln -c Release
dotnet test TpsParser.sln -c Release
```

## Command-line inspector

`TpsInspector` opens one TPS file or scans all TPS files in a directory without
modifying them. It reports table, field, record, and MEMO/BLOB counts.
Files are opened with read/write sharing so the inspector can read TPS files
that are currently open by a Clarion application.

```powershell
dotnet run --project src\TpsInspector -c Release -- C:\data
dotnet run --project src\TpsInspector -c Release -- C:\data\CUSTOMER.TPS --details
```

Use `--recursive` to include subdirectories, `--owner <password>` for encrypted
files, or `--ignore-errors` to attempt partial recovery from damaged pages.
`--owner` can be repeated when a directory contains files with different keys.

## Basic usage

```csharp
using TpsParser;

var parser = TpsParser.TpsParser.Open(@"C:\data\CUSTOMER.TPS");

foreach (var table in parser.Tables)
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
var parser = TpsParser.TpsParser.Open(
    @"C:\data\encrypted.tps",
    new TpsOpenOptions { Owner = "owner-password" });
```

## Damaged files / partial recovery

Set `IgnoreErrors` to discard unreadable pages and continue with later valid pages. For a truncated BLOB, it returns the available payload bytes after the BLOB length header. Records from a failed page are never returned partially.

```csharp
var parser = TpsParser.TpsParser.Open(
    @"C:\data\damaged.tps",
    new TpsOpenOptions { IgnoreErrors = true });
```

## API notes

- `TpsParser.Open` throws `TpsParseException` when the file cannot be opened or parsed.
- `TpsParser.TryOpen` returns a `TpsParseError` instead of throwing.
- Field lookups accept full field names like `CUS:CUSTNUMBER` and unambiguous short names like `CUSTNUMBER`. When a short name occurs more than once, use its table-qualified name.
- BCD/DECIMAL values are preserved losslessly as strings through `GetDecimalString`; `TryGetDecimal` is available when the value fits .NET `decimal`.
- `StringEncoding` applies to field values, MEMO text, and table/schema names.
- TIME values preserve hours, minutes, seconds, and hundredths of a second.
- `GetBlob` returns a new byte array each call.
- This version is read-only. It does not write, repair, or export TPS files to CSV.

## Attribution and license

This parser adapts logic from the original Java project `ctrl-alt-dev/tps-parse`.

- Project: `ctrl-alt-dev/tps-parse`
- URL: https://github.com/ctrl-alt-dev/tps-parse
- Author / copyright: (C) 2012-2021 E. Hooijmeijer / Erik Hooijmeijer
- Organization / site: ctrl-alt-dev, http://www.ctrl-alt-dev.nl/
- License: Apache License 2.0
- Local license copy: `Apache-2.0.txt`

The original project describes itself as reverse-engineered TPS parsing software. TPS parsing may be incomplete and may misinterpret data; verify output before relying on it.
