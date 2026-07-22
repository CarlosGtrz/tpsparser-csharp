namespace TpsReader;

/// <summary>Describes a MEMO or BLOB declared by a TPS table.</summary>
public sealed class TpsMemo
{
    internal TpsMemo(int memoNumber, string name, string shortName, int flags, bool isBlob)
    {
        MemoNumber = memoNumber;
        Name = name;
        ShortName = shortName;
        Flags = flags;
        Type = isBlob ? TpsFieldType.Blob : TpsFieldType.Memo;
    }

    /// <summary>Gets the one-based MEMO/BLOB ordinal in the table schema.</summary>
    public int MemoNumber { get; }

    /// <summary>Gets the full schema name.</summary>
    public string Name { get; }

    /// <summary>Gets the name without its table prefix.</summary>
    public string ShortName { get; }

    /// <summary>Gets the raw TPS schema flags.</summary>
    public int Flags { get; }

    /// <summary>Gets whether this definition represents a MEMO or BLOB.</summary>
    public TpsFieldType Type { get; }

    /// <summary>Gets whether this definition contains text MEMO data.</summary>
    public bool IsMemo => Type == TpsFieldType.Memo;

    /// <summary>Gets whether this definition contains binary BLOB data.</summary>
    public bool IsBlob => Type == TpsFieldType.Blob;
}
