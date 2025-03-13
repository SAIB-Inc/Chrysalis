using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.Generators.Models;

public sealed class ParameterGenerationSpec(IParameterSymbol parameter)
{
    public string Name { get; } = parameter.Name;
    public TypeRef ParameterType { get; } = new TypeRef(parameter.Type);
    public int Position { get; } = parameter.Ordinal;
}