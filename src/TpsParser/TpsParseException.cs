namespace TpsParser;

public sealed class TpsParseException : Exception
{
    public TpsParseException()
        : this(new TpsParseError("TPS parsing failed."))
    {
    }

    public TpsParseException(string message)
        : this(new TpsParseError(message))
    {
    }

    public TpsParseException(string message, Exception innerException)
        : this(new TpsParseError(message, Exception: innerException))
    {
    }

    public TpsParseException(TpsParseError error)
        : this(EnsureNotNull(error), initialize: true)
    {
    }

    private TpsParseException(TpsParseError error, bool initialize)
        : base(error.Message, error.Exception)
    {
        _ = initialize;
        Error = error;
    }

    public TpsParseError Error { get; }

    private static TpsParseError EnsureNotNull(TpsParseError? error) =>
        error ?? throw new ArgumentNullException(nameof(error));
}
