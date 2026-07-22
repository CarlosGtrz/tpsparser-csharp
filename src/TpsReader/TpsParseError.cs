namespace TpsReader;

/// <summary>Describes a TPS open or parsing failure without requiring an exception to be thrown.</summary>
public sealed record TpsParseError(string Message, string? SourcePath = null, Exception? Exception = null);
