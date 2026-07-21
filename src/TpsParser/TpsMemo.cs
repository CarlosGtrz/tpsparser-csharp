namespace TpsParser;

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

    public int MemoNumber { get; }
    public string Name { get; }
    public string ShortName { get; }
    public int Flags { get; }
    public TpsFieldType Type { get; }
    public bool IsMemo => Type == TpsFieldType.Memo;
    public bool IsBlob => Type == TpsFieldType.Blob;
}
