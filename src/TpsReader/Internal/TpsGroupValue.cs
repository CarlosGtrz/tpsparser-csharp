namespace TpsReader.Internal;

internal sealed class TpsGroupValue
{
    private readonly byte[] _rawBytes;

    public TpsGroupValue(string text, byte[] rawBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rawBytes);
        Text = text;
        _rawBytes = rawBytes.ToArray();
    }

    public string Text { get; }

    public byte[] CopyRawBytes() => _rawBytes.ToArray();
}
