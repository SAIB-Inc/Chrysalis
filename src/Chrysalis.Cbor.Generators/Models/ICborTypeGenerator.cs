namespace Chrysalis.Cbor.Generators.Models;

/// <summary>
/// Interface for type-specific code generators
/// </summary>
public interface ICborTypeGenerator
{
    string GenerateSerializer(CborTypeGenerationSpec spec);
    string GenerateDeserializer(CborTypeGenerationSpec spec);
}