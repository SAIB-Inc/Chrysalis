namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class CborConverterAttribute(Type converterType) : Attribute
{
    public Type ConverterType { get; } = converterType;
}