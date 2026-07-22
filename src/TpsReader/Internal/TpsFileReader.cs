using System.Text;

namespace TpsReader.Internal;

internal sealed class TpsFileReader
{
    private readonly TpsBinaryReader _reader;
    private readonly Encoding _textEncoding;

    public TpsFileReader(byte[] data, string owner, Encoding textEncoding, bool ignoreErrors)
    {
        var key = new TpsEncryptionKey(owner);
        if (data.Length < 0x200)
        {
            throw new InvalidDataException("Encrypted TPS file is smaller than its header.");
        }

        key.Decrypt(data, 0, 0x200);
        _reader = new TpsBinaryReader(data);
        _textEncoding = textEncoding;
        var header = GetHeader();
        for (var i = 0; i < header.PageStarts.Count; i++)
        {
            var start = header.PageStarts[i];
            var end = header.PageEnds[i];
            if (IsEmptyBlock(start, end) || start >= _reader.Length)
            {
                continue;
            }

            try
            {
                ValidateBlockRange(start, end);
                key.Decrypt(data, start, end - start);
            }
            catch (InvalidDataException) when (ignoreErrors)
            {
            }
        }
    }

    public TpsFileReader(byte[] data, Encoding textEncoding)
    {
        _reader = new TpsBinaryReader(data);
        _textEncoding = textEncoding;
    }

    public TpsHeader GetHeader()
    {
        _reader.Seek(0);
        return new TpsHeader(_reader);
    }

    public ParsedTpsFile Parse(bool ignoreErrors)
    {
        var (definitions, names) = ReadMetadata(ignoreErrors);
        var (dataRecords, memoRecords) = ReadContent(definitions, ignoreErrors);
        return new ParsedTpsFile(definitions, names, dataRecords, memoRecords);
    }

    private (SortedDictionary<int, TableDefinitionRecord> Definitions, Dictionary<int, string> Names) ReadMetadata(bool ignoreErrors)
    {
        var fragments = new SortedDictionary<int, List<RawTpsRecord?>>();
        var tableNames = new Dictionary<int, string>();

        foreach (var page in ReadRecordPages(ignoreErrors))
        {
            var pageDefinitions = new List<(TableDefinitionHeader Header, RawTpsRecord Record)>();
            var pageNames = new List<TableNameRecord>();
            try
            {
                foreach (var record in page)
                {
                    switch (record.Header)
                    {
                        case TableDefinitionHeader header:
                            pageDefinitions.Add((header, record));
                            break;
                        case TableNameHeader:
                            pageNames.Add(new TableNameRecord(record));
                            break;
                    }
                }
            }
            catch (InvalidDataException) when (ignoreErrors)
            {
                continue;
            }

            foreach (var item in pageDefinitions)
            {
                AddFragment(fragments, item.Header.TableNumber, item.Header.BlockNumber, item.Record);
            }

            foreach (var tableName in pageNames)
            {
                tableNames.TryAdd(tableName.TableNumber, tableName.Name);
            }
        }

        var definitions = new SortedDictionary<int, TableDefinitionRecord>();
        foreach (var table in fragments)
        {
            if (!IsComplete(table.Value))
            {
                continue;
            }

            try
            {
                definitions[table.Key] = new TableDefinitionRecord(Merge(table.Value), _textEncoding);
            }
            catch (InvalidDataException) when (ignoreErrors)
            {
            }
        }

        return (definitions, tableNames);
    }

