using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TpsReader;

namespace TpsReader.Tool;

internal static class StructuredCommands
{
    public static int RunSchema(string[] arguments, TextWriter output, TextWriter errorOutput) =>
        Run(StructuredCommand.Schema, arguments, output, errorOutput);

    public static int RunRows(string[] arguments, TextWriter output, TextWriter errorOutput) =>
        Run(StructuredCommand.Rows, arguments, output, errorOutput);

    public static int RunExport(string[] arguments, TextWriter output, TextWriter errorOutput) =>
        Run(StructuredCommand.Export, arguments, output, errorOutput);

    private static int Run(
        StructuredCommand command,
        string[] arguments,
        TextWriter output,
        TextWriter errorOutput)
    {
        StructuredOptions options;
        try
        {
            options = StructuredOptions.Parse(command, arguments);
            if (options.ShowHelp)
            {
                PrintUsage(command, output);
                return 0;
            }

            ValidateInputFile(options.Path!);
        }
        catch (CliValidationException ex)
        {
            errorOutput.WriteLine($"Error: {ex.Message}");
            errorOutput.WriteLine();
            PrintUsage(command, errorOutput);
            return 1;
        }

        var fullPath = Path.GetFullPath(options.Path!);
        if (!TryOpen(fullPath, options, out var file, out var parseError))
        {
            errorOutput.WriteLine($"Error: {parseError?.Message ?? "Unspecified TPS parsing error."}");
            return 2;
        }

        try
        {
            return command switch
            {
                StructuredCommand.Schema => WriteSchema(fullPath, file!, options, output),
                StructuredCommand.Rows => WriteRows(fullPath, file!, options, output, errorOutput),
                StructuredCommand.Export => Export(fullPath, file!, options, output),
                _ => throw new InvalidOperationException($"Unsupported command {command}.")
            };
        }
        catch (CliValidationException ex)
        {
            errorOutput.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            errorOutput.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errorOutput.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int WriteSchema(
        string fullPath,
        TpsFile file,
        StructuredOptions options,
        TextWriter output)
    {
        IReadOnlyList<TpsTable> tables = options.Table is null
            ? file.Tables
            : [RecordQuery.ResolveTable(file.Tables, options.Table)];
        StructuredJson.WriteSchema(fullPath, tables, output);
        return 0;
    }

    private static int WriteRows(
        string fullPath,
        TpsFile file,
        StructuredOptions options,
        TextWriter output,
        TextWriter errorOutput)
    {
        var limit = options.All ? null : options.Limit;
        var query = RecordQuery.Execute(file.Tables, options.ToQueryRequest(limit));
        if (string.Equals(options.Format, "jsonl", StringComparison.OrdinalIgnoreCase))
        {
            StructuredJson.WriteRowsJsonLines(query, options.BlobMode, output);
            if (query.HasMore)
            {
                errorOutput.WriteLine(
                    $"Matched {query.MatchedCount} records; returned {query.Records.Count} after skipping {query.Skip}. " +
                    "More records are available; use --all or adjust --skip/--limit.");
            }
        }
        else
        {
            StructuredJson.WriteRows(fullPath, query, options.BlobMode, output);
        }

        return 0;
    }

    private static int Export(
        string fullPath,
        TpsFile file,
        StructuredOptions options,
        TextWriter output)
    {
        var outputDirectory = Path.GetFullPath(options.Output!);
        Directory.CreateDirectory(outputDirectory);

        IReadOnlyList<string> exported;
        if (!options.HasExportQuery)
        {
            exported = CsvExporter.Export(fullPath, outputDirectory, file.Tables);
        }
        else
        {
            var limit = options.LimitSpecified ? options.Limit : null;
            var query = RecordQuery.Execute(file.Tables, options.ToQueryRequest(limit));
            exported = CsvExporter.Export(fullPath, outputDirectory, query);
        }

        foreach (var path in exported)
        {
            output.WriteLine(path);
        }

        return 0;
    }

    private static bool TryOpen(
        string path,
        StructuredOptions options,
        out TpsFile? file,
        out TpsParseError? error)
    {
        IEnumerable<string?> owners = options.Owners.Count == 0
            ? [null]
            : options.Owners.Select(owner => (string?)owner);
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
            ?? throw new InvalidOperationException("Code page 1252 is unavailable.");

        file = null;
        error = null;
        foreach (var owner in owners)
        {
            var openOptions = new TpsOpenOptions
            {
                Owner = owner,
                IgnoreErrors = options.IgnoreErrors,
                StringEncoding = windows1252
            };
            if (TpsFile.TryOpen(path, out file, out error, openOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateInputFile(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new CliValidationException($"Invalid input path: {ex.Message}");
        }

        if (Directory.Exists(fullPath))
        {
            throw new CliValidationException("This command accepts one TPS file, not a directory.");
        }

        if (!File.Exists(fullPath))
        {
            throw new CliValidationException($"File '{fullPath}' does not exist.");
        }
    }

    private static void PrintUsage(StructuredCommand command, TextWriter output)
    {
        switch (command)
        {
            case StructuredCommand.Schema:
                output.WriteLine("Usage: tps schema <file> [--table <name-or-number>] [open-options]");
                output.WriteLine("Writes TPS table, field, MEMO/BLOB, index, and record-count metadata as JSON.");
                break;
            case StructuredCommand.Rows:
                output.WriteLine("Usage: tps rows <file> [query-options] [open-options]");
                output.WriteLine("Query options:");
                output.WriteLine("  --table <value>              Table name or number; optional for a single-table file.");
                output.WriteLine("  --fields <a,b,...>           Fields/MEMOs/BLOBs to return; may be repeated.");
                output.WriteLine("  --where <field> <op> [value] Typed predicate; may be repeated and predicates are ANDed.");
                output.WriteLine("  --record <number>            Match one TPS record number.");
                output.WriteLine("  --skip <count>               Skip matching records (default 0).");
                output.WriteLine("  --limit <count>              Maximum returned records (default 100).");
                output.WriteLine("  --all                        Return all matching records; incompatible with --limit.");
                output.WriteLine("  --case-sensitive             Use case-sensitive text predicates.");
                output.WriteLine("  --format <json|jsonl>        Output format (default json).");
                output.WriteLine("  --blob-mode <metadata|base64> BLOB representation (default metadata).");
                output.WriteLine("Operators: eq ne lt le gt ge contains starts-with ends-with is-null is-not-null");
                break;
            case StructuredCommand.Export:
                output.WriteLine("Usage: tps export <file> --output <directory> [query-options] [open-options]");
                output.WriteLine("Exports CSV and external BLOB files. Query options are the same as rows; export has no default limit.");
                output.WriteLine("  --format csv                 Export format (CSV is the only supported format).");
                break;
        }

        output.WriteLine("Open options:");
        output.WriteLine("  --owner <key>                Owner key; may be repeated.");
        output.WriteLine("  --owner-env <variable>       Read an owner key from an environment variable; may be repeated.");
        output.WriteLine("  --ignore-errors              Skip damaged pages and recover readable data.");
        output.WriteLine("  -h, --help                   Show command help.");
        output.WriteLine("Exit codes: 0=success, 1=invalid input/query, 2=parse/export failure.");
    }

    private enum StructuredCommand
    {
        Schema,
        Rows,
        Export
    }

    private sealed class StructuredOptions
    {
        public required string? Path { get; init; }
        public required string? Table { get; init; }
        public required string? Output { get; init; }
        public required string Format { get; init; }
        public required string BlobMode { get; init; }
        public required IReadOnlyList<string> Owners { get; init; }
        public required IReadOnlyList<string> Fields { get; init; }
        public required IReadOnlyList<PredicateInput> Predicates { get; init; }
        public int? RecordNumber { get; init; }
        public int Skip { get; init; }
        public int? Limit { get; init; }
        public bool LimitSpecified { get; init; }
        public bool All { get; init; }
        public bool CaseSensitive { get; init; }
        public bool IgnoreErrors { get; init; }
        public bool ShowHelp { get; init; }

        public bool HasExportQuery =>
            Table is not null || Fields.Count > 0 || Predicates.Count > 0 || RecordNumber is not null ||
            Skip != 0 || LimitSpecified || CaseSensitive;

        public QueryRequest ToQueryRequest(int? limit) => new(
            Table,
            Fields,
            RecordNumber,
            Predicates,
            Skip,
            limit,
            CaseSensitive);

        public static StructuredOptions Parse(StructuredCommand command, string[] arguments)
        {
            string? path = null;
            string? table = null;
            string? output = null;
            var format = command == StructuredCommand.Export ? "csv" : "json";
            var blobMode = "metadata";
            var owners = new List<string>();
            var fields = new List<string>();
            var predicates = new List<PredicateInput>();
            int? recordNumber = null;
            var skip = 0;
            int? limit = 100;
            var limitSpecified = false;
            var all = false;
            var caseSensitive = false;
            var ignoreErrors = false;
            var showHelp = false;

            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                switch (argument.ToUpperInvariant())
                {
                    case "-H":
                    case "--HELP":
                        showHelp = true;
                        break;
                    case "--OWNER":
                        owners.Add(ReadValue(arguments, ref i, "--owner"));
                        break;
                    case "--OWNER-ENV":
                        {
                            var variable = ReadValue(arguments, ref i, "--owner-env");
                            var value = Environment.GetEnvironmentVariable(variable);
                            if (string.IsNullOrEmpty(value))
                            {
                                throw new CliValidationException(
                                    $"Environment variable '{variable}' is missing or empty.");
                            }

                            owners.Add(value);
                            break;
                        }
                    case "--IGNORE-ERRORS":
                        ignoreErrors = true;
                        break;
                    case "--TABLE":
                        table = ReadOnce(table, ReadValue(arguments, ref i, "--table"), "--table");
                        break;
                    case "--FIELDS":
                        RequireQueryCommand(command, "--fields");
                        foreach (var field in ReadValue(arguments, ref i, "--fields").Split(','))
                        {
                            if (string.IsNullOrWhiteSpace(field))
                            {
                                throw new CliValidationException("--fields contains an empty field name.");
                            }

                            fields.Add(field.Trim());
                        }

                        break;
                    case "--WHERE":
                        {
                            RequireQueryCommand(command, "--where");
                            var field = ReadValue(arguments, ref i, "--where field");
                            var op = ReadValue(arguments, ref i, "--where operator");
                            var unary = op.Equals("is-null", StringComparison.OrdinalIgnoreCase) ||
                                op.Equals("is-not-null", StringComparison.OrdinalIgnoreCase);
                            var literal = unary ? null : ReadValue(arguments, ref i, "--where value");
                            predicates.Add(new PredicateInput(field, op, literal));
                            break;
                        }
                    case "--RECORD":
                        RequireQueryCommand(command, "--record");
                        recordNumber = ParseInt32(ReadValue(arguments, ref i, "--record"), "--record");
                        break;
                    case "--SKIP":
                        RequireQueryCommand(command, "--skip");
                        skip = ParseNonNegative(ReadValue(arguments, ref i, "--skip"), "--skip");
                        break;
                    case "--LIMIT":
                        RequireQueryCommand(command, "--limit");
                        limit = ParseNonNegative(ReadValue(arguments, ref i, "--limit"), "--limit");
                        limitSpecified = true;
                        break;
                    case "--ALL":
                        RequireQueryCommand(command, "--all");
                        all = true;
                        break;
                    case "--CASE-SENSITIVE":
                        RequireQueryCommand(command, "--case-sensitive");
                        caseSensitive = true;
                        break;
                    case "--FORMAT":
                        format = ReadValue(arguments, ref i, "--format");
                        break;
                    case "--BLOB-MODE":
                        if (command != StructuredCommand.Rows)
                        {
                            throw new CliValidationException("--blob-mode is only valid for rows.");
                        }

                        blobMode = ReadValue(arguments, ref i, "--blob-mode");
                        break;
                    case "--OUTPUT":
                        if (command != StructuredCommand.Export)
                        {
                            throw new CliValidationException("--output is only valid for export.");
                        }

                        output = ReadOnce(output, ReadValue(arguments, ref i, "--output"), "--output");
                        break;
                    default:
                        if (argument.StartsWith('-'))
                        {
                            throw new CliValidationException($"Unknown option '{argument}'.");
                        }

                        path = ReadOnce(path, argument, "input path");
                        break;
                }
            }

            if (showHelp)
            {
                return EmptyHelp();
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new CliValidationException("A TPS file is required.");
            }

            if (limitSpecified && all)
            {
                throw new CliValidationException("--limit and --all cannot be used together.");
            }

            if (command == StructuredCommand.Schema && !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliValidationException("schema supports only --format json.");
            }

            if (command == StructuredCommand.Rows &&
                !format.Equals("json", StringComparison.OrdinalIgnoreCase) &&
                !format.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliValidationException("rows supports --format json or jsonl.");
            }

            if (command == StructuredCommand.Export)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new CliValidationException("--output <directory> is required for export.");
                }

                if (!format.Equals("csv", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CliValidationException("export supports only --format csv.");
                }
            }

            if (!blobMode.Equals("metadata", StringComparison.OrdinalIgnoreCase) &&
                !blobMode.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliValidationException("--blob-mode must be metadata or base64.");
            }

            return new StructuredOptions
            {
                Path = path,
                Table = table,
                Output = output,
                Format = format.ToLowerInvariant(),
                BlobMode = blobMode.ToLowerInvariant(),
                Owners = owners,
                Fields = fields,
                Predicates = predicates,
                RecordNumber = recordNumber,
                Skip = skip,
                Limit = limit,
                LimitSpecified = limitSpecified,
                All = all,
                CaseSensitive = caseSensitive,
                IgnoreErrors = ignoreErrors,
                ShowHelp = false
            };
        }

        private static StructuredOptions EmptyHelp() => new()
        {
            Path = null,
            Table = null,
            Output = null,
            Format = "json",
            BlobMode = "metadata",
            Owners = [],
            Fields = [],
            Predicates = [],
            Limit = 100,
            ShowHelp = true
        };

        private static void RequireQueryCommand(StructuredCommand command, string option)
        {
            if (command == StructuredCommand.Schema)
            {
                throw new CliValidationException($"{option} is not valid for schema.");
            }
        }

        private static string ReadValue(string[] arguments, ref int index, string option)
        {
            if (++index >= arguments.Length)
            {
                throw new CliValidationException($"Missing value after {option}.");
            }

            return arguments[index];
        }

        private static string ReadOnce(string? existing, string value, string name)
        {
            if (existing is not null)
            {
                throw new CliValidationException($"{name} may only be specified once.");
            }

            return value;
        }

        private static int ParseInt32(string value, string option)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new CliValidationException($"{option} requires a valid 32-bit integer.");
            }

            return parsed;
        }

        private static int ParseNonNegative(string value, string option)
        {
            var parsed = ParseInt32(value, option);
            if (parsed < 0)
            {
                throw new CliValidationException($"{option} cannot be negative.");
            }

            return parsed;
        }
    }
}

internal static class StructuredJson
{
    private static readonly StructuredJsonContext IndentedContext = new(
        new JsonSerializerOptions { WriteIndented = true });
    private static readonly StructuredJsonContext CompactContext = new(new JsonSerializerOptions());

