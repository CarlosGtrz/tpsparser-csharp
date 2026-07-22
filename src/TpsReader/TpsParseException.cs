namespace TpsReader;

/// <summary>Represents an error opening, navigating, or decoding a TPS input.</summary>
public sealed class TpsParseException : Exception
{
    /// <summary>Initializes an exception with a generic TPS parsing error.</summary>
    public TpsParseException()
        : this(new TpsParseError("TPS parsing failed."))
    {
    }

    /// <summary>Initializes an exception with the specified message.</summary>
    public TpsParseException(string message)
        : this(new TpsParseError(message))
    {
    }

    /// <summary>Initializes an exception with the specified message and underlying exception.</summary>
    public TpsParseException(string message, Exception innerException)
        : this(new TpsParseError(message, Exception: innerException))
    {
    }

    /// <summary>Initializes an exception from a structured parse error.</summary>
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

    /// <summary>Gets the structured error associated with this exception.</summary>
    public TpsParseError Error { get; }

    private static TpsParseError EnsureNotNull(TpsParseError? error) =>
        error ?? throw new ArgumentNullException(nameof(error));
}
