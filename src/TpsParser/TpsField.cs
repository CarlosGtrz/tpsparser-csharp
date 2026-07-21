namespace TpsParser;

public sealed class TpsField
{
    internal TpsField(
        int fieldNumber,
        string name,
        string shortName,
        string tablePrefix,
        TpsFieldType type,
        int offset,
        int length,
        int elementCount,
        int decimalDigits,
        int decimalStorageLength)
    {
        FieldNumber = fieldNumber;
        Name = name;
        ShortName = shortName;
        TablePrefix = tablePrefix;
        Type = type;
        Offset = offset;
        Length = length;
        ElementCount = elementCount;
        DecimalDigits = decimalDigits;
        DecimalStorageLength = decimalStorageLength;
    }

    public int FieldNumber { get; }
    public string Name { get; }
    public string ShortName { get; }
    public string TablePrefix { get; }
    public TpsFieldType Type { get; }
    public int Offset { get; }
    public int Length { get; }
    public int ElementCount { get; }
    public int DecimalDigits { get; }
    public int DecimalStorageLength { get; }
    public bool IsArray => ElementCount > 1;
    public bool IsGroup => Type == TpsFieldType.Group;
}
