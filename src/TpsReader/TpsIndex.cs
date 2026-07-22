namespace TpsReader;

/// <summary>Describes an index declared by a TPS table.</summary>
public sealed class TpsIndex
{
    internal TpsIndex(int indexNumber, string name, int fieldsInKey)
    {
        IndexNumber = indexNumber;
        Name = name;
        FieldsInKey = fieldsInKey;
    }

    /// <summary>Gets the one-based index ordinal in the table schema.</summary>
    public int IndexNumber { get; }

    /// <summary>Gets the schema name of the index.</summary>
    public string Name { get; }

    /// <summary>Gets the number of fields in the index key.</summary>
    public int FieldsInKey { get; }
}
