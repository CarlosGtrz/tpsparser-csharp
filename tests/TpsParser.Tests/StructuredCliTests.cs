using System.Text.Json;
using TpsInspector;

namespace TpsParser.Tests;

public sealed class StructuredCliTests
{
    [Fact]
    public void General_help_lists_agent_facing_commands_and_legacy_help_still_works()
    {
        var output = new StringWriter();

        Assert.Equal(0, InspectorApplication.Run(["--help"], output, new StringWriter()));
        Assert.Contains("tps schema", output.ToString());
        Assert.Contains("tps rows", output.ToString());
        Assert.Contains("tps export", output.ToString());

        output.GetStringBuilder().Clear();
        Assert.Equal(0, InspectorApplication.Run(["inspect", "--help"], output, new StringWriter()));
        Assert.Contains("--recursive", output.ToString());
        Assert.Contains("--owner-env", output.ToString());
    }

    [Fact]
    public void Schema_writes_parseable_metadata_json_and_can_select_a_table()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InspectorApplication.Run(
            ["schema", Fixture("CUSTOMER.TPS"), "--table", "14"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        var table = Assert.Single(root.GetProperty("tables").EnumerateArray());
        Assert.Equal(14, table.GetProperty("tableNumber").GetInt32());
        Assert.Equal("CUS", table.GetProperty("name").GetString());
        Assert.Equal(7, table.GetProperty("recordCount").GetInt32());
        Assert.Equal(8, table.GetProperty("fields").GetArrayLength());
        Assert.Equal("Long", table.GetProperty("fields")[0].GetProperty("type").GetString());
        Assert.Equal(3, table.GetProperty("indexes").GetArrayLength());
    }

    [Fact]
    public void Rows_projects_canonical_fields_and_applies_typed_and_predicates()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InspectorApplication.Run(
            [
                "rows", Fixture("CUSTOMER.TPS"),
                "--fields", "CUSTNUMBER,COMPANY,STATE",
                "--where", "STATE", "eq", "sc",
                "--where", "@recordNumber", "ge", "17"
            ],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var query = document.RootElement.GetProperty("query");
        Assert.Equal(2, query.GetProperty("matched").GetInt32());
        Assert.Equal(100, query.GetProperty("limit").GetInt32());
        Assert.False(query.GetProperty("hasMore").GetBoolean());
        var rows = document.RootElement.GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(17, rows[0].GetProperty("recordNumber").GetInt32());
        var values = rows[0].GetProperty("values");
        Assert.True(values.TryGetProperty("CUS:CUSTNUMBER", out _));
        Assert.True(values.TryGetProperty("CUS:COMPANY", out _));
        Assert.Equal("SC", values.GetProperty("CUS:STATE").GetString());
    }

    [Fact]
    public void Json_lines_are_compact_versioned_and_keep_diagnostics_off_stdout()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InspectorApplication.Run(
            ["rows", Fixture("CUSTOMER.TPS"), "--fields", "CUSTNUMBER", "--limit", "1", "--format", "jsonl"],
            output,
            error);

        Assert.Equal(0, exitCode);
        var line = Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(16, document.RootElement.GetProperty("recordNumber").GetInt32());
        Assert.Contains("More records are available", error.ToString());
        Assert.DoesNotContain("Matched", output.ToString());
    }

    [Fact]
    public void Invalid_predicate_returns_usage_error_without_structured_output()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InspectorApplication.Run(
            ["rows", Fixture("CUSTOMER.TPS"), "--where", "COMPANY", "gt", "A"],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("is not valid", error.ToString());
    }

