using System.Globalization;
using System.Text;
using TpsReader;

namespace TpsReader.Tool;

internal static class CsvExporter
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly HashSet<char> InvalidFileNameCharacters =
        [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static IReadOnlyList<string> Export(string sourcePath, IReadOnlyList<TpsTable> tables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(tables);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullSourcePath)
            ?? throw new ArgumentException("The TPS source path does not have a parent directory.", nameof(sourcePath));
        return Export(fullSourcePath, directory, tables);
    }

    public static IReadOnlyList<string> Export(
        string sourcePath,
        string outputDirectory,
        IReadOnlyList<TpsTable> tables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(tables);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        var sourceStem = Path.GetFileNameWithoutExtension(fullSourcePath);
        var csvStems = CreateCsvStems(sourceStem, tables);
        var exportedPaths = new List<string>(tables.Count);

        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var table = tables[tableIndex];
            var csvStem = csvStems[tableIndex];
            var csvPath = Path.Combine(fullOutputDirectory, $"{csvStem}.csv");
            var columns = table.Fields.Select(SelectedColumn.ForField)
                .Concat(table.Memos.Select(SelectedColumn.ForMemo))
                .ToArray();
            ExportTable(fullOutputDirectory, csvStem, csvPath, table, table.Records, columns);
            exportedPaths.Add(csvPath);
        }

        return exportedPaths;
    }

    public static IReadOnlyList<string> Export(
        string sourcePath,
        string outputDirectory,
        QueryResult query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(query);

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        var csvStem = Path.GetFileNameWithoutExtension(Path.GetFullPath(sourcePath));
        var csvPath = Path.Combine(fullOutputDirectory, $"{csvStem}.csv");
        ExportTable(
            fullOutputDirectory,
            csvStem,
            csvPath,
            query.Table,
            query.Records,
            query.Columns);
        return [csvPath];
    }

    private static string[] CreateCsvStems(string sourceStem, IReadOnlyList<TpsTable> tables)
    {
        if (tables.Count == 1)
        {
            return [sourceStem];
        }

        var stems = new string[tables.Count];
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            var tableToken = SanitizeFileNamePart(
                table.Name,
                table.TableNumber.ToString(CultureInfo.InvariantCulture));
            var candidate = $"{sourceStem}-{tableToken}";
            stems[i] = MakeUnique(candidate, $"-{table.TableNumber}", usedNames);
        }

        return stems;
    }

    private static void ExportTable(
        string directory,
        string csvStem,
        string csvPath,
        TpsTable table,
        IReadOnlyList<TpsRecord> records,
        IReadOnlyList<SelectedColumn> columns)
    {
        var blobTokens = CreateBlobTokens(table.Memos);
        WriteAtomic(csvPath, stream =>
        {
            using var writer = new StreamWriter(stream, Utf8WithBom, bufferSize: 1024, leaveOpen: true)
            {
                NewLine = "\r\n"
            };

            WriteRow(writer, CreateHeaders(columns).Select(value => new CsvCell(value)));
            foreach (var record in records)
            {
                WriteRow(writer, CreateRow(directory, csvStem, record, columns, blobTokens));
            }
        });
    }

    private static IReadOnlyList<string> CreateHeaders(IReadOnlyList<SelectedColumn> columns)
    {
        var headers = new List<string> { "RecordNumber" };
        foreach (var column in columns)
        {
            if (column.Field is { } field)
            {
                if (field.IsArray)
                {
                    for (var elementIndex = 0; elementIndex < field.ElementCount; elementIndex++)
                    {
                        headers.Add($"{field.Name}[{elementIndex + 1}]");
                    }
                }
                else
                {
                    headers.Add(field.Name);
                }

                continue;
            }

            headers.Add(column.Memo!.ShortName.ToLowerInvariant());
        }

        return headers;
    }

    private static IReadOnlyList<CsvCell> CreateRow(
        string directory,
        string csvStem,
        TpsRecord record,
        IReadOnlyList<SelectedColumn> columns,
        IReadOnlyDictionary<int, string> blobTokens)
    {
        var values = new List<CsvCell>
        {
            new(record.RecordNumber.ToString(CultureInfo.InvariantCulture))
        };

        foreach (var column in columns)
        {
            if (column.Field is { } field)
            {
                var value = record.GetValue(field.Name);
                if (field.IsArray)
                {
                    var elements = value as object?[]
                        ?? throw new InvalidDataException($"Array field '{field.Name}' did not contain an array value.");
                    if (elements.Length != field.ElementCount)
                    {
                        throw new InvalidDataException(
                            $"Array field '{field.Name}' declares {field.ElementCount} elements but contains {elements.Length}.");
                    }

                    values.AddRange(elements.Select(element => FormatFieldValue(field, element)));
                }
                else
                {
                    values.Add(FormatFieldValue(field, value));
                }

                continue;
            }

            var memo = column.Memo!;
            if (memo.IsMemo)
            {
                values.Add(new CsvCell(record.GetMemo(memo.Name) ?? string.Empty));
                continue;
            }

            var blob = record.GetBlob(memo.Name);
            if (blob is null)
            {
                values.Add(new CsvCell(string.Empty));
                continue;
            }

            var blobFileName = $"{csvStem}-{record.RecordNumber.ToString(CultureInfo.InvariantCulture)}-{blobTokens[memo.MemoNumber]}.blob";
            WriteAtomic(Path.Combine(directory, blobFileName), stream => stream.Write(blob));
            values.Add(new CsvCell(blobFileName));
        }

        return values;
    }

    private static IReadOnlyDictionary<int, string> CreateBlobTokens(IReadOnlyList<TpsMemo> memos)
    {
        var tokens = new Dictionary<int, string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var memo in memos.Where(memo => memo.IsBlob))
        {
            var token = SanitizeFileNamePart(
                memo.ShortName.ToLowerInvariant(),
                memo.MemoNumber.ToString(CultureInfo.InvariantCulture));
            tokens[memo.MemoNumber] = MakeUnique(token, $"-{memo.MemoNumber}", usedNames);
        }

        return tokens;
    }

    private static CsvCell FormatFieldValue(TpsField field, object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            string stringValue when field.Type == TpsFieldType.Group => stringValue,
            string stringValue => stringValue.TrimEnd('\0', ' '),
            byte[] bytes when bytes.All(item => item == 0) => string.Empty,
            byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss.FF", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        return new CsvCell(text, ForceQuotes: field.Type == TpsFieldType.Group);
    }

    private static void WriteRow(TextWriter writer, IEnumerable<CsvCell> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                writer.Write(',');
            }

            WriteCsvValue(writer, value.Value, value.ForceQuotes);
            first = false;
        }

        writer.WriteLine();
    }

    private static void WriteCsvValue(TextWriter writer, string value, bool forceQuotes)
    {
        if (!forceQuotes && value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        writer.Write('"');
    }

    private readonly record struct CsvCell(string Value, bool ForceQuotes = false);

    private static string SanitizeFileNamePart(string value, string fallback)
    {
        var sanitized = new string(value
            .Select(character => InvalidFileNameCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string MakeUnique(string candidate, string collisionSuffix, ISet<string> usedNames)
    {
        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        var suffixedCandidate = candidate + collisionSuffix;
        var uniqueCandidate = suffixedCandidate;
        var ordinal = 2;
        while (!usedNames.Add(uniqueCandidate))
        {
            uniqueCandidate = $"{suffixedCandidate}-{ordinal++}";
        }

        return uniqueCandidate;
    }

    private static void WriteAtomic(string destinationPath, Action<Stream> write)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("The export path does not have a parent directory.", nameof(destinationPath));
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                write(stream);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
