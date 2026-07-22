using System.Text;
using TpsParser.Internal;

namespace TpsParser.Tests;

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
            AddUInt16(bytes, 1);
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

    private sealed record FieldSpec(int Type, int Offset, string Name, int Length);
}
