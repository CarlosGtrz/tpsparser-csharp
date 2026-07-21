namespace TpsParser;

public sealed class TpsIndex
{
    internal TpsIndex(int indexNumber, string name, int fieldsInKey)
    {
        IndexNumber = indexNumber;
        Name = name;
        FieldsInKey = fieldsInKey;
    }

    public int IndexNumber { get; }
    public string Name { get; }
    public int FieldsInKey { get; }
}
