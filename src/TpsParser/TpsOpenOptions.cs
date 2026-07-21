using System.Text;

namespace TpsParser;

public sealed class TpsOpenOptions
{
    public string? Owner { get; init; }
    public bool IgnoreErrors { get; init; }
    public Encoding StringEncoding { get; init; } = Encoding.Latin1;
}
