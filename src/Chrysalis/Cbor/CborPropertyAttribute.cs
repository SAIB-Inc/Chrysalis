namespace Chrysalis.Cbor;

[AttributeUsage(AttributeTargets.Property)]
public class CborPropertyAttribute : Attribute
{
    public string? Name { get; set; }
    public int Index { get; set; } = 0;
    public bool IsBasicType { get; set; }

    public CborPropertyAttribute(string Name)
    {
        this.Name = Name;
    }

    public CborPropertyAttribute(int Index)
    {
        this.Index = Index;
    }

    public CborPropertyAttribute(string Name, int Index)
    {
        this.Name = Name;
        this.Index = Index;
    }
}