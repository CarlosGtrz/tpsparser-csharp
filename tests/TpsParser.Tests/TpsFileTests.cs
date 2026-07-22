namespace TpsParser.Tests;

public sealed class TpsFileTests
{
    [Fact]
    public void CustomerFile_opens_with_expected_metadata_and_records()
    {
        var file = TpsFile.Open(Fixture("CUSTOMER.TPS"));

        var table = Assert.Single(file.Tables);
        Assert.Equal(14, table.TableNumber);
        Assert.Equal("CUS", table.Name);
        Assert.Equal(8, table.Fields.Count);
        Assert.Equal(3, table.Indexes.Count);
        Assert.Empty(table.Memos);
        Assert.Equal(7, table.Records.Count);

        var first = table.GetRecord(16);
        Assert.Equal(1, first.GetInt32("CUS:CUSTNUMBER"));
        Assert.Equal(1, first.GetInt32("CUSTNUMBER"));
        Assert.Equal("William".PadRight(20), first.GetString("FIRSTNAME"));
    }

    [Fact]
    public void OrdersFile_preserves_bcd_decimal_as_string()
    {
        var file = TpsFile.Open(Fixture("ORDERS.TPS"));

        var table = Assert.Single(file.Tables);
        Assert.Equal(1, table.TableNumber);
        Assert.Equal(5, table.Fields.Count);
        Assert.Equal(2, table.Records.Count);
        Assert.Equal(TpsFieldType.Decimal, table.Fields.Single(f => f.ShortName == "INVOICEAMOUNT").Type);

        var first = table.GetRecord(2);
        Assert.Equal("660.20", first.GetDecimalString("INVOICEAMOUNT"));
        Assert.True(first.TryGetDecimal("INVOICEAMOUNT", out var amount));
        Assert.Equal(660.20m, amount);
        Assert.Equal(new DateOnly(1997, 12, 2), first.GetDate("ORDERDATE"));
    }

    [Fact]
    public void EncryptedFile_requires_owner_and_opens_with_owner()
    {
        Assert.Throws<TpsParseException>(() => TpsFile.Open(Fixture("encrypted-a.tps")));

        var file = TpsFile.Open(Fixture("encrypted-a.tps"), new TpsOpenOptions { Owner = "a" });
        var table = Assert.Single(file.Tables);
        Assert.Equal(2, table.TableNumber);
        Assert.Equal(4, table.Fields.Count);
        Assert.Equal(17, table.Records.Count);
    }

    [Fact]
    public void TryOpen_returns_error_instead_of_throwing()
    {
        var ok = TpsFile.TryOpen(Fixture("encrypted-a.tps"), out var file, out var error);

        Assert.False(ok);
        Assert.Null(file);
        Assert.NotNull(error);
        Assert.Contains("Could not open or parse TPS file", error.Message);
    }

    [Fact]
    public void Field_and_navigation_errors_are_exposed_as_tps_parse_exceptions()
    {
        var file = TpsFile.Open(Fixture("CUSTOMER.TPS"));
        var table = file.GetTable(14);
        var record = table.GetRecord(16);

        Assert.Throws<TpsParseException>(() => file.GetTable(999));
        Assert.Throws<TpsParseException>(() => table.GetRecord(999));
        Assert.Throws<TpsParseException>(() => record.GetValue("DOES_NOT_EXIST"));
        Assert.Throws<TpsParseException>(() => record.GetInt32("FIRSTNAME"));
    }

    [Fact]
    public void MalformedFile_throws_tps_parse_exception()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tps");
        try
        {
            File.WriteAllText(path, "not a topspeed file");
            Assert.Throws<TpsParseException>(() => TpsFile.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void File_can_be_read_while_another_process_has_it_open_for_writing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tps");
        File.Copy(Fixture("CUSTOMER.TPS"), path);

        try
        {
            using var writer = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            var file = TpsFile.Open(path);

            Assert.Single(file.Tables);
            Assert.Equal(7, file.Tables[0].Records.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
