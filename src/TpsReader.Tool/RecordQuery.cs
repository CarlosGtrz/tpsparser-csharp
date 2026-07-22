using System.Globalization;
using System.Numerics;
using TpsReader;

namespace TpsReader.Tool;

internal sealed record PredicateInput(string FieldName, string Operator, string? Literal);

internal sealed record QueryRequest(
    string? Table,
    IReadOnlyList<string> Fields,
    int? RecordNumber,
    IReadOnlyList<PredicateInput> Predicates,
    int Skip,
    int? Limit,
    bool CaseSensitive);

internal sealed record QueryResult(
    TpsTable Table,
    IReadOnlyList<SelectedColumn> Columns,
    IReadOnlyList<TpsRecord> Records,
    int MatchedCount,
    int Skip,
    int? Limit)
{
    public bool HasMore => Skip + Records.Count < MatchedCount;
}

internal sealed class SelectedColumn
{
    private SelectedColumn(string name, TpsField? field, TpsMemo? memo)
    {
        Name = name;
        Field = field;
        Memo = memo;
    }

    public string Name { get; }
    public TpsField? Field { get; }
    public TpsMemo? Memo { get; }
    public bool IsField => Field is not null;

    public static SelectedColumn ForField(TpsField field) => new(field.Name, field, null);
    public static SelectedColumn ForMemo(TpsMemo memo) => new(memo.Name, null, memo);

    public object? GetValue(TpsRecord record)
    {
        if (Field is not null)
        {
            return record.GetValue(Field.Name);
        }

        return Memo!.IsMemo
            ? record.GetMemo(Memo.Name)
            : record.GetBlob(Memo.Name);
    }
}

