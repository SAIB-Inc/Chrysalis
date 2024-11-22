namespace Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public class CborPropertyAttribute : Attribute
{
    public string? Key { get; } = default!;
    public int? Index { get; } = default!;

    public CborPropertyAttribute(string key)
    {
        Key = key;
    }

    public CborPropertyAttribute(int index)
    {
        Index = index;
    }
}