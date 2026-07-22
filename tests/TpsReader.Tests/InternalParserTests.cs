using System.Text;
using TpsReader.Internal;

namespace TpsReader.Tests;

public sealed class InternalParserTests
{
    [Fact]
    public void BinaryReader_distinguishes_signed_and_unsigned_shorts()
    {
        Assert.Equal(-1, new TpsBinaryReader([0xFF, 0xFF]).ReadInt16LittleEndian());
        Assert.Equal(ushort.MaxValue, new TpsBinaryReader([0xFF, 0xFF]).ReadUInt16LittleEndian());
    }

    [Fact]
    public void BinaryReader_rejects_invalid_ranges_and_bad_rle()
    {
        Assert.Throws<InvalidDataException>(() => new TpsBinaryReader([1]).ReadInt32LittleEndian());
        Assert.Throws<InvalidDataException>(() => TpsRunLengthEncoding.Decompress(new TpsBinaryReader([0, 0]), 1));
    }

    [Fact]
    public void TableDefinition_parses_signed_short_and_complete_time()
    {
        var definition = CreateDefinition(
            new FieldSpec(2, 0, "T:SIGNED", 2),
            new FieldSpec(5, 2, "T:TIME", 4));

        var values = definition.ParseRecord([
            0xFF, 0xFF,
            42, 7, 8, 9
        ]);

        Assert.Equal((short)-1, values[0]);
        Assert.Equal(new TimeOnly(9, 8, 7, 420), values[1]);
    }

    [Fact]
    public void StringEncoding_applies_to_schema_names_and_values()
    {
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var definition = CreateDefinition(
            [new FieldSpec(0x12, 0, "T:CAFÉ", 1)],
            windows1252);

        var values = definition.ParseRecord(windows1252.GetBytes("é"));

        Assert.Equal("T:CAFÉ", definition.Fields[0].Name);
        Assert.Equal("é", values[0]);
    }

    [Fact]
    public void Fixed_strings_trim_trailing_nul_and_space_padding()
    {
        var definition = CreateDefinition(new FieldSpec(0x12, 0, "T:TEXT", 8));

        var values = definition.ParseRecord("value\0  "u8.ToArray());

        Assert.Equal("value", values[0]);
    }

    [Fact]
    public void Group_projection_overlays_only_logical_string_content()
    {
        var definition = CreateDefinition(
            new FieldSpec(0x16, 0, "T:GROUP", 20),
            new FieldSpec(6, 0, "T:BINARY", 4),
            new FieldSpec(0x12, 4, "T:FIXED", 4),
            new FieldSpec(0x13, 8, "T:CSTRING", 5),
            new FieldSpec(0x14, 13, "T:PSTRING", 5));
        byte[] record =
        [
            1, 2, 3, 4,
            (byte)'A', 0x20, 0x20, 0x20,
            (byte)'B', (byte)'C', 0, 0xFF, 0xFF,
            2, (byte)'D', (byte)'E', 0xFF, 0xFF,
            0xAA, 0xBB
        ];

        var values = definition.ParseRecord(record);
        var group = Assert.IsType<TpsGroupValue>(values[0]);

        Assert.Equal(20, group.Text.Length);
        Assert.Equal("    A   BC   DE     ", group.Text);
        Assert.Equal(record, group.CopyRawBytes());
        Assert.Equal("A", values[2]);
        Assert.Equal("BC", values[3]);
        Assert.Equal("DE", values[4]);
    }

    [Fact]
    public void Arrayed_groups_repeat_nested_string_and_string_array_prototypes()
    {
        var definition = CreateDefinition(
            new FieldSpec(0x16, 0, "T:OUTER", 24, ElementCount: 2),
            new FieldSpec(0x16, 2, "T:INNER", 8),
            new FieldSpec(0x12, 4, "T:TEXTS", 6, ElementCount: 2));
        var record = Enumerable.Repeat((byte)0xFF, 24).ToArray();
        "A  B  "u8.CopyTo(record.AsSpan(4));
        "C  D  "u8.CopyTo(record.AsSpan(16));

        var values = definition.ParseRecord(record);
        var groups = Assert.IsType<object?[]>(values[0]);
        var first = Assert.IsType<TpsGroupValue>(groups[0]);
        var second = Assert.IsType<TpsGroupValue>(groups[1]);
        var nested = Assert.IsType<TpsGroupValue>(values[1]);

        Assert.Equal("    A  B    ", first.Text);
        Assert.Equal("    C  D    ", second.Text);
        Assert.Equal("  A  B  ", nested.Text);
        Assert.Equal(record[..12], first.CopyRawBytes());
        Assert.Equal(record[12..], second.CopyRawBytes());
    }