internal static class RecordQuery
{
    public static TpsTable ResolveTable(IReadOnlyList<TpsTable> tables, string? selector)
    {
        if (selector is null)
        {
            if (tables.Count == 1)
            {
                return tables[0];
            }

            throw new CliValidationException(
                $"The TPS file contains {tables.Count} tables; specify --table. Available tables: {FormatTables(tables)}.");
        }

        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tableNumber))
        {
            var numbered = tables.Where(table => table.TableNumber == tableNumber).ToArray();
            if (numbered.Length == 1)
            {
                return numbered[0];
            }
        }

        var named = tables
            .Where(table => string.Equals(table.Name, selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (named.Length == 1)
        {
            return named[0];
        }

        if (named.Length > 1)
        {
            throw new CliValidationException($"Table name '{selector}' is ambiguous; use its table number.");
        }

        throw new CliValidationException(
            $"Table '{selector}' was not found. Available tables: {FormatTables(tables)}.");
    }

    public static QueryResult Execute(IReadOnlyList<TpsTable> tables, QueryRequest request)
    {
        var table = ResolveTable(tables, request.Table);
        var columns = ResolveColumns(table, request.Fields);
        var predicates = request.Predicates
            .Select(predicate => ResolvedPredicate.Create(table, predicate, request.CaseSensitive))
            .ToArray();

        var matching = table.Records
            .Where(record => request.RecordNumber is null || record.RecordNumber == request.RecordNumber)
            .Where(record => predicates.All(predicate => predicate.Matches(record)))
            .ToArray();
        IEnumerable<TpsRecord> page = matching.Skip(request.Skip);
        if (request.Limit is not null)
        {
            page = page.Take(request.Limit.Value);
        }

        return new QueryResult(
            table,
            columns,
            page.ToArray(),
            matching.Length,
            request.Skip,
            request.Limit);
    }

    public static IReadOnlyList<SelectedColumn> ResolveColumns(TpsTable table, IReadOnlyList<string> requestedFields)
    {
        if (requestedFields.Count == 0)
        {
            return table.Fields.Select(SelectedColumn.ForField)
                .Concat(table.Memos.Select(SelectedColumn.ForMemo))
                .ToArray();
        }

        var columns = new List<SelectedColumn>(requestedFields.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedField in requestedFields)
        {
            var column = ResolveColumn(table, requestedField);
            if (!used.Add(column.Name))
            {
                throw new CliValidationException($"Field/MEMO/BLOB '{column.Name}' was selected more than once.");
            }

            columns.Add(column);
        }

        return columns;
    }

    private static SelectedColumn ResolveColumn(TpsTable table, string name)
    {
        var fullFields = table.Fields
            .Where(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(SelectedColumn.ForField);
        var fullMemos = table.Memos
            .Where(memo => string.Equals(memo.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(SelectedColumn.ForMemo);
        var fullMatches = fullFields.Concat(fullMemos).ToArray();
        if (fullMatches.Length == 1)
        {
            return fullMatches[0];
        }

        if (fullMatches.Length > 1)
        {
            throw new CliValidationException(
                $"Field/MEMO/BLOB name '{name}' is ambiguous; use a unique schema name.");
        }

        var shortFields = table.Fields
            .Where(field => string.Equals(field.ShortName, name, StringComparison.OrdinalIgnoreCase))
            .Select(SelectedColumn.ForField);
        var shortMemos = table.Memos
            .Where(memo => string.Equals(memo.ShortName, name, StringComparison.OrdinalIgnoreCase))
            .Select(SelectedColumn.ForMemo);
        var shortMatches = shortFields.Concat(shortMemos).ToArray();
        return shortMatches.Length switch
        {
            1 => shortMatches[0],
            > 1 => throw new CliValidationException(
                $"Field/MEMO/BLOB name '{name}' is ambiguous; use its full table-qualified name."),
            _ => throw new CliValidationException($"Field/MEMO/BLOB '{name}' was not found in table {table.Name}.")
        };
    }

    private static string FormatTables(IEnumerable<TpsTable> tables) => string.Join(
        ", ",
        tables.Select(table => $"{table.TableNumber}:{table.Name}"));

    private sealed class ResolvedPredicate
    {
        private readonly Func<TpsRecord, object?> _getValue;
        private readonly TpsFieldType _type;
        private readonly PredicateOperator _operator;
        private readonly object? _literal;
        private readonly StringComparison _textComparison;
        private readonly bool _trimTrailingPadding;

        private ResolvedPredicate(
            Func<TpsRecord, object?> getValue,
            TpsFieldType type,
            PredicateOperator @operator,
            object? literal,
            bool caseSensitive,
            bool trimTrailingPadding)
        {
            _getValue = getValue;
            _type = type;
            _operator = @operator;
            _literal = literal;
            _textComparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            _trimTrailingPadding = trimTrailingPadding;
        }

        public static ResolvedPredicate Create(TpsTable table, PredicateInput input, bool caseSensitive)
        {
            var @operator = ParseOperator(input.Operator);
            if (string.Equals(input.FieldName, "@recordNumber", StringComparison.OrdinalIgnoreCase))
            {
                RequireOperator(input.FieldName, @operator, ComparableOperators);
                var literal = ParseLiteral(TpsFieldType.Long, @operator, input.Literal, input.FieldName);
                return new ResolvedPredicate(
                    record => record.RecordNumber,
                    TpsFieldType.Long,
                    @operator,
                    literal,
                    caseSensitive,
                    trimTrailingPadding: false);
            }

            var (baseName, elementIndex) = ParseArrayReference(input.FieldName);
            var column = ResolveColumn(table, baseName);
            if (column.Field is not null)
            {
                var field = column.Field;
                if (field.IsArray && elementIndex is null)
                {
                    throw new CliValidationException(
                        $"Array field '{field.Name}' requires a one-based element suffix such as '{field.Name}[1]' in --where.");
                }

                if (!field.IsArray && elementIndex is not null)
                {
                    throw new CliValidationException($"Field '{field.Name}' is not an array.");
                }

                if (elementIndex is not null && (elementIndex < 0 || elementIndex >= field.ElementCount))
                {
                    throw new CliValidationException(
                        $"Array index {elementIndex + 1} is outside field '{field.Name}' with {field.ElementCount} elements.");
                }

                RequireOperator(field.Name, @operator, OperatorsFor(field.Type));
                var literal = ParseLiteral(field.Type, @operator, input.Literal, field.Name);
                return new ResolvedPredicate(
                    record => elementIndex is null
                        ? record.GetValue(field.Name)
                        : ((object?[])record.GetValue(field.Name)!)[elementIndex.Value],
                    field.Type,
                    @operator,
                    literal,
                    caseSensitive,
                    trimTrailingPadding: field.Type is TpsFieldType.String or TpsFieldType.Group);
            }

            var memo = column.Memo!;
            RequireOperator(memo.Name, @operator, OperatorsFor(memo.Type));
            var memoLiteral = ParseLiteral(memo.Type, @operator, input.Literal, memo.Name);
            return new ResolvedPredicate(
                memo.IsMemo
                    ? record => record.GetMemo(memo.Name)
                    : record => record.GetBlob(memo.Name),
                memo.Type,
                @operator,
                memoLiteral,
                caseSensitive,
                trimTrailingPadding: false);
        }

        public bool Matches(TpsRecord record)
        {
            var actual = _getValue(record);
            if (_operator == PredicateOperator.IsNull)
            {
                return actual is null;
            }

            if (_operator == PredicateOperator.IsNotNull)
            {
                return actual is not null;
            }

            if (actual is null)
            {
                return false;
            }

            if (_type is TpsFieldType.String or TpsFieldType.CString or TpsFieldType.PString or
                TpsFieldType.Group or TpsFieldType.Memo)
            {
                var text = (string)actual;
                if (_trimTrailingPadding)
                {
                    text = text.TrimEnd('\0', ' ');
                }

                return MatchText(text, (string)_literal!);
            }

            var comparison = Compare(actual, _literal!);
            return _operator switch
            {
                PredicateOperator.Equal => comparison == 0,
                PredicateOperator.NotEqual => comparison != 0,
                PredicateOperator.LessThan => comparison < 0,
                PredicateOperator.LessThanOrEqual => comparison <= 0,
                PredicateOperator.GreaterThan => comparison > 0,
                PredicateOperator.GreaterThanOrEqual => comparison >= 0,
                _ => false
            };
        }

        private bool MatchText(string actual, string expected) => _operator switch
        {
            PredicateOperator.Equal => string.Equals(actual, expected, _textComparison),
            PredicateOperator.NotEqual => !string.Equals(actual, expected, _textComparison),
            PredicateOperator.Contains => actual.Contains(expected, _textComparison),
            PredicateOperator.StartsWith => actual.StartsWith(expected, _textComparison),
            PredicateOperator.EndsWith => actual.EndsWith(expected, _textComparison),
            _ => false
        };

        private int Compare(object actual, object expected) => _type switch
        {
            TpsFieldType.Byte or TpsFieldType.Short or TpsFieldType.UShort or
                TpsFieldType.Long or TpsFieldType.ULong => ToBigInteger(actual).CompareTo((BigInteger)expected),
            TpsFieldType.SReal => ((float)actual).CompareTo((float)expected),
            TpsFieldType.Real => ((double)actual).CompareTo((double)expected),
            TpsFieldType.Decimal => BigDecimalValue.Parse((string)actual).CompareTo((BigDecimalValue)expected),
            TpsFieldType.Date => ((DateOnly)actual).CompareTo((DateOnly)expected),
            TpsFieldType.Time => ((TimeOnly)actual).CompareTo((TimeOnly)expected),
            _ => throw new InvalidOperationException($"Cannot compare TPS type {_type}.")
        };

        private static object? ParseLiteral(
            TpsFieldType type,
            PredicateOperator @operator,
            string? literal,
            string fieldName)
        {
            if (@operator is PredicateOperator.IsNull or PredicateOperator.IsNotNull)
            {
                if (literal is not null)
                {
                    throw new CliValidationException($"Operator '{FormatOperator(@operator)}' does not accept a value.");
                }

                return null;
            }

            if (literal is null)
            {
                throw new CliValidationException($"Operator '{FormatOperator(@operator)}' requires a value.");
            }

            try
            {
                return type switch
                {
                    TpsFieldType.Byte or TpsFieldType.Short or TpsFieldType.UShort or
                        TpsFieldType.Long or TpsFieldType.ULong => ParseInteger(type, literal),
                    TpsFieldType.SReal => float.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture),
                    TpsFieldType.Real => double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture),
                    TpsFieldType.Decimal => BigDecimalValue.Parse(literal),
                    TpsFieldType.Date => DateOnly.ParseExact(literal, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TpsFieldType.Time => ParseTime(literal),
                    TpsFieldType.String or TpsFieldType.CString or TpsFieldType.PString or
                        TpsFieldType.Group or TpsFieldType.Memo => literal,
                    TpsFieldType.Blob => throw new CliValidationException(
                        $"BLOB '{fieldName}' only supports is-null and is-not-null."),
                    _ => throw new CliValidationException($"TPS type {type} cannot be used in --where.")
                };
            }
            catch (CliValidationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new CliValidationException(
                    $"Value '{literal}' is not valid for {type} field '{fieldName}'.");
            }
        }

        private static TimeOnly ParseTime(string literal)
        {
            string[] formats = ["HH:mm:ss", "HH:mm:ss.f", "HH:mm:ss.ff"];
            if (TimeOnly.TryParseExact(literal, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return time;
            }

            throw new FormatException();
        }

        private static BigInteger ParseInteger(TpsFieldType type, string literal)
        {
            var value = BigInteger.Parse(literal, CultureInfo.InvariantCulture);
            var (minimum, maximum) = type switch
            {
                TpsFieldType.Byte => (BigInteger.Zero, new BigInteger(byte.MaxValue)),
                TpsFieldType.Short => (new BigInteger(short.MinValue), new BigInteger(short.MaxValue)),
                TpsFieldType.UShort => (BigInteger.Zero, new BigInteger(ushort.MaxValue)),
                TpsFieldType.Long => (new BigInteger(int.MinValue), new BigInteger(int.MaxValue)),
                TpsFieldType.ULong => (BigInteger.Zero, new BigInteger(uint.MaxValue)),
                _ => throw new InvalidOperationException($"TPS type {type} is not an integer.")
            };
            if (value < minimum || value > maximum)
            {
                throw new OverflowException();
            }

            return value;
        }

        private static BigInteger ToBigInteger(object value) => value switch
        {
            byte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            long number => number,
            _ => throw new InvalidOperationException($"Cannot convert {value.GetType().Name} to an integer.")
        };

        private static (string BaseName, int? ElementIndex) ParseArrayReference(string name)
        {
            if (!name.EndsWith(']'))
            {
                return (name, null);
            }

            var bracket = name.LastIndexOf('[');
            if (bracket < 0 || !int.TryParse(
                    name.AsSpan(bracket + 1, name.Length - bracket - 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var oneBasedIndex))
            {
                throw new CliValidationException($"Invalid array field reference '{name}'.");
            }

            if (oneBasedIndex <= 0)
            {
                throw new CliValidationException($"Array indexes are one-based in --where; found '{name}'.");
            }

            return (name[..bracket], oneBasedIndex - 1);
        }

        private static PredicateOperator ParseOperator(string value) => value.ToUpperInvariant() switch
        {
            "EQ" => PredicateOperator.Equal,
            "NE" => PredicateOperator.NotEqual,
            "LT" => PredicateOperator.LessThan,
            "LE" => PredicateOperator.LessThanOrEqual,
            "GT" => PredicateOperator.GreaterThan,
            "GE" => PredicateOperator.GreaterThanOrEqual,
            "CONTAINS" => PredicateOperator.Contains,
            "STARTS-WITH" => PredicateOperator.StartsWith,
            "ENDS-WITH" => PredicateOperator.EndsWith,
            "IS-NULL" => PredicateOperator.IsNull,
            "IS-NOT-NULL" => PredicateOperator.IsNotNull,
            _ => throw new CliValidationException($"Unknown --where operator '{value}'.")
        };

        private static void RequireOperator(
            string fieldName,
            PredicateOperator @operator,
            IReadOnlySet<PredicateOperator> allowed)
        {
            if (!allowed.Contains(@operator))
            {
                throw new CliValidationException(
                    $"Operator '{FormatOperator(@operator)}' is not valid for field '{fieldName}'.");
            }
        }

        private static IReadOnlySet<PredicateOperator> OperatorsFor(TpsFieldType type) => type switch
        {
            TpsFieldType.Byte or TpsFieldType.Short or TpsFieldType.UShort or
                TpsFieldType.Long or TpsFieldType.ULong or TpsFieldType.SReal or
                TpsFieldType.Real or TpsFieldType.Decimal or TpsFieldType.Date or
                TpsFieldType.Time => ComparableOperators,
            TpsFieldType.String or TpsFieldType.CString or TpsFieldType.PString or
                TpsFieldType.Group or TpsFieldType.Memo => TextOperators,
            TpsFieldType.Blob => NullOperators,
            _ => NullOperators
        };

        private static string FormatOperator(PredicateOperator value) => value switch
        {
            PredicateOperator.Equal => "eq",
            PredicateOperator.NotEqual => "ne",
            PredicateOperator.LessThan => "lt",
            PredicateOperator.LessThanOrEqual => "le",
            PredicateOperator.GreaterThan => "gt",
            PredicateOperator.GreaterThanOrEqual => "ge",
            PredicateOperator.Contains => "contains",
            PredicateOperator.StartsWith => "starts-with",
            PredicateOperator.EndsWith => "ends-with",
            PredicateOperator.IsNull => "is-null",
            PredicateOperator.IsNotNull => "is-not-null",
            _ => value.ToString()
        };

        private static readonly IReadOnlySet<PredicateOperator> NullOperators =
            new HashSet<PredicateOperator>([PredicateOperator.IsNull, PredicateOperator.IsNotNull]);

        private static readonly IReadOnlySet<PredicateOperator> ComparableOperators =
            new HashSet<PredicateOperator>([
                PredicateOperator.Equal,
                PredicateOperator.NotEqual,
                PredicateOperator.LessThan,
                PredicateOperator.LessThanOrEqual,
                PredicateOperator.GreaterThan,
                PredicateOperator.GreaterThanOrEqual,
                PredicateOperator.IsNull,
                PredicateOperator.IsNotNull]);

        private static readonly IReadOnlySet<PredicateOperator> TextOperators =
            new HashSet<PredicateOperator>([
                PredicateOperator.Equal,
                PredicateOperator.NotEqual,
                PredicateOperator.Contains,
                PredicateOperator.StartsWith,
                PredicateOperator.EndsWith,
                PredicateOperator.IsNull,
                PredicateOperator.IsNotNull]);
    }

    private enum PredicateOperator
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Contains,
        StartsWith,
        EndsWith,
        IsNull,
        IsNotNull
    }

    private readonly record struct BigDecimalValue(BigInteger Unscaled, int Scale) : IComparable<BigDecimalValue>
    {
        public static BigDecimalValue Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException();
            }

            var span = value.AsSpan();
            var negative = false;
            if (span[0] is '+' or '-')
            {
                negative = span[0] == '-';
                span = span[1..];
            }

            var decimalPoint = span.IndexOf('.');
            ReadOnlySpan<char> integerPart = decimalPoint < 0 ? span : span[..decimalPoint];
            ReadOnlySpan<char> fractionalPart = decimalPoint < 0 ? [] : span[(decimalPoint + 1)..];
            if ((integerPart.Length == 0 && fractionalPart.Length == 0) ||
                integerPart.IndexOfAnyExceptInRange('0', '9') >= 0 ||
                fractionalPart.IndexOfAnyExceptInRange('0', '9') >= 0)
            {
                throw new FormatException();
            }

            var digits = string.Concat(integerPart, fractionalPart);
            var unscaled = BigInteger.Parse(digits.Length == 0 ? "0" : digits, CultureInfo.InvariantCulture);
            return new BigDecimalValue(negative ? -unscaled : unscaled, fractionalPart.Length);
        }

        public int CompareTo(BigDecimalValue other)
        {
            var commonScale = Math.Max(Scale, other.Scale);
            var left = Unscaled * BigInteger.Pow(10, commonScale - Scale);
            var right = other.Unscaled * BigInteger.Pow(10, commonScale - other.Scale);
            return left.CompareTo(right);
        }
    }
}

internal sealed class CliValidationException(string message) : Exception(message);
