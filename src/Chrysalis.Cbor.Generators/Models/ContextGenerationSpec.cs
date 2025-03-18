namespace Chrysalis.Cbor.Generators.Models;

public class ContextGenerationSpec
{
    // The type of the context class itself (the one with [CborSerializable] attributes)
    public TypeRef ContextType { get; set; } = null!;

    // All the types that need CBOR serialization/deserialization code
    public List<CborTypeGenerationSpec> Types { get; set; } = new();
}