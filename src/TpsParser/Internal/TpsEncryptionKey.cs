using System.Text;

namespace TpsParser.Internal;

internal sealed class TpsEncryptionKey
{
    private readonly TpsBinaryReader _keyWords;

    public TpsEncryptionKey(string owner)
    {
        var ownerEncoding = CodePagesEncodingProvider.Instance.GetEncoding(1258)
            ?? throw new InvalidOperationException("Code page 1258 is unavailable.");
        var ownerBytes = ownerEncoding.GetBytes(owner);
        var terminatedOwner = new byte[ownerBytes.Length + 1];
        Array.Copy(ownerBytes, terminatedOwner, ownerBytes.Length);

        var block = new byte[64];
        for (var i = 0; i < block.Length; i++)
        {
            var target = (i * 0x11) & 0x3F;
            block[target] = (byte)((i + terminatedOwner[(i + 1) % terminatedOwner.Length]) & 0xFF);
        }

        _keyWords = new TpsBinaryReader(block);
        Shuffle();
        Shuffle();
    }

    public void Decrypt(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException($"Invalid encrypted range: offset={offset}, length={length}, buffer={bytes.Length}.");
        }

        if (offset % 64 != 0 || length % 64 != 0)
        {
            throw new InvalidDataException($"Encrypted ranges must be aligned to 64 bytes: offset={offset}, length={length}.");
        }

        var block = new byte[64];
        for (var i = 0; i < length / block.Length; i++)
        {
            Array.Copy(bytes, offset + i * block.Length, block, 0, block.Length);
            DecryptBlock(block);
            Array.Copy(block, 0, bytes, offset + i * block.Length, block.Length);
        }
    }

    private void Shuffle()
    {
        for (var i = 0; i < 0x10; i++)
        {
            var first = GetWord(i);
            var secondPosition = first & 0x0F;
            var second = GetWord(secondPosition);
            var firstSum = unchecked((int)((((long)first) & 0xFFFFFFFFL) + (long)(first & second) & 0xFFFFFFFFL));
            SetWord(secondPosition, firstSum);
            var secondSum = unchecked((int)((((long)(first | second)) & 0xFFFFFFFFL) + (long)first & 0xFFFFFFFFL));
            SetWord(i, secondSum);
        }
    }

    private void DecryptBlock(byte[] block)
    {
        var data = new TpsBinaryReader(block);
        for (var i = 0x0F; i >= 0; i--)
        {
            var firstPosition = i;
            var key = GetWord(firstPosition);
            var secondPosition = key & 0x0F;
            var first = unchecked((int)((((long)data.Seek(firstPosition * 4).ReadInt32LittleEndian()) & 0xFFFFFFFFL) - (((long)key) & 0xFFFFFFFFL)));
            var second = unchecked((int)((((long)data.Seek(secondPosition * 4).ReadInt32LittleEndian()) & 0xFFFFFFFFL) - (((long)key) & 0xFFFFFFFFL)));
            data.Seek(firstPosition * 4).WriteInt32LittleEndian((first & key) | (second & ~key));
            data.Seek(secondPosition * 4).WriteInt32LittleEndian((second & key) | (first & ~key));
        }
    }

    private int GetWord(int index) => _keyWords.Seek(index * 4).ReadInt32LittleEndian();

    private void SetWord(int index, int value) => _keyWords.Seek(index * 4).WriteInt32LittleEndian(value);
}
