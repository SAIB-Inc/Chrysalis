namespace Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = true)]
public class CborPropertyAttribute : Attribute
{
    public string? Key { get; set; } = default!;
    public int? Index { get; set; } = default!;

    public CborPropertyAttribute(string key)
    {
        Key = key;
    }

    public CborPropertyAttribute(int index)
    {
        Index = index;
    }
}