    public static void WriteSchema(string source, IReadOnlyList<TpsTable> tables, TextWriter output)
    {
        var document = new Dictionary<string, object?>
        {
            ["formatVersion"] = 1,
            ["source"] = source,
            ["tables"] = tables.Select(CreateTableSchema).ToArray()
        };
        output.WriteLine(JsonSerializer.Serialize(document, IndentedContext.StringObjectDictionary));
    }

    public static void WriteRows(string source, QueryResult query, string blobMode, TextWriter output)
    {
        var document = new Dictionary<string, object?>
        {
            ["formatVersion"] = 1,
            ["source"] = source,
            ["table"] = new Dictionary<string, object?>
            {
                ["tableNumber"] = query.Table.TableNumber,
                ["name"] = query.Table.Name
            },
            ["query"] = new Dictionary<string, object?>
            {
                ["matched"] = query.MatchedCount,
                ["returned"] = query.Records.Count,
                ["skip"] = query.Skip,
                ["limit"] = query.Limit,
                ["hasMore"] = query.HasMore
            },
            ["rows"] = query.Records.Select(record => CreateRow(query, record, blobMode, includeVersion: false)).ToArray()
        };
        output.WriteLine(JsonSerializer.Serialize(document, IndentedContext.StringObjectDictionary));
    }

    public static void WriteRowsJsonLines(QueryResult query, string blobMode, TextWriter output)
    {
        foreach (var record in query.Records)
        {
            output.WriteLine(JsonSerializer.Serialize(
                CreateRow(query, record, blobMode, includeVersion: true),
                CompactContext.StringObjectDictionary));
        }
    }

    private static Dictionary<string, object?> CreateTableSchema(TpsTable table) => new()
    {
        ["tableNumber"] = table.TableNumber,
        ["name"] = table.Name,
        ["recordCount"] = table.Records.Count,
        ["fields"] = table.Fields.Select(field => new Dictionary<string, object?>
        {
            ["fieldNumber"] = field.FieldNumber,
            ["name"] = field.Name,
            ["shortName"] = field.ShortName,
            ["tablePrefix"] = field.TablePrefix,
            ["type"] = field.Type.ToString(),
            ["offset"] = field.Offset,
            ["length"] = field.Length,
            ["elementCount"] = field.ElementCount,
            ["decimalDigits"] = field.DecimalDigits,
            ["decimalStorageLength"] = field.DecimalStorageLength
        }).ToArray(),
        ["memos"] = table.Memos.Select(memo => new Dictionary<string, object?>
        {
            ["memoNumber"] = memo.MemoNumber,
            ["name"] = memo.Name,
            ["shortName"] = memo.ShortName,
            ["type"] = memo.Type.ToString(),
            ["flags"] = memo.Flags
        }).ToArray(),
        ["indexes"] = table.Indexes.Select(index => new Dictionary<string, object?>
        {
            ["indexNumber"] = index.IndexNumber,
            ["name"] = index.Name,
            ["fieldsInKey"] = index.FieldsInKey
        }).ToArray()
    };

    private static Dictionary<string, object?> CreateRow(
        QueryResult query,
        TpsRecord record,
        string blobMode,
        bool includeVersion)
    {
        var row = new Dictionary<string, object?>();
        if (includeVersion)
        {
            row["formatVersion"] = 1;
        }

        row["recordNumber"] = record.RecordNumber;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var column in query.Columns)
        {
            values[column.Name] = ConvertValue(column, column.GetValue(record), blobMode);
        }

        row["values"] = values;
        return row;
    }

    private static object? ConvertValue(SelectedColumn column, object? value, string blobMode)
    {
        if (value is null)
        {
            return null;
        }

        if (column.Memo is not null)
        {
            return column.Memo.IsMemo ? value : ConvertBlob((byte[])value, blobMode);
        }

        var field = column.Field!;
        if (value is object?[] elements)
        {
            return elements.Select(element => ConvertFieldValue(field, element)).ToArray();
        }

        return ConvertFieldValue(field, value);
    }

    private static object? ConvertFieldValue(TpsField field, object? value)
    {
        if (value is null)
        {
            return null;
        }

        return field.Type switch
        {
            TpsFieldType.String => ((string)value).TrimEnd('\0', ' '),
            TpsFieldType.Decimal => (string)value,
            TpsFieldType.Date => ((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TpsFieldType.Time => ((TimeOnly)value).ToString("HH:mm:ss.ff", CultureInfo.InvariantCulture),
            TpsFieldType.Group => (string)value,
            TpsFieldType.SReal when !float.IsFinite((float)value) => FormatNonFinite((float)value),
            TpsFieldType.Real when !double.IsFinite((double)value) => FormatNonFinite((double)value),
            _ => value
        };
    }

    private static Dictionary<string, object?> ConvertBlob(byte[] bytes, string blobMode)
    {
        var blob = new Dictionary<string, object?>
        {
            ["length"] = bytes.Length,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
        if (string.Equals(blobMode, "base64", StringComparison.OrdinalIgnoreCase))
        {
            blob["base64"] = Convert.ToBase64String(bytes);
        }

        return blob;
    }

    private static string FormatNonFinite(double value) => double.IsNaN(value)
        ? "NaN"
        : double.IsPositiveInfinity(value) ? "Infinity" : "-Infinity";
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Dictionary<string, object?>), TypeInfoPropertyName = "StringObjectDictionary")]
[JsonSerializable(typeof(Dictionary<string, object?>[]))]
[JsonSerializable(typeof(object?[]))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(string))]
internal partial class StructuredJsonContext : JsonSerializerContext
{
}
