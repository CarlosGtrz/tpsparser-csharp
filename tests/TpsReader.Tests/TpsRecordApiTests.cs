using TpsReader.Internal;

namespace TpsReader.Tests;

public sealed class TpsRecordApiTests
{
    [Fact]
    public void Typed_getters_handle_supported_values_and_copy_bytes()
    {
        var sourceBytes = new byte[] { 1, 2, 3 };
        var record = CreateRecord(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEXT"] = "value",
            ["SIGNED"] = (short)-12,
            ["UNSIGNED"] = (ushort)65000,
            ["DOUBLE"] = 1.5f,
            ["DATE"] = new DateOnly(2024, 2, 3),
            ["TIME"] = new TimeOnly(4, 5, 6, 70),
            ["BYTES"] = sourceBytes,
            ["ARRAY"] = new object?[] { 7, 8 },
            ["DECIMAL"] = "123.45"
        });

        Assert.Equal("value", record.GetString("TEXT"));
        Assert.Equal(-12, record.GetInt32("SIGNED"));
        Assert.Equal(65000u, record.GetUInt32("UNSIGNED"));
        Assert.Equal(1.5, record.GetDouble("DOUBLE"));
        Assert.Equal(new DateOnly(2024, 2, 3), record.GetDate("DATE"));
        Assert.Equal(new TimeOnly(4, 5, 6, 70), record.GetTime("TIME"));
        Assert.Equal(8, record.GetInt32("ARRAY", 1));
        Assert.Equal("123.45", record.GetDecimalString("DECIMAL"));
        Assert.True(record.TryGetDecimal("DECIMAL", out var number));
        Assert.Equal(123.45m, number);

