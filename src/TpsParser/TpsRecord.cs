using System.Globalization;

namespace TpsParser;

public sealed class TpsRecord
{
    private static readonly IReadOnlySet<string> EmptyAliases =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, object?> _valuesByName;
    private readonly IReadOnlyDictionary<string, TpsMemoValue> _memosByName;
    private readonly IReadOnlySet<string> _ambiguousFieldAliases;
    private readonly IReadOnlySet<string> _ambiguousMemoAliases;

    internal TpsRecord(
        int recordNumber,
        IReadOnlyDictionary<string, object?> valuesByName,
        IReadOnlyDictionary<string, TpsMemoValue> memosByName,
        IReadOnlySet<string>? ambiguousFieldAliases = null,
        IReadOnlySet<string>? ambiguousMemoAliases = null)
    {
        RecordNumber = recordNumber;
        _valuesByName = valuesByName;
        _memosByName = memosByName;
        _ambiguousFieldAliases = ambiguousFieldAliases ?? EmptyAliases;
        _ambiguousMemoAliases = ambiguousMemoAliases ?? EmptyAliases;
    }

    public int RecordNumber { get; }

    public object? GetValue(string name)
    {
        if (_valuesByName.TryGetValue(name, out var value))
        {
            return value;
        }

        if (_ambiguousFieldAliases.Contains(name))
        {
            throw new TpsParseException(new TpsParseError($"Field name '{name}' is ambiguous; use its full table-qualified name."));
        }

        throw new TpsParseException(new TpsParseError($"Field '{name}' was not found in record {RecordNumber}."));
    }

    public string? GetString(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        null => null,
        string text => text,
        var value => throw WrongType(name, "string", value)
    };

    public int GetInt32(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        byte value => value,
        short value => value,
        ushort value => value,
        int value => value,
        long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
        var value => throw WrongType(name, "Int32", value)
    };

    public uint GetUInt32(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        byte value => value,
        short value when value >= 0 => (uint)value,
        ushort value => value,
        int value when value >= 0 => (uint)value,
        long value when value is >= 0 and <= uint.MaxValue => (uint)value,
        var value => throw WrongType(name, "UInt32", value)
    };

    public double GetDouble(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        byte value => value,
        short value => value,
        ushort value => value,
        int value => value,
        long value => value,
        float value => value,
        double value => value,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        var value => throw WrongType(name, "Double", value)
    };

    public DateOnly? GetDate(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        null => null,
        DateOnly date => date,
        var value => throw WrongType(name, "DateOnly", value)
    };

    public TimeOnly? GetTime(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        null => null,
        TimeOnly time => time,
        var value => throw WrongType(name, "TimeOnly", value)
    };

    public byte[]? GetBytes(string name, int elementIndex = 0) => GetSingleValue(name, elementIndex) switch
    {
        null => null,
        byte[] bytes => bytes.ToArray(),
        var value => throw WrongType(name, "byte[]", value)
    };

    public string? GetMemo(string name)
    {
        var memo = GetMemoValue(name);
        if (!memo.Definition.IsMemo)
        {
            throw new TpsParseException(new TpsParseError($"Memo '{name}' is a BLOB, not a MEMO."));
        }

        return memo.Text;
    }

    public byte[]? GetBlob(string name)
    {
        var memo = GetMemoValue(name);
        if (!memo.Definition.IsBlob)
        {
            throw new TpsParseException(new TpsParseError($"Memo '{name}' is a MEMO, not a BLOB."));
        }

        return memo.Blob?.ToArray();
    }

    public string GetDecimalString(string name, int elementIndex = 0)
    {
        var value = GetSingleValue(name, elementIndex);
        return value as string ?? throw WrongType(name, "decimal string", value);
    }

    public bool TryGetDecimal(string name, out decimal value, int elementIndex = 0)
    {
        var raw = GetSingleValue(name, elementIndex);
        if (raw is string text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private object? GetSingleValue(string name, int elementIndex)
    {
        var value = GetValue(name);
        if (value is object?[] values)
        {
            if ((uint)elementIndex >= (uint)values.Length)
            {
                throw new TpsParseException(new TpsParseError($"Element index {elementIndex} is outside field '{name}' with {values.Length} elements."));
            }

            return values[elementIndex];
        }

        if (elementIndex != 0)
        {
            throw new TpsParseException(new TpsParseError($"Element index {elementIndex} is invalid because field '{name}' is not an array."));
        }

        return value;
    }

    private TpsMemoValue GetMemoValue(string name)
    {
        if (_memosByName.TryGetValue(name, out var value))
        {
            return value;
        }

        if (_ambiguousMemoAliases.Contains(name))
        {
            throw new TpsParseException(new TpsParseError($"MEMO/BLOB name '{name}' is ambiguous; use its full table-qualified name."));
        }

        throw new TpsParseException(new TpsParseError($"Memo/BLOB '{name}' was not found in record {RecordNumber}."));
    }

    private static TpsParseException WrongType(string name, string expected, object? actual)
    {
        var actualName = actual?.GetType().Name ?? "null";
        return new TpsParseException(new TpsParseError($"Field '{name}' cannot be read as {expected}; actual value type is {actualName}."));
    }
}

internal sealed record TpsMemoValue(TpsMemo Definition, string? Text, byte[]? Blob);
