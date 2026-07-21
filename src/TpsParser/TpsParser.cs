using System.Globalization;
using TpsParser.Internal;

namespace TpsParser;

public sealed class TpsParser
{
    private readonly Dictionary<int, TpsTable> _tablesByNumber;

    private TpsParser(IReadOnlyList<TpsTable> tables)
    {
        Tables = tables;
        _tablesByNumber = tables.ToDictionary(table => table.TableNumber);
    }

    public IReadOnlyList<TpsTable> Tables { get; }

    public static TpsParser Open(string path, TpsOpenOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new TpsOpenOptions();
        ArgumentNullException.ThrowIfNull(options.StringEncoding);

        try
        {
            var file = OpenInternalFile(path, options);
            var contents = file.Parse(options.IgnoreErrors);
            if (contents.TableDefinitions.Count == 0)
            {
                throw new TpsParseException(new TpsParseError("No table definitions were found in the TPS file.", path));
            }

            var tables = contents.TableDefinitions
                .Select(table => BuildTable(table.Key, table.Value, contents, options))
                .ToArray();
            return new TpsParser(tables);
        }
        catch (TpsParseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TpsParseException(new TpsParseError($"Could not open or parse TPS file '{path}': {ex.Message}", path, ex));
        }
    }

    public static bool TryOpen(
        string path,
        out TpsParser? parser,
        out TpsParseError? error,
        TpsOpenOptions? options = null)
    {
        try
        {
            parser = Open(path, options);
            error = null;
            return true;
        }
        catch (TpsParseException ex)
        {
            parser = null;
            error = ex.Error;
            return false;
        }
    }

    public TpsTable GetTable(int tableNumber)
    {
        if (_tablesByNumber.TryGetValue(tableNumber, out var table))
        {
            return table;
        }

        throw new TpsParseException(new TpsParseError($"Table {tableNumber} was not found."));
    }

    private static TpsFile OpenInternalFile(string path, TpsOpenOptions options)
    {
        if (string.IsNullOrEmpty(options.Owner))
        {
            return new TpsFile(path, options.StringEncoding);
        }

        try
        {
            var unencryptedFile = new TpsFile(path, options.StringEncoding);
            _ = unencryptedFile.GetHeader();
            return unencryptedFile;
        }
        catch (InvalidDataException)
        {
            return new TpsFile(path, options.Owner, options.StringEncoding, options.IgnoreErrors);
        }
    }

    private static TpsTable BuildTable(
        int tableNumber,
        TableDefinitionRecord definition,
        ParsedTpsFile contents,
        TpsOpenOptions options)
    {
        var fields = definition.Fields
            .Select((field, index) => new TpsField(
                index + 1,
                field.Name,
                field.ShortName,
                field.TablePrefix,
                MapFieldType(field.FieldType),
                field.Offset,
                field.Length,
                field.ElementCount,
                field.DecimalDigits,
                field.DecimalStorageLength))
            .ToArray();

        var memos = definition.Memos
            .Select((memo, index) => new TpsMemo(index + 1, memo.Name, memo.ShortName, memo.Flags, memo.IsBlob))
            .ToArray();

        var indexes = definition.Indexes
            .Select((index, ordinal) => new TpsIndex(ordinal + 1, index.Name, index.FieldCount))
            .ToArray();

        var ambiguousFieldAliases = FindAmbiguousAliases(fields, field => field.Name, field => field.ShortName);
        var ambiguousMemoAliases = FindAmbiguousAliases(memos, memo => memo.Name, memo => memo.ShortName);
        var sourceRecords = contents.DataRecords.GetValueOrDefault(tableNumber) ?? [];
        var records = sourceRecords
            .Select(record => BuildRecord(
                tableNumber,
                record,
                fields,
                memos,
                contents.MemoRecords,
                ambiguousFieldAliases,
                ambiguousMemoAliases,
                options))
            .ToArray();

        var name = ResolveTableName(tableNumber, definition, contents.TableNames);
        return new TpsTable(tableNumber, name, fields, memos, indexes, records);
    }