        var returnedBytes = record.GetBytes("BYTES")!;
        returnedBytes[0] = 99;
        Assert.Equal(1, sourceBytes[0]);
        Assert.NotSame(returnedBytes, record.GetValue("BYTES"));
    }

    [Fact]
    public void Generic_access_supports_checked_conversions_nullable_values_and_typed_arrays()
    {
        var record = CreateRecord(
            new Dictionary<string, object?>
            {
                ["NUMBER"] = (short)12,
                ["OVERFLOW"] = long.MaxValue,
                ["NULL"] = null,
                ["DATE"] = new DateOnly(2025, 6, 7),
                ["ARRAY"] = new object?[] { 3, 4 },
                ["DECIMAL"] = "1234567890.25",
                ["TOO_PRECISE"] = "0.12345678901234567890123456789",
                ["NUMERIC_TEXT"] = "42"
            },
            new Dictionary<string, TpsFieldType>
            {
                ["NUMBER"] = TpsFieldType.Short,
                ["OVERFLOW"] = TpsFieldType.ULong,
                ["NULL"] = TpsFieldType.Long,
                ["DATE"] = TpsFieldType.Date,
                ["ARRAY"] = TpsFieldType.Long,
                ["DECIMAL"] = TpsFieldType.Decimal,
                ["TOO_PRECISE"] = TpsFieldType.Decimal,
                ["NUMERIC_TEXT"] = TpsFieldType.String
            });

        Assert.Equal(12L, record.Get<long>("NUMBER"));
        Assert.Equal(12, record.Get<int?>("NUMBER"));
        Assert.Null(record.Get<int?>("NULL"));
        Assert.Equal(new DateOnly(2025, 6, 7), record.Get<DateOnly>("DATE"));
        Assert.Equal([3, 4], record.Get<int[]>("ARRAY")!);
        Assert.Equal(4, record.Get<int>("ARRAY", 1));
        Assert.Equal(1234567890.25m, record.Get<decimal>("DECIMAL"));
        Assert.False(record.TryGet<decimal>("TOO_PRECISE", out _));
        Assert.False(record.TryGetDecimal("TOO_PRECISE", out _));
        Assert.Throws<OverflowException>(() => record.Get<int>("OVERFLOW"));
        Assert.Throws<TpsParseException>(() => record.Get<int>("NUMERIC_TEXT"));
    }

    [Fact]
    public void Generic_array_access_supports_aot_safe_tps_value_types()
    {
        var date = new DateOnly(2026, 7, 22);
        var time = new TimeOnly(9, 30, 15);
        var record = CreateRecord(
            new Dictionary<string, object?>
            {
                ["NUMBERS"] = new object?[] { 1, 2 },
                ["NULLABLE_NUMBERS"] = new object?[] { 1, null, 3 },
                ["DECIMALS"] = new object?[] { "1.25", null },
                ["DATES"] = new object?[] { date, null },
                ["TIMES"] = new object?[] { time, null },
                ["STRINGS"] = new object?[] { "A", null }
            },
            new Dictionary<string, TpsFieldType>
            {
                ["NUMBERS"] = TpsFieldType.Long,
                ["NULLABLE_NUMBERS"] = TpsFieldType.Long,
                ["DECIMALS"] = TpsFieldType.Decimal,
                ["DATES"] = TpsFieldType.Date,
                ["TIMES"] = TpsFieldType.Time,
                ["STRINGS"] = TpsFieldType.String
            });

        Assert.Equal([1, 2], record.Get<byte[]>("NUMBERS")!);
        Assert.Equal([(sbyte)1, (sbyte)2], record.Get<sbyte[]>("NUMBERS")!);
        Assert.Equal([(short)1, (short)2], record.Get<short[]>("NUMBERS")!);
        Assert.Equal([(ushort)1, (ushort)2], record.Get<ushort[]>("NUMBERS")!);
        Assert.Equal([1, 2], record.Get<int[]>("NUMBERS")!);
        Assert.Equal([1u, 2u], record.Get<uint[]>("NUMBERS")!);
        Assert.Equal([1L, 2L], record.Get<long[]>("NUMBERS")!);
        Assert.Equal([1UL, 2UL], record.Get<ulong[]>("NUMBERS")!);
        Assert.Equal([1f, 2f], record.Get<float[]>("NUMBERS")!);
        Assert.Equal([1d, 2d], record.Get<double[]>("NUMBERS")!);
        Assert.Equal([1m, 2m], record.Get<decimal[]>("NUMBERS")!);
        Assert.Equal([1, null, 3], record.Get<int?[]>("NULLABLE_NUMBERS")!);
        Assert.Equal([1.25m, null], record.Get<decimal?[]>("DECIMALS")!);
        Assert.Equal([date, null], record.Get<DateOnly?[]>("DATES")!);
        Assert.Equal([time, null], record.Get<TimeOnly?[]>("TIMES")!);
        Assert.True(record.Get<string?[]>("STRINGS")!.SequenceEqual(["A", null]));
        Assert.Equal([1, 2], record.Get<object?[]>("NUMBERS")!);
    }

    [Fact]
    public void TryGet_returns_false_for_access_and_conversion_failures_only()
    {
        var record = new TpsRecord(
            1,
            new Dictionary<string, object?>
            {
                ["A:CODE"] = 1,
                ["B:CODE"] = 2,
                ["NULL"] = null,
                ["TEXT"] = "text",
                ["OVERFLOW"] = long.MaxValue,
                ["ARRAY"] = new object?[] { 1 }
            },
            new Dictionary<string, TpsMemoValue>(),
            new HashSet<string>(["CODE"], StringComparer.OrdinalIgnoreCase));

        Assert.True(record.TryGet("A:CODE", out int code));
        Assert.Equal(1, code);
        Assert.False(record.TryGet("MISSING", out int _));
        Assert.False(record.TryGet("CODE", out int _));
        Assert.False(record.TryGet("NULL", out int _));
        Assert.False(record.TryGet("TEXT", out int _));
        Assert.False(record.TryGet("OVERFLOW", out int _));
        Assert.False(record.TryGet("ARRAY", 2, out int _));
        Assert.Throws<ArgumentException>(() => record.TryGet(" ", out int _));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.TryGet("ARRAY", -1, out int _));
    }

    [Fact]
    public void Indexer_and_GetValue_unify_fields_memos_and_blobs()
    {
        var memoDefinition = new TpsMemo(1, "T:NOTE", "NOTE", 0, isBlob: false);
        var blobDefinition = new TpsMemo(2, "T:IMAGE", "IMAGE", 4, isBlob: true);
        var blob = new byte[] { 4, 5, 6 };
        var record = new TpsRecord(
            1,
            new Dictionary<string, object?> { ["T:NAME"] = "Ada" },
            new Dictionary<string, TpsMemoValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["NOTE"] = new TpsMemoValue(memoDefinition, "hello", null),
                ["IMAGE"] = new TpsMemoValue(blobDefinition, null, blob)
            });

        Assert.Equal("Ada", record["T:NAME"]);
        Assert.Equal("hello", record["NOTE"]);
        Assert.Equal("hello", record.GetValue("NOTE"));
        Assert.Equal("hello", record.Get<string>("NOTE"));
        Assert.Equal("hello", record.GetMemo("NOTE"));
        Assert.Throws<TpsParseException>(() => record.GetBlob("NOTE"));
        Assert.Throws<TpsParseException>(() => record.GetMemo("IMAGE"));

        var fromValue = Assert.IsType<byte[]>(record.GetValue("IMAGE"));
        var fromGeneric = record.Get<byte[]>("IMAGE")!;
        var fromInterface = record.Get<IReadOnlyList<byte>>("IMAGE")!;
        fromValue[0] = 99;
        fromGeneric[1] = 99;
        Assert.Equal([4, 5, 6], fromInterface);
        Assert.Equal([4, 5, 6], blob);
        Assert.Equal([4, 5, 6], record.GetBlob("IMAGE"));
    }

    [Fact]
    public void Unified_lookup_rejects_field_and_memo_name_collisions()
    {
        var memoDefinition = new TpsMemo(1, "T:VALUE", "VALUE", 0, isBlob: false);
        var record = new TpsRecord(
            1,
            new Dictionary<string, object?> { ["VALUE"] = "field" },
            new Dictionary<string, TpsMemoValue>
            {
                ["VALUE"] = new TpsMemoValue(memoDefinition, "memo", null)
            });

        Assert.Throws<TpsParseException>(() => record.GetValue("VALUE"));
        Assert.False(record.TryGet<string>("VALUE", out _));
        Assert.Equal("memo", record.GetMemo("VALUE"));
    }

    [Fact]
    public void Group_values_default_to_text_and_expose_cloned_raw_bytes()
    {
        var raw = new byte[] { 1, 2, 3, 4 };
        var first = new TpsGroupValue("A   ", raw);
        var second = new TpsGroupValue("B   ", [5, 6, 7, 8]);
        var record = CreateRecord(new Dictionary<string, object?>
        {
            ["GROUP"] = first,
            ["GROUPS"] = new object?[] { first, second }
        });

        Assert.Equal("A   ", record["GROUP"]);
        Assert.Equal("A   ", record.GetString("GROUP"));
        Assert.Equal("A   ", record.Get<string>("GROUP"));
        Assert.Equal(["A   ", "B   "], record.Get<string[]>("GROUPS")!);
        Assert.Equal("B   ", record.Get<string>("GROUPS", 1));
        Assert.Equal(["A   ", "B   "], Assert.IsType<string[]>(record.GetValue("GROUPS")));

        var bytes = record.Get<byte[]>("GROUP")!;
        var arrayBytes = record.Get<byte[][]>("GROUPS")!;
        bytes[0] = 99;
        arrayBytes[1][0] = 99;
        Assert.Equal([1, 2, 3, 4], record.GetBytes("GROUP"));
        Assert.Equal([5, 6, 7, 8], record.GetBytes("GROUPS", 1));
    }

    [Fact]
    public void Scalar_fields_reject_nonzero_element_indexes()
    {
        var record = CreateRecord(new Dictionary<string, object?> { ["VALUE"] = 1 });

        var error = Assert.Throws<TpsParseException>(() => record.GetInt32("VALUE", 1));

        Assert.Contains("is not an array", error.Message);
    }

    [Fact]
    public void Ambiguous_aliases_require_a_full_name()
    {
        var record = new TpsRecord(
            1,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["A:CODE"] = 1,
                ["B:CODE"] = 2
            },
            new Dictionary<string, TpsMemoValue>(),
            new HashSet<string>(["CODE"], StringComparer.OrdinalIgnoreCase));

        var error = Assert.Throws<TpsParseException>(() => record.GetInt32("CODE"));

        Assert.Contains("ambiguous", error.Message);
        Assert.Equal(1, record.GetInt32("A:CODE"));
        Assert.Equal(2, record.GetInt32("B:CODE"));
    }

    private static TpsRecord CreateRecord(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, TpsFieldType>? fieldTypes = null) =>
        new(1, values, new Dictionary<string, TpsMemoValue>(), fieldTypesByName: fieldTypes);
}