    [Fact]
    public void Structured_parse_failure_returns_exit_two_without_stdout_data()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "bad.tps");
            File.WriteAllText(path, "not a TPS file");
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = InspectorApplication.Run(["schema", path], output, error);

            Assert.Equal(2, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("Could not open or parse TPS file", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Owner_environment_variable_opens_an_encrypted_file()
    {
        var variable = $"TPS_OWNER_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "a");
        try
        {
            var output = new StringWriter();

            var exitCode = InspectorApplication.Run(
                ["rows", Fixture("encrypted-a.tps"), "--owner-env", variable, "--limit", "1"],
                output,
                new StringWriter());

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, document.RootElement.GetProperty("rows").GetArrayLength());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Missing_owner_environment_variable_is_a_usage_error()
    {
        var variable = $"TPS_OWNER_{Guid.NewGuid():N}";
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InspectorApplication.Run(
            ["schema", Fixture("CUSTOMER.TPS"), "--owner-env", variable],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("missing or empty", error.ToString());
    }

    [Fact]
    public void Export_filters_and_projects_into_an_explicit_directory()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputDirectory = Path.Combine(directory, "nested", "export");
            var output = new StringWriter();

            var exitCode = InspectorApplication.Run(
                [
                    "export", Fixture("CUSTOMER.TPS"), "--output", outputDirectory,
                    "--fields", "CUSTNUMBER,COMPANY", "--where", "STATE", "eq", "FL"
                ],
                output,
                new StringWriter());

            Assert.Equal(0, exitCode);
            var csvPath = Path.Combine(outputDirectory, "CUSTOMER.csv");
            Assert.Equal(csvPath + Environment.NewLine, output.ToString());
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(3, lines.Length);
            Assert.Equal("RecordNumber,CUS:CUSTNUMBER,CUS:COMPANY", lines[0]);
            Assert.StartsWith("20,5,", lines[1]);
            Assert.StartsWith("21,6,", lines[2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Query_supports_lossless_decimal_date_time_group_array_and_text_operations()
    {
        var fields = new[]
        {
            Field(1, "T:DECIMAL", TpsFieldType.Decimal),
            Field(2, "T:DATE", TpsFieldType.Date),
            Field(3, "T:TIME", TpsFieldType.Time),
            Field(4, "T:GROUP", TpsFieldType.Group),
            Field(5, "T:ARRAY", TpsFieldType.Long, elementCount: 2),
            Field(6, "T:TEXT", TpsFieldType.String)
        };
        var table = new TpsTable(
            1,
            "T",
            fields,
            [],
            [],
            [
                Record(1, new Dictionary<string, object?>
                {
                    ["T:DECIMAL"] = "123456789012345678901234567890.10",
                    ["T:DATE"] = new DateOnly(2025, 1, 2),
                    ["T:TIME"] = new TimeOnly(12, 30, 15, 400),
                    ["T:GROUP"] = new byte[] { 0x01, 0xA4 },
                    ["T:ARRAY"] = new object?[] { 2, 9 },
                    ["T:TEXT"] = "Technology Today".PadRight(30)
                }),
                Record(2, new Dictionary<string, object?>
                {
                    ["T:DECIMAL"] = "123456789012345678901234567889.99",
                    ["T:DATE"] = new DateOnly(2024, 1, 2),
                    ["T:TIME"] = new TimeOnly(11, 30),
                    ["T:GROUP"] = new byte[] { 0x00, 0x00 },
                    ["T:ARRAY"] = new object?[] { 2, 3 },
                    ["T:TEXT"] = "Other".PadRight(30)
                })
            ]);
        var request = Request(
            new PredicateInput("DECIMAL", "gt", "123456789012345678901234567890.09"),
            new PredicateInput("DATE", "ge", "2025-01-02"),
            new PredicateInput("TIME", "eq", "12:30:15.40"),
            new PredicateInput("GROUP", "eq", "0x01a4"),
            new PredicateInput("ARRAY[2]", "ge", "9"),
            new PredicateInput("TEXT", "ends-with", "today"));

        var result = RecordQuery.Execute([table], request);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, Assert.Single(result.Records).RecordNumber);
    }

    [Fact]
    public void Query_null_case_sensitivity_validation_and_paging_are_deterministic()
    {
        var memo = new TpsMemo(1, "T:NOTE", "NOTE", 0, isBlob: false);
        var blob = new TpsMemo(2, "T:DATA", "DATA", 4, isBlob: true);
        var field = Field(1, "T:TEXT", TpsFieldType.String);
        var records = Enumerable.Range(1, 105)
            .Select(number => new TpsRecord(
                number,
                new Dictionary<string, object?> { [field.Name] = number == 1 ? "Value " : "value " },
                new Dictionary<string, TpsMemoValue>
                {
                    [memo.Name] = new TpsMemoValue(memo, number == 1 ? null : "memo", null),
                    [blob.Name] = new TpsMemoValue(blob, null, number == 1 ? null : [1, 2])
                }))
            .ToArray();
        var table = new TpsTable(1, "T", [field], [memo, blob], [], records);

        var page = RecordQuery.Execute(
            [table],
            new QueryRequest(null, [], null, [], 2, 100, CaseSensitive: false));
        Assert.Equal(105, page.MatchedCount);
        Assert.Equal(100, page.Records.Count);
        Assert.True(page.HasMore);

        var insensitive = RecordQuery.Execute(
            [table],
            Request(new PredicateInput("TEXT", "eq", "VALUE")));
        Assert.Equal(105, insensitive.MatchedCount);

        var sensitive = RecordQuery.Execute(
            [table],
            Request(caseSensitive: true, new PredicateInput("TEXT", "eq", "Value")));
        Assert.Equal(1, sensitive.MatchedCount);

        var nullResult = RecordQuery.Execute(
            [table],
            Request(
                new PredicateInput("NOTE", "is-null", null),
                new PredicateInput("DATA", "is-null", null)));
        Assert.Equal(1, nullResult.MatchedCount);

        var notNullResult = RecordQuery.Execute(
            [table],
            Request(
                new PredicateInput("NOTE", "is-not-null", null),
                new PredicateInput("DATA", "is-not-null", null)));
        Assert.Equal(104, notNullResult.MatchedCount);

        Assert.Throws<CliValidationException>(() => RecordQuery.Execute(
            [table],
            Request(new PredicateInput("TEXT", "lt", "x"))));
    }

    [Theory]
    [InlineData("eq", "2", 1)]
    [InlineData("ne", "2", 2)]
    [InlineData("lt", "2", 1)]
    [InlineData("le", "2", 2)]
    [InlineData("gt", "2", 1)]
    [InlineData("ge", "2", 2)]
    public void Numeric_predicate_operators_have_stable_semantics(string op, string literal, int expected)
    {
        var field = Field(1, "T:NUMBER", TpsFieldType.Long);
        var table = new TpsTable(
            1,
            "T",
            [field],
            [],
            [],
            Enumerable.Range(1, 3)
                .Select(number => Record(number, new Dictionary<string, object?> { [field.Name] = number }))
                .ToArray());

        var result = RecordQuery.Execute([table], Request(new PredicateInput("NUMBER", op, literal)));

        Assert.Equal(expected, result.MatchedCount);
    }

    [Theory]
    [InlineData("eq", "alphabet", 1)]
    [InlineData("ne", "alphabet", 2)]
    [InlineData("contains", "pha", 2)]
    [InlineData("starts-with", "alpha", 2)]
    [InlineData("ends-with", "bet", 1)]
    public void Text_predicate_operators_have_stable_semantics(string op, string literal, int expected)
    {
        var field = Field(1, "T:TEXT", TpsFieldType.String);
        var table = new TpsTable(
            1,
            "T",
            [field],
            [],
            [],
            [
                Record(1, new Dictionary<string, object?> { [field.Name] = "Alphabet ".PadRight(20) }),
                Record(2, new Dictionary<string, object?> { [field.Name] = "Alphanumeric".PadRight(20) }),
                Record(3, new Dictionary<string, object?> { [field.Name] = "Other".PadRight(20) })
            ]);

        var result = RecordQuery.Execute([table], Request(new PredicateInput("TEXT", op, literal)));

        Assert.Equal(expected, result.MatchedCount);
    }

    [Fact]
    public void Query_rejects_ambiguous_tables_unindexed_arrays_and_out_of_range_literals()
    {
        var array = Field(1, "T:ARRAY", TpsFieldType.Long, elementCount: 2);
        var byteField = Field(2, "T:BYTE", TpsFieldType.Byte);
        var first = new TpsTable(1, "FIRST", [array, byteField], [], [], []);
        var second = new TpsTable(2, "SECOND", [], [], [], []);

        Assert.Throws<CliValidationException>(() => RecordQuery.Execute(
            [first, second],
            Request(new PredicateInput("ARRAY", "eq", "1"))));
        Assert.Throws<CliValidationException>(() => RecordQuery.Execute(
            [first],
            Request(new PredicateInput("ARRAY", "eq", "1"))));
        Assert.Throws<CliValidationException>(() => RecordQuery.Execute(
            [first],
            Request(new PredicateInput("ARRAY[3]", "eq", "1"))));
        Assert.Throws<CliValidationException>(() => RecordQuery.Execute(
            [first],
            Request(new PredicateInput("BYTE", "eq", "256"))));
    }

    [Fact]
    public void Json_encoding_preserves_machine_types_and_blob_metadata()
    {
        var blob = new TpsMemo(1, "T:DATA", "DATA", 4, isBlob: true);
        var fields = new[]
        {
            Field(1, "T:DECIMAL", TpsFieldType.Decimal),
            Field(2, "T:DATE", TpsFieldType.Date),
            Field(3, "T:TIME", TpsFieldType.Time),
            Field(4, "T:GROUP", TpsFieldType.Group),
            Field(5, "T:REAL", TpsFieldType.Real)
        };
        var record = new TpsRecord(
            7,
            new Dictionary<string, object?>
            {
                ["T:DECIMAL"] = "999999999999999999999.10",
                ["T:DATE"] = new DateOnly(2025, 2, 3),
                ["T:TIME"] = new TimeOnly(4, 5, 6, 70),
                ["T:GROUP"] = new byte[] { 0, 0 },
                ["T:REAL"] = double.PositiveInfinity
            },
            new Dictionary<string, TpsMemoValue>
            {
                [blob.Name] = new TpsMemoValue(blob, null, new byte[] { 0, 255 })
            });
        var table = new TpsTable(1, "T", fields, [blob], [], [record]);
        var result = RecordQuery.Execute([table], Request());
        var output = new StringWriter();

        StructuredJson.WriteRows("sample.tps", result, "base64", output);

        using var document = JsonDocument.Parse(output.ToString());
        var values = document.RootElement.GetProperty("rows")[0].GetProperty("values");
        Assert.Equal("999999999999999999999.10", values.GetProperty("T:DECIMAL").GetString());
        Assert.Equal("2025-02-03", values.GetProperty("T:DATE").GetString());
        Assert.Equal("04:05:06.07", values.GetProperty("T:TIME").GetString());
        Assert.Equal("0x0000", values.GetProperty("T:GROUP").GetString());
        Assert.Equal("Infinity", values.GetProperty("T:REAL").GetString());
        var blobJson = values.GetProperty("T:DATA");
        Assert.Equal(2, blobJson.GetProperty("length").GetInt32());
        Assert.Equal("AP8=", blobJson.GetProperty("base64").GetString());
        Assert.Equal(64, blobJson.GetProperty("sha256").GetString()!.Length);
    }

    private static QueryRequest Request(params PredicateInput[] predicates) =>
        new(null, [], null, predicates, 0, null, CaseSensitive: false);

    private static QueryRequest Request(bool caseSensitive, params PredicateInput[] predicates) =>
        new(null, [], null, predicates, 0, null, caseSensitive);

    private static TpsField Field(int number, string name, TpsFieldType type, int elementCount = 1) =>
        new(number, name, name[(name.IndexOf(':') + 1)..], "T", type, 0, 1, elementCount, 0, 0);

    private static TpsRecord Record(int number, IReadOnlyDictionary<string, object?> fields) =>
        new(number, fields, new Dictionary<string, TpsMemoValue>());

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
