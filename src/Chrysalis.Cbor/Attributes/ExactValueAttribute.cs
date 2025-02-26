namespace Chrysalis.Cbor.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class ExactValueAttribute(object value) : Attribute
{
    public object Value { get; } = value;
}