    [Theory]
    [InlineData(5, 2, 0, 5)]
    [InlineData(12, 2, 4, 4)]
    public void Malformed_group_dimensions_and_component_ranges_fail_atomically(
        int groupLength,
        int groupElements,
        int stringOffset,
        int stringLength)
    {
        var definition = CreateDefinition(
            new FieldSpec(0x16, 0, "T:GROUP", groupLength, groupElements),
            new FieldSpec(0x12, stringOffset, "T:TEXT", stringLength));

        Assert.Throws<InvalidDataException>(() => definition.ParseRecord(new byte[Math.Max(groupLength, stringOffset + stringLength)]));
    }

    [Fact]
    public void Blob_recovery_returns_only_available_payload_bytes()
    {
        var header = CreateMemoHeader();
        var truncated = new MemoRecord(header, new TpsBinaryReader([
            10, 0, 0, 0,
            1, 2, 3
        ]));

        Assert.Throws<InvalidDataException>(() => truncated.ReadBlob(ignoreErrors: false));
        Assert.Equal([1, 2, 3], truncated.ReadBlob(ignoreErrors: true));

        var missingHeader = new MemoRecord(CreateMemoHeader(), new TpsBinaryReader([1, 2, 3]));
        Assert.Empty(missingHeader.ReadBlob(ignoreErrors: true));
    }

    [Fact]
    public void Memo_text_uses_requested_encoding()
    {
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var memo = new MemoRecord(CreateMemoHeader(), new TpsBinaryReader(windows1252.GetBytes("café")));

        Assert.Equal("café", memo.ReadText(windows1252));
    }

    [Fact]
    public void IgnoreErrors_discards_a_failed_page_as_a_unit()
    {
        var page = new TpsPage(new TpsBinaryReader([
            0, 2, 0, 0,
            15, 0,
            20, 0,
            0, 0,
            1, 0,
            0,
            0, 0
        ]));

        Assert.Throws<InvalidDataException>(() =>
            TpsFileReader.TryReadPage(page, Encoding.Latin1, ignoreErrors: false, out _));
        Assert.False(TpsFileReader.TryReadPage(page, Encoding.Latin1, ignoreErrors: true, out var records));
        Assert.Empty(records);
    }

    private static TableDefinitionRecord CreateDefinition(params FieldSpec[] fields) =>
        CreateDefinition(fields, Encoding.Latin1);

    private static TableDefinitionRecord CreateDefinition(FieldSpec[] fields, Encoding encoding)
    {
        var bytes = new List<byte>();
        AddUInt16(bytes, 1);
        AddUInt16(bytes, fields.Sum(field => field.Length));
        AddUInt16(bytes, fields.Length);
        AddUInt16(bytes, 0);
        AddUInt16(bytes, 0);

        foreach (var field in fields)
        {
            bytes.Add((byte)field.Type);
            AddUInt16(bytes, field.Offset);
            bytes.AddRange(encoding.GetBytes(field.Name));
            bytes.Add(0);
            AddUInt16(bytes, field.ElementCount);
            AddUInt16(bytes, field.Length);
            AddUInt16(bytes, 0);
            AddUInt16(bytes, 0);
            if (field.Type is 0x12 or 0x13 or 0x14)
            {
                AddUInt16(bytes, field.Length);
                bytes.Add(0);
                bytes.Add(1);
            }
        }

        return new TableDefinitionRecord(new TpsBinaryReader(bytes.ToArray()), encoding);
    }

    private static MemoHeader CreateMemoHeader()
    {
        return new MemoHeader(new TpsBinaryReader([
            0, 0, 0, 1,
            0xFC,
            0, 0, 0, 2,
            0,
            0, 0
        ]));
    }

    private static void AddUInt16(ICollection<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private sealed record FieldSpec(int Type, int Offset, string Name, int Length, int ElementCount = 1);
}