    private static TpsRecord BuildRecord(
        int tableNumber,
        DataRecord record,
        TpsField[] fields,
        TpsMemo[] memos,
        IReadOnlyDictionary<(int TableNumber, int MemoIndex), IReadOnlyDictionary<int, MemoRecord>> memoRecords,
        IReadOnlySet<string> ambiguousFieldAliases,
        IReadOnlySet<string> ambiguousMemoAliases,
        TpsOpenOptions options)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fields.Length; i++)
        {
            AddAliases(values, fields[i].Name, fields[i].ShortName, record.Values[i], ambiguousFieldAliases);
        }

        var memoValues = new Dictionary<string, TpsMemoValue>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < memos.Length; i++)
        {
            var memoDefinition = memos[i];
            var records = memoRecords.GetValueOrDefault((tableNumber, i));
            var source = records?.GetValueOrDefault(record.RecordNumber);
            var value = BuildMemoValue(memoDefinition, source, record.RecordNumber, options);
            AddAliases(memoValues, memoDefinition.Name, memoDefinition.ShortName, value, ambiguousMemoAliases);
        }

        return new TpsRecord(
            record.RecordNumber,
            values,
            memoValues,
            ambiguousFieldAliases,
            ambiguousMemoAliases);
    }

    private static TpsMemoValue BuildMemoValue(
        TpsMemo definition,
        MemoRecord? source,
        int recordNumber,
        TpsOpenOptions options)
    {
        if (source is null)
        {
            return new TpsMemoValue(definition, null, null);
        }

        if (definition.IsMemo)
        {
            return new TpsMemoValue(definition, source.ReadText(options.StringEncoding), null);
        }

        try
        {
            return new TpsMemoValue(definition, null, source.ReadBlob(options.IgnoreErrors));
        }
        catch (InvalidDataException ex)
        {
            throw new TpsParseException(new TpsParseError(
                $"Could not read BLOB '{definition.Name}' for record {recordNumber}: {ex.Message}",
                Exception: ex));
        }
    }

    private static HashSet<string> FindAmbiguousAliases<T>(
        IEnumerable<T> definitions,
        Func<T, string> getName,
        Func<T, string> getShortName)
    {
        var items = definitions.ToArray();
        var fullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!fullNames.Add(getName(item)))
            {
                throw new InvalidDataException($"Duplicate TPS field or MEMO name '{getName(item)}'.");
            }
        }

        var aliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var shortName = getShortName(item);
            if (string.IsNullOrWhiteSpace(shortName) || string.Equals(shortName, getName(item), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            aliases[shortName] = aliases.GetValueOrDefault(shortName) + 1;
        }

        return aliases
            .Where(alias => alias.Value > 1 || fullNames.Contains(alias.Key))
            .Select(alias => alias.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddAliases<T>(
        IDictionary<string, T> values,
        string name,
        string shortName,
        T value,
        IReadOnlySet<string> ambiguousAliases)
    {
        values.Add(name, value);
        if (!string.IsNullOrWhiteSpace(shortName) && !ambiguousAliases.Contains(shortName))
        {
            values.TryAdd(shortName, value);
        }
    }

    private static string ResolveTableName(
        int tableNumber,
        TableDefinitionRecord definition,
        IReadOnlyDictionary<int, string> tableNames)
    {
        if (tableNames.TryGetValue(tableNumber, out var tableName))
        {
            var normalizedName = tableName.TrimEnd('\0', ' ');
            if (!string.IsNullOrWhiteSpace(normalizedName) &&
                !string.Equals(normalizedName, "UNNAMED", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedName;
            }
        }

        var fieldPrefix = definition.Fields.FirstOrDefault()?.TablePrefix;
        return string.IsNullOrWhiteSpace(fieldPrefix)
            ? tableNumber.ToString(CultureInfo.InvariantCulture)
            : fieldPrefix;
    }

    private static TpsFieldType MapFieldType(int type) => type switch
    {
        1 => TpsFieldType.Byte,
        2 => TpsFieldType.Short,
        3 => TpsFieldType.UShort,
        4 => TpsFieldType.Date,
        5 => TpsFieldType.Time,
        6 => TpsFieldType.Long,
        7 => TpsFieldType.ULong,
        8 => TpsFieldType.SReal,
        9 => TpsFieldType.Real,
        0x0A => TpsFieldType.Decimal,
        0x12 => TpsFieldType.String,
        0x13 => TpsFieldType.CString,
        0x14 => TpsFieldType.PString,
        0x16 => TpsFieldType.Group,
        _ => TpsFieldType.Unknown
    };
}
