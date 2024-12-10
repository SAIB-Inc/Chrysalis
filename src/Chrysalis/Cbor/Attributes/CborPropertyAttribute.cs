namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class CborPropertyAttribute : Attribute
{
    public string PropertyName { get; }
    public int? Index { get; }

    // Constructor for name-only
    public CborPropertyAttribute(string propertyName)
    {
        PropertyName = propertyName;
        Index = null;
    }

    // Constructor for index-only
    public CborPropertyAttribute(int index)
    {
        PropertyName = string.Empty;
        Index = index;
    }

    // Constructor for both
    public CborPropertyAttribute(string propertyName, int index)
    {
        PropertyName = propertyName;
        Index = index;
    }
}