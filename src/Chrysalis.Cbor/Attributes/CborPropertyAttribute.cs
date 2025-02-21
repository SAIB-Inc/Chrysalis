namespace Chrysalis.Cbor.Attributes;

/// <summary>
/// Maps a property for complex types (maps, dictionaries, etc)
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class CborPropertyAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}