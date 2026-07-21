using TpsInspector;

namespace TpsParser.Tests;

public sealed class InspectorApplicationTests
{
    [Fact]
    public void Help_and_usage_errors_are_in_english()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, InspectorApplication.Run(["--help"], output, error));
        Assert.Contains("Usage:", output.ToString());
        Assert.Contains("Search subdirectories", output.ToString());

        output.GetStringBuilder().Clear();
        Assert.Equal(1, InspectorApplication.Run(["--unknown"], output, error));
        Assert.Contains("Unknown option", error.ToString());
    }

    [Fact]
    public void Directory_scan_accepts_uppercase_tps_extensions_and_prints_english_details()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Fixture("CUSTOMER.TPS"), Path.Combine(directory, "SAMPLE.TPS"));
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = InspectorApplication.Run([directory, "--details"], output, error);

            Assert.Equal(0, exitCode);
            Assert.Empty(error.ToString());
            Assert.Contains("tables=1", output.ToString());
            Assert.Contains("Table #14", output.ToString());
            Assert.Contains("indexes=3", output.ToString());
            Assert.Contains("Sample value", output.ToString());
            Assert.Contains("William", output.ToString());
            Assert.Contains("Summary:", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Mixed_directory_returns_exit_code_two_when_a_file_fails()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Fixture("CUSTOMER.TPS"), Path.Combine(directory, "GOOD.tps"));
            File.WriteAllText(Path.Combine(directory, "BAD.tps"), "bad");
            var output = new StringWriter();

            var exitCode = InspectorApplication.Run([directory], output, new StringWriter());

            Assert.Equal(2, exitCode);
            Assert.Contains("ERROR", output.ToString());
            Assert.Contains("opened=1", output.ToString());
            Assert.Contains("errors=1", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Recursive_scan_finds_nested_files_only_when_requested()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            File.Copy(Fixture("CUSTOMER.TPS"), Path.Combine(nested, "CUSTOMER.TPS"));

            Assert.Equal(1, InspectorApplication.Run([directory], new StringWriter(), new StringWriter()));
            Assert.Equal(0, InspectorApplication.Run([directory, "--recursive"], new StringWriter(), new StringWriter()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Repeated_owner_options_are_tried_in_order()
    {
        var output = new StringWriter();

        var exitCode = InspectorApplication.Run(
            [Fixture("encrypted-a.tps"), "--owner", "wrong", "--owner", "a"],
            output,
            new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("opened=1", output.ToString());
    }

    [Fact]
    public void Sample_values_are_truncated_after_fifty_characters()
    {
        var fiftyCharacters = new string('a', 50);

        Assert.Equal(fiftyCharacters, InspectorApplication.FormatSampleValue(fiftyCharacters));
        Assert.Equal($"{fiftyCharacters}...", InspectorApplication.FormatSampleValue($"{fiftyCharacters}b"));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
