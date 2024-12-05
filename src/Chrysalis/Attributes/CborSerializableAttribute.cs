using Chrysalis.Converters;

namespace Chrysalis.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class CborConverterAttribute(Type converterType) : Attribute
{
    public Type ConverterType { get; } = converterType;
}


[AttributeUsage(AttributeTargets.Class)]
public class CborIndexAttribute(int tag) : Attribute
{
    public int Index { get; } = tag;
}


[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class CborPropertyAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}


[AttributeUsage(AttributeTargets.Class)]
public class CborDefiniteAttribute : Attribute { }


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class CborSizeAttribute(int size) : Attribute
{
    public int Size { get; } = size;
}

[AttributeUsage(AttributeTargets.Interface)]
public class CborUnionTypesAttribute(params Type[] types) : Attribute
{
    public Type[] Types { get; } = types;
}