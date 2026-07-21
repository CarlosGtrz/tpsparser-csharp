using System.Text;
using TpsInspector;

namespace TpsParser.Tests;

public sealed class CsvExporterTests
{
    [Fact]
    public void Export_writes_typed_values_memos_and_blobs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var textMemo = new TpsMemo(1, "T:NOTE", "NOTE", 0, isBlob: false);
            var firstBlob = new TpsMemo(2, "T:A/B", "A/B", 4, isBlob: true);
            var secondBlob = new TpsMemo(3, "T:A\\B", "A\\B", 4, isBlob: true);
            var fields = new[]
            {
                Field(1, "T:TEXT", TpsFieldType.String),
                Field(2, "T:ARRAY", TpsFieldType.Long, elementCount: 2),
                Field(3, "T:BYTES", TpsFieldType.Group),
                Field(4, "T:NULLBYTES", TpsFieldType.Group),
                Field(5, "T:DATE", TpsFieldType.Date),
                Field(6, "T:TIME", TpsFieldType.Time)
            };
            var firstRecord = Record(
                42,
                new Dictionary<string, object?>
                {
                    ["T:TEXT"] = "hello\0  ",
                    ["T:ARRAY"] = new object?[] { 1, 2 },
                    ["T:BYTES"] = new byte[] { 0x01, 0xA4, 0xFF },
                    ["T:NULLBYTES"] = null,
                    ["T:DATE"] = new DateOnly(2024, 2, 3),
                    ["T:TIME"] = new TimeOnly(4, 5, 6, 70)
                },
                new Dictionary<string, TpsMemoValue>
                {
                    ["T:NOTE"] = new TpsMemoValue(textMemo, "line 1,\r\n\"line 2\"", null),
                    ["T:A/B"] = new TpsMemoValue(firstBlob, null, new byte[] { 0x00, 0xFF }),
                    ["T:A\\B"] = new TpsMemoValue(secondBlob, null, [])
                });
            var secondRecord = Record(
                43,
                new Dictionary<string, object?>
                {
                    ["T:TEXT"] = null,
                    ["T:ARRAY"] = new object?[] { null, null },
                    ["T:BYTES"] = Array.Empty<byte>(),
                    ["T:NULLBYTES"] = null,
                    ["T:DATE"] = null,
                    ["T:TIME"] = new TimeOnly(0, 0)
                },
                new Dictionary<string, TpsMemoValue>
                {
                    ["T:NOTE"] = new TpsMemoValue(textMemo, null, null),
                    ["T:A/B"] = new TpsMemoValue(firstBlob, null, null),
                    ["T:A\\B"] = new TpsMemoValue(secondBlob, null, null)
                });
            var table = new TpsTable(
                1,
                "T",
                fields,
                [textMemo, firstBlob, secondBlob],
                [],
                [firstRecord, secondRecord]);

            var exported = CsvExporter.Export(Path.Combine(directory, "sample.tps"), [table]);

            var csvPath = Assert.Single(exported);
            Assert.Equal(Path.Combine(directory, "sample.csv"), csvPath);
            var bytes = File.ReadAllBytes(csvPath);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

            var csv = File.ReadAllText(csvPath, Encoding.UTF8);
            Assert.StartsWith(
                "RecordNumber,T:TEXT,T:ARRAY[1],T:ARRAY[2],T:BYTES,T:NULLBYTES,T:DATE,T:TIME,T:NOTE,T:A/B,T:A\\B\r\n",
                csv);
            Assert.Contains(
                "42,hello,1,2,0x01A4FF,,2024-02-03,04:05:06.07,\"line 1,\r\n\"\"line 2\"\"\",sample-42-A_B.blob,sample-42-A_B-3.blob\r\n",
                csv);
            Assert.Contains("43,,,,0x,,,00:00:00,,,\r\n", csv);
            Assert.Equal(new byte[] { 0x00, 0xFF }, File.ReadAllBytes(Path.Combine(directory, "sample-42-A_B.blob")));
            Assert.Empty(File.ReadAllBytes(Path.Combine(directory, "sample-42-A_B-3.blob")));
            Assert.False(File.Exists(Path.Combine(directory, "sample-43-A_B.blob")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Export_adds_sanitized_table_names_and_resolves_collisions()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var tables = new[]
            {
                new TpsTable(1, "A/B", [], [], [], []),
                new TpsTable(2, "A\\B", [], [], [], [])
            };

            var exported = CsvExporter.Export(Path.Combine(directory, "source.tps"), tables);

            Assert.Equal(
                [Path.Combine(directory, "source-A_B.csv"), Path.Combine(directory, "source-A_B-2.csv")],
                exported);
            Assert.All(exported, path => Assert.True(File.Exists(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TpsField Field(int number, string name, TpsFieldType type, int elementCount = 1) =>
        new(number, name, name[(name.IndexOf(':') + 1)..], "T", type, 0, 1, elementCount, 0, 0);

    private static TpsRecord Record(
        int number,
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlyDictionary<string, TpsMemoValue> memos) =>
        new(number, fields, memos);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
