namespace Chrysalis.Codec.Types;

public readonly record struct CborLabel
{
    public object Value { get; }

    public CborLabel(int value) => Value = value;
    public CborLabel(long value) => Value = value;
    public CborLabel(string value) { ArgumentNullException.ThrowIfNull(value); Value = value; }

    public static implicit operator CborLabel(int value) => new(value);
    public static implicit operator CborLabel(long value) => new(value);
    public static implicit operator CborLabel(string value) => new(value);

    public static CborLabel FromInt32(int value) => new(value);
    public static CborLabel FromInt64(long value) => new(value);
    public static CborLabel FromString(string value) => new(value);
}
