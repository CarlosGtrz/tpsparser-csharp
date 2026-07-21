namespace TpsParser.Tests;

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

    [Fact]
    public void Memo_and_blob_getters_validate_types_and_copy_blob_data()
    {
        var memoDefinition = new TpsMemo(1, "T:NOTE", "NOTE", 0, isBlob: false);
        var blobDefinition = new TpsMemo(2, "T:IMAGE", "IMAGE", 4, isBlob: true);
        var blob = new byte[] { 4, 5, 6 };
        var record = new TpsRecord(
            1,
            new Dictionary<string, object?>(),
            new Dictionary<string, TpsMemoValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["NOTE"] = new TpsMemoValue(memoDefinition, "hello", null),
                ["IMAGE"] = new TpsMemoValue(blobDefinition, null, blob)
            });

        Assert.Equal("hello", record.GetMemo("NOTE"));
        Assert.Throws<TpsParseException>(() => record.GetBlob("NOTE"));
        Assert.Throws<TpsParseException>(() => record.GetMemo("IMAGE"));
        var returnedBlob = record.GetBlob("IMAGE")!;
        returnedBlob[0] = 99;
        Assert.Equal(4, blob[0]);
    }

    private static TpsRecord CreateRecord(IReadOnlyDictionary<string, object?> values) =>
        new(1, values, new Dictionary<string, TpsMemoValue>());
}
