namespace TpsParser;

public sealed class TpsTable
{
    private readonly Dictionary<int, TpsRecord> _recordsByNumber;

    internal TpsTable(
        int tableNumber,
        string name,
        IReadOnlyList<TpsField> fields,
        IReadOnlyList<TpsMemo> memos,
        IReadOnlyList<TpsIndex> indexes,
        IReadOnlyList<TpsRecord> records)
    {
        TableNumber = tableNumber;
        Name = name;
        Fields = fields;
        Memos = memos;
        Indexes = indexes;
        Records = records;
        _recordsByNumber = records.ToDictionary(r => r.RecordNumber);
    }

    public int TableNumber { get; }
    public string Name { get; }
    public IReadOnlyList<TpsField> Fields { get; }
    public IReadOnlyList<TpsMemo> Memos { get; }
    public IReadOnlyList<TpsIndex> Indexes { get; }
    public IReadOnlyList<TpsRecord> Records { get; }

    public TpsRecord GetRecord(int recordNumber)
    {
        if (_recordsByNumber.TryGetValue(recordNumber, out var record))
        {
            return record;
        }

        throw new TpsParseException(new TpsParseError($"Record {recordNumber} was not found in table {TableNumber}."));
    }
}