    private (
        Dictionary<int, IReadOnlyList<DataRecord>> DataRecords,
        Dictionary<(int TableNumber, int MemoIndex), IReadOnlyDictionary<int, MemoRecord>> MemoRecords)
        ReadContent(IReadOnlyDictionary<int, TableDefinitionRecord> definitions, bool ignoreErrors)
    {
        var dataByTable = definitions.Keys.ToDictionary(key => key, _ => new List<DataRecord>());
        var memoFragments = new Dictionary<(int TableNumber, int MemoIndex, int OwnerRecord), List<RawTpsRecord?>>();

        foreach (var page in ReadRecordPages(ignoreErrors))
        {
            var pageData = new List<(int TableNumber, DataRecord Record)>();
            var pageMemos = new List<(MemoHeader Header, RawTpsRecord Record)>();
            try
            {
                foreach (var record in page)
                {
                    switch (record.Header)
                    {
                        case DataHeader dataHeader when definitions.TryGetValue(dataHeader.TableNumber, out var definition):
                            pageData.Add((dataHeader.TableNumber, new DataRecord(record, definition)));
                            break;
                        case MemoHeader memoHeader:
                            pageMemos.Add((memoHeader, record));
                            break;
                    }
                }
            }
            catch (InvalidDataException) when (ignoreErrors)
            {
                continue;
            }

            foreach (var item in pageData)
            {
                dataByTable[item.TableNumber].Add(item.Record);
            }

            foreach (var item in pageMemos)
            {
                var key = (item.Header.TableNumber, item.Header.MemoIndex, item.Header.OwnerRecordNumber);
                if (!memoFragments.TryGetValue(key, out var sequence))
                {
                    memoFragments[key] = sequence = [];
                }

                while (sequence.Count <= item.Header.SequenceNumber)
                {
                    sequence.Add(null);
                }

                sequence[item.Header.SequenceNumber] = item.Record;
            }
        }

        var memosByDefinition = new Dictionary<(int, int), Dictionary<int, MemoRecord>>();
        foreach (var group in memoFragments)
        {
            if (!IsComplete(group.Value))
            {
                continue;
            }

            var firstHeader = (MemoHeader)group.Value[0]!.Header!;
            var memo = new MemoRecord(firstHeader, Merge(group.Value));
            var definitionKey = (group.Key.TableNumber, group.Key.MemoIndex);
            if (!memosByDefinition.TryGetValue(definitionKey, out var records))
            {
                memosByDefinition[definitionKey] = records = [];
            }

            records[memo.OwnerRecordNumber] = memo;
        }

        return (
            dataByTable.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DataRecord>)pair.Value),
            memosByDefinition.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<int, MemoRecord>)pair.Value));
    }

    private IEnumerable<IReadOnlyList<RawTpsRecord>> ReadRecordPages(bool ignoreErrors)
    {
        foreach (var block in ReadBlocks(ignoreErrors))
        {
            foreach (var page in block.Pages)
            {
                if (TryReadPage(page, _textEncoding, ignoreErrors, out var records))
                {
                    yield return records;
                }
            }
        }
    }

    internal static bool TryReadPage(
        TpsPage page,
        Encoding textEncoding,
        bool ignoreErrors,
        out IReadOnlyList<RawTpsRecord> records)
    {
        try
        {
            records = page.ReadRecords(textEncoding);
            return true;
        }
        catch (InvalidDataException) when (ignoreErrors)
        {
            records = [];
            return false;
        }
    }

    private IEnumerable<TpsBlock> ReadBlocks(bool ignoreErrors)
    {
        var header = GetHeader();
        for (var i = 0; i < header.PageStarts.Count; i++)
        {
            var start = header.PageStarts[i];
            var end = header.PageEnds[i];
            if (IsEmptyBlock(start, end) || start >= _reader.Length)
            {
                continue;
            }

            TpsBlock block;
            try
            {
                ValidateBlockRange(start, end);
                block = new TpsBlock(_reader, start, end, ignoreErrors);
            }
            catch (InvalidDataException) when (ignoreErrors)
            {
                continue;
            }

            yield return block;
        }
    }

    private void ValidateBlockRange(int start, int end)
    {
        if (start < 0x200 || end < start || end > _reader.Length)
        {
            throw new InvalidDataException($"Invalid TPS block range: start={start}, end={end}, file={_reader.Length}.");
        }
    }

    private static void AddFragment(
        SortedDictionary<int, List<RawTpsRecord?>> fragments,
        int group,
        int sequenceNumber,
        RawTpsRecord record)
    {
        if (!fragments.TryGetValue(group, out var sequence))
        {
            fragments[group] = sequence = [];
        }

        while (sequence.Count <= sequenceNumber)
        {
            sequence.Add(null);
        }

        sequence[sequenceNumber] = record;
    }

    private static bool IsComplete(IEnumerable<RawTpsRecord?> records) => records.All(record => record is not null);

    private static TpsBinaryReader Merge(IEnumerable<RawTpsRecord?> records)
    {
        using var output = new MemoryStream();
        foreach (var record in records)
        {
            output.Write(record!.Data.RemainingBytes());
        }

        return new TpsBinaryReader(output.ToArray());
    }

    private static bool IsEmptyBlock(int start, int end) => start == 0x200 && end == 0x200;

    internal static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return ReadAllBytes(stream);
    }

    internal static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            var remainingLength = Math.Max(0, stream.Length - stream.Position);
            if (remainingLength > int.MaxValue)
            {
                throw InputTooLarge();
            }

            var data = new byte[(int)remainingLength];
            stream.ReadExactly(data);
            return data;
        }

        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                return output.ToArray();
            }

            if (output.Length > int.MaxValue - bytesRead)
            {
                throw InputTooLarge();
            }

            output.Write(buffer, 0, bytesRead);
        }
    }

    private static NotSupportedException InputTooLarge() =>
        new($"TPS inputs larger than {int.MaxValue} bytes are not supported.");
}

internal sealed record ParsedTpsFile(
    IReadOnlyDictionary<int, TableDefinitionRecord> TableDefinitions,
    IReadOnlyDictionary<int, string> TableNames,
    IReadOnlyDictionary<int, IReadOnlyList<DataRecord>> DataRecords,
    IReadOnlyDictionary<(int TableNumber, int MemoIndex), IReadOnlyDictionary<int, MemoRecord>> MemoRecords);
