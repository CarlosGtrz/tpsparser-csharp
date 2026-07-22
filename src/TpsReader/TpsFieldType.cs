namespace TpsReader;

/// <summary>Identifies the logical type of a TPS field, MEMO, or BLOB.</summary>
public enum TpsFieldType
{
    /// <summary>An unsupported or unrecognized field type.</summary>
    Unknown = 0,
    /// <summary>An unsigned 8-bit integer.</summary>
    Byte,
    /// <summary>A signed 16-bit integer.</summary>
    Short,
    /// <summary>An unsigned 16-bit integer.</summary>
    UShort,
    /// <summary>A calendar date.</summary>
    Date,
    /// <summary>A time of day with hundredths-of-a-second precision.</summary>
    Time,
    /// <summary>A signed 32-bit integer.</summary>
    Long,
    /// <summary>An unsigned 32-bit integer.</summary>
    ULong,
    /// <summary>A 32-bit floating-point value.</summary>
    SReal,
    /// <summary>A 64-bit floating-point value.</summary>
    Real,
    /// <summary>A losslessly decoded decimal string.</summary>
    Decimal,
    /// <summary>A fixed-width string.</summary>
    String,
    /// <summary>A NUL-terminated string.</summary>
    CString,
    /// <summary>A length-prefixed string.</summary>
    PString,
    /// <summary>A fixed-width grouped storage area projected as text by default.</summary>
    Group,
    /// <summary>A text MEMO.</summary>
    Memo,
    /// <summary>A binary large object.</summary>
    Blob
}
