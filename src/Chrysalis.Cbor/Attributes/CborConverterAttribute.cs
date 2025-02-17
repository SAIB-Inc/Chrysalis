namespace Chrysalis.Cbor.Attributes;

/// <summary>
/// Specifies the CBOR converter to use for this type
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class CborConverterAttribute(Type converterType) : Attribute
{
    public Type ConverterType { get; } = converterType;
}