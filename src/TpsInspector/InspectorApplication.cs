using System.Text;
using System.Globalization;
using TpsParser;
using Parser = TpsParser.TpsParser;

namespace TpsInspector;

internal static class InspectorApplication
{
    private const int MaximumSampleLength = 50;

    public static int Run(string[] arguments, TextWriter output, TextWriter errorOutput)
    {
        var parseResult = CommandLineOptions.Parse(arguments);
        if (parseResult.ShowHelp)
        {
            PrintUsage(output);
            return 0;
        }

        if (parseResult.Error is not null)
        {
            errorOutput.WriteLine($"Error: {parseResult.Error}");
            errorOutput.WriteLine();
            PrintUsage(errorOutput);
            return 1;
        }

        var options = parseResult.Options!;
        string[] files;
        try
        {
            files = ResolveFiles(options.Path, options.Recursive);
        }
        catch (IOException ex)
        {
            errorOutput.WriteLine($"Could not inspect the path: {ex.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            errorOutput.WriteLine($"Could not inspect the path: {ex.Message}");
            return 1;
        }
        catch (ArgumentException ex)
        {
            errorOutput.WriteLine($"Could not inspect the path: {ex.Message}");
            return 1;
        }
        catch (NotSupportedException ex)
        {
            errorOutput.WriteLine($"Could not inspect the path: {ex.Message}");
            return 1;
        }

        if (files.Length == 0)
        {
            errorOutput.WriteLine($"No TPS files were found in '{options.Path}'.");
            return 1;
        }

        var succeeded = 0;
        var failed = 0;
        long totalTables = 0;
        long totalRecords = 0;

        foreach (var file in files)
        {
            if (!TryOpenWithOwners(file, options, out var parser, out var parseError))
            {
                failed++;
                output.WriteLine($"ERROR  {file}");
                output.WriteLine($"       {parseError?.Message ?? "Unspecified parsing error."}");
                continue;
            }

            succeeded++;
            var tableCount = parser!.Tables.Count;
            var recordCount = parser.Tables.Sum(table => (long)table.Records.Count);
            var fieldCount = parser.Tables.Sum(table => table.Fields.Count);
            var memoCount = parser.Tables.Sum(table => table.Memos.Count);
            totalTables += tableCount;
            totalRecords += recordCount;

            output.WriteLine(
                $"OK     {file}  tables={tableCount} fields={fieldCount} " +
                $"records={recordCount} memo/blob={memoCount}");

            if (options.Details)
            {
                PrintDetails(parser, output);
            }
        }

        output.WriteLine();
        output.WriteLine(
            $"Summary: files={files.Length}, opened={succeeded}, errors={failed}, " +
            $"tables={totalTables}, records={totalRecords}");
        return failed == 0 ? 0 : 2;
    }

    private static void PrintDetails(Parser parser, TextWriter output)
    {
        foreach (var table in parser.Tables)
        {
            output.WriteLine(
                $"       Table #{table.TableNumber} {table.Name}: " +
                $"fields={table.Fields.Count}, indexes={table.Indexes.Count}, " +
                $"records={table.Records.Count}, memo/blob={table.Memos.Count}");

            output.WriteLine(
                $"         {"No.",3}  {"Field",-32} {"Type",-18} " +
                $"{"Offset",6} {"Length",6}  Sample value");
            var firstRecord = table.Records.Count == 0 ? null : table.Records[0];
            foreach (var field in table.Fields)
            {
                var arraySuffix = field.IsArray ? $"[{field.ElementCount}]" : string.Empty;
                var type = $"{field.Type}{arraySuffix}";
                var sample = firstRecord is null
                    ? "<no records>"
                    : FormatSampleValue(firstRecord.GetValue(field.Name));
                output.WriteLine(
                    $"         {field.FieldNumber,3}  {field.Name,-32} " +
                    $"{type,-18} {field.Offset,6} {field.Length,6}  {sample}");
            }

            foreach (var memo in table.Memos)
            {
                output.WriteLine($"         MEMO  {memo.Name} ({(memo.IsBlob ? "BLOB" : "MEMO")})");
            }
        }
    }

    internal static string FormatSampleValue(object? value)
    {
        var text = EscapeLineBreaks(FormatValue(value));
        return text.Length <= MaximumSampleLength
            ? text
            : $"{text[..MaximumSampleLength]}...";
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "<null>",
        string text => text.TrimEnd('\0', ' '),
        byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
        object?[] values => $"[{string.Join(", ", values.Select(FormatValue))}]",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FF", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string EscapeLineBreaks(string value) => value
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    private static string[] ResolveFiles(string path, bool recursive)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            return [fullPath];
        }

        if (!Directory.Exists(fullPath))
        {
            throw new FileNotFoundException($"Path '{fullPath}' does not exist.", fullPath);
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(fullPath, "*", searchOption)
            .Where(file => string.Equals(Path.GetExtension(file), ".tps", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryOpenWithOwners(
        string file,
        CommandLineOptions options,
        out Parser? parser,
        out TpsParseError? error)
    {
        IEnumerable<string?> ownerCandidates = options.Owners.Count == 0
            ? [null]
            : options.Owners.Select(owner => (string?)owner);
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
            ?? throw new InvalidOperationException("Code page 1252 is unavailable.");

        parser = null;
        error = null;
        foreach (var owner in ownerCandidates)
        {
            var openOptions = new TpsOpenOptions
            {
                Owner = owner,
                IgnoreErrors = options.IgnoreErrors,
                StringEncoding = windows1252
            };

            if (Parser.TryOpen(file, out parser, out error, openOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("TpsInspector - read-only Clarion TopSpeed file inspection");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  TpsInspector <file-or-directory> [options]");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --recursive       Search subdirectories for TPS files.");
        output.WriteLine("  --ignore-errors   Skip damaged pages and recover readable data.");
        output.WriteLine("  --owner <key>     Owner key for encrypted files; may be repeated.");
        output.WriteLine("  --details         Show tables, field definitions, and first-record sample values.");
        output.WriteLine("  -h, --help        Show this help.");
        output.WriteLine();
        output.WriteLine("Exit codes: 0=success, 1=invalid usage/path, 2=one or more TPS files failed.");
    }

    private sealed record CommandLineOptions(
        string Path,
        bool Recursive,
        bool IgnoreErrors,
        bool Details,
        IReadOnlyList<string> Owners)
    {
        public static CommandLineParseResult Parse(string[] arguments)
        {
            if (arguments.Length == 0)
            {
                return new CommandLineParseResult(null, null, ShowHelp: true);
            }

            string? path = null;
            var owners = new List<string>();
            var recursive = false;
            var ignoreErrors = false;
            var details = false;

            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                switch (argument.ToUpperInvariant())
                {
                    case "-H":
                    case "--HELP":
                        return new CommandLineParseResult(null, null, ShowHelp: true);
                    case "--RECURSIVE":
                        recursive = true;
                        break;
                    case "--IGNORE-ERRORS":
                        ignoreErrors = true;
                        break;
                    case "--DETAILS":
                        details = true;
                        break;
                    case "--OWNER":
                        if (++i >= arguments.Length)
                        {
                            return new CommandLineParseResult(null, "Missing owner key after --owner.", false);
                        }

                        owners.Add(arguments[i]);
                        break;
                    default:
                        if (argument.StartsWith('-'))
                        {
                            return new CommandLineParseResult(null, $"Unknown option '{argument}'.", false);
                        }

                        if (path is not null)
                        {
                            return new CommandLineParseResult(null, "Only one file or directory may be specified.", false);
                        }

                        path = argument;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return new CommandLineParseResult(null, "A TPS file or directory is required.", false);
            }

            return new CommandLineParseResult(
                new CommandLineOptions(path, recursive, ignoreErrors, details, owners),
                null,
                ShowHelp: false);
        }
    }

    private sealed record CommandLineParseResult(
        CommandLineOptions? Options,
        string? Error,
        bool ShowHelp);
}
