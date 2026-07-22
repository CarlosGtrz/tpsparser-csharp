namespace TpsReader.Tests;

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
        Assert.Equal("William", first.GetString("FIRSTNAME"));
        Assert.Same(table, file.GetTable());
        Assert.Same(table, file.GetTable("cus"));
        Assert.Same(table, file.GetTable(14));
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
        Assert.Equal(660.20m, first.Get<decimal>("INVOICEAMOUNT"));
        Assert.Equal(new DateOnly(1997, 12, 2), first.GetDate("ORDERDATE"));
    }

    [Fact]
    public void Table_selection_reports_missing_and_ambiguous_choices()
    {
        var first = new TpsTable(1, "DUPLICATE", [], [], [], []);
        var second = new TpsTable(2, "duplicate", [], [], [], []);
        var multiple = new TpsFile([first, second]);
        var empty = new TpsFile([]);

        Assert.Throws<TpsParseException>(() => multiple.GetTable());
        Assert.Throws<TpsParseException>(() => empty.GetTable());
        Assert.Throws<TpsParseException>(() => multiple.GetTable("duplicate"));
        Assert.Throws<TpsParseException>(() => multiple.GetTable("missing"));
        Assert.Throws<TpsParseException>(() => multiple.GetTable(99));
        Assert.Throws<ArgumentException>(() => multiple.GetTable(" "));
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

    [Fact]
    public void MemoryStream_opens_with_expected_metadata_and_remains_open()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(Fixture("CUSTOMER.TPS")));

        var file = TpsFile.Open(stream);

        var table = Assert.Single(file.Tables);
        Assert.Equal(14, table.TableNumber);
        Assert.Equal(7, table.Records.Count);
        Assert.Equal(stream.Length, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Encrypted_stream_opens_with_owner()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(Fixture("encrypted-a.tps")));

        var file = TpsFile.Open(stream, new TpsOpenOptions { Owner = "a" });

        var table = Assert.Single(file.Tables);
        Assert.Equal(2, table.TableNumber);
        Assert.Equal(17, table.Records.Count);
    }

    [Fact]
    public void TryOpen_stream_returns_error_without_source_path_and_leaves_stream_open()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(Fixture("encrypted-a.tps")));

        var ok = TpsFile.TryOpen(stream, out var file, out var error);

        Assert.False(ok);
        Assert.Null(file);
        Assert.NotNull(error);
        Assert.Contains("TPS stream", error.Message);
        Assert.Null(error.SourcePath);
        Assert.Equal(stream.Length, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Malformed_stream_returns_parse_error_and_remains_open()
    {
        using var stream = new MemoryStream("not a topspeed file"u8.ToArray());

        var ok = TpsFile.TryOpen(stream, out var file, out var error);

        Assert.False(ok);
        Assert.Null(file);
        Assert.NotNull(error);
        Assert.Contains("TPS stream", error.Message);
        Assert.Null(error.SourcePath);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Non_seekable_readable_stream_is_supported()
    {
        using var stream = new NonSeekableReadStream(File.ReadAllBytes(Fixture("CUSTOMER.TPS")));

        var file = TpsFile.Open(stream);

        Assert.Single(file.Tables);
        Assert.Equal(7, file.Tables[0].Records.Count);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Stream_parsing_starts_at_current_position()
    {
        var contents = File.ReadAllBytes(Fixture("CUSTOMER.TPS"));
        var prefix = new byte[17];
        var prefixedContents = new byte[prefix.Length + contents.Length];
        prefix.CopyTo(prefixedContents, 0);
        contents.CopyTo(prefixedContents, prefix.Length);
        using var stream = new MemoryStream(prefixedContents) { Position = prefix.Length };

        var file = TpsFile.Open(stream);

        Assert.Single(file.Tables);
        Assert.Equal(7, file.Tables[0].Records.Count);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Stream_must_be_non_null_and_readable()
    {
        Assert.Throws<ArgumentNullException>(() => TpsFile.Open((Stream)null!));

        var stream = new MemoryStream();
        stream.Dispose();

        var error = Assert.Throws<ArgumentException>(() => TpsFile.Open(stream));
        Assert.Equal("stream", error.ParamName);
    }

    [Fact]
    public void Byte_array_opens_with_expected_metadata_and_records()
    {
        var data = File.ReadAllBytes(Fixture("CUSTOMER.TPS"));

        var file = TpsFile.Open(data);

        var table = Assert.Single(file.Tables);
        Assert.Equal(14, table.TableNumber);
        Assert.Equal(7, table.Records.Count);
        Assert.Equal(1, table.GetRecord(16).GetInt32("CUSTNUMBER"));
    }

    [Fact]
    public void Encrypted_byte_array_opens_with_owner_without_mutating_input()
    {
        var data = File.ReadAllBytes(Fixture("encrypted-a.tps"));
        var originalData = data.ToArray();

        var file = TpsFile.Open(data, new TpsOpenOptions { Owner = "a" });

        var table = Assert.Single(file.Tables);
        Assert.Equal(2, table.TableNumber);
        Assert.Equal(17, table.Records.Count);
        Assert.Equal(originalData, data);
    }

    [Fact]
    public void TryOpen_encrypted_byte_array_without_owner_returns_error_without_source_path()
    {
        var data = File.ReadAllBytes(Fixture("encrypted-a.tps"));

        var ok = TpsFile.TryOpen(data, out var file, out var error);

        Assert.False(ok);
        Assert.Null(file);
        Assert.NotNull(error);
        Assert.Contains("TPS byte array", error.Message);
        Assert.Null(error.SourcePath);
    }

    [Fact]
    public void TryOpen_malformed_byte_array_returns_error_without_source_path()
    {
        var data = "not a topspeed file"u8.ToArray();

        var ok = TpsFile.TryOpen(data, out var file, out var error);

        Assert.False(ok);
        Assert.Null(file);
        Assert.NotNull(error);
        Assert.Contains("TPS byte array", error.Message);
        Assert.Null(error.SourcePath);
    }

    [Fact]
    public void Byte_array_must_be_non_null()
    {
        Assert.Throws<ArgumentNullException>(() => TpsFile.Open((byte[])null!));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
