using System.Globalization;
using System.Text;
using TpsParser;

namespace TpsInspector;

internal static class CsvExporter
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static IReadOnlyList<string> Export(string sourcePath, IReadOnlyList<TpsTable> tables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(tables);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullSourcePath)
            ?? throw new ArgumentException("The TPS source path does not have a parent directory.", nameof(sourcePath));
        var sourceStem = Path.GetFileNameWithoutExtension(fullSourcePath);
        var csvStems = CreateCsvStems(sourceStem, tables);
        var exportedPaths = new List<string>(tables.Count);

        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var table = tables[tableIndex];
            var csvStem = csvStems[tableIndex];
            var csvPath = Path.Combine(directory, $"{csvStem}.csv");
            ExportTable(directory, csvStem, csvPath, table);
            exportedPaths.Add(csvPath);
        }

        return exportedPaths;
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

    private static void ExportTable(string directory, string csvStem, string csvPath, TpsTable table)
    {
        var blobTokens = CreateBlobTokens(table.Memos);
        WriteAtomic(csvPath, stream =>
        {
            using var writer = new StreamWriter(stream, Utf8WithBom, bufferSize: 1024, leaveOpen: true)
            {
                NewLine = "\r\n"
            };

            WriteRow(writer, CreateHeaders(table));
            foreach (var record in table.Records)
            {
                WriteRow(writer, CreateRow(directory, csvStem, table, record, blobTokens));
            }
        });
    }

    private static IReadOnlyList<string> CreateHeaders(TpsTable table)
    {
        var headers = new List<string> { "RecordNumber" };
        foreach (var field in table.Fields)
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
        }

        headers.AddRange(table.Memos.Select(memo => memo.Name));
        return headers;
    }

    private static IReadOnlyList<string> CreateRow(
        string directory,
        string csvStem,
        TpsTable table,
        TpsRecord record,
        IReadOnlyDictionary<int, string> blobTokens)
    {
        var values = new List<string>
        {
            record.RecordNumber.ToString(CultureInfo.InvariantCulture)
        };

        foreach (var field in table.Fields)
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

                values.AddRange(elements.Select(FormatFieldValue));
            }
            else
            {
                values.Add(FormatFieldValue(value));
            }
        }

        foreach (var memo in table.Memos)
        {
            if (memo.IsMemo)
            {
                values.Add(record.GetMemo(memo.Name) ?? string.Empty);
                continue;
            }

            var blob = record.GetBlob(memo.Name);
            if (blob is null)
            {
                values.Add(string.Empty);
                continue;
            }

            var blobFileName = $"{csvStem}-{record.RecordNumber.ToString(CultureInfo.InvariantCulture)}-{blobTokens[memo.MemoNumber]}.blob";
            WriteAtomic(Path.Combine(directory, blobFileName), stream => stream.Write(blob));
            values.Add(blobFileName);
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
                memo.ShortName,
                memo.MemoNumber.ToString(CultureInfo.InvariantCulture));
            tokens[memo.MemoNumber] = MakeUnique(token, $"-{memo.MemoNumber}", usedNames);
        }

        return tokens;
    }

    private static string FormatFieldValue(object? value) => value switch
    {
        null => string.Empty,
        string text => text.TrimEnd('\0', ' '),
        byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FF", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static void WriteRow(TextWriter writer, IEnumerable<string> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                writer.Write(',');
            }

            WriteCsvValue(writer, value);
            first = false;
        }

        writer.WriteLine();
    }

    private static void WriteCsvValue(TextWriter writer, string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        writer.Write('"');
    }

    private static string SanitizeFileNamePart(string value, string fallback)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
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
