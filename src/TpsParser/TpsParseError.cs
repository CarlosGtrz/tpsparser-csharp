namespace TpsParser;

public sealed record TpsParseError(string Message, string? SourcePath = null, Exception? Exception = null);
