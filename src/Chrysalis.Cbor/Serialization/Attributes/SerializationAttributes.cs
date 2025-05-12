

namespace Chrysalis.Cbor.Serialization.Attributes;

// Type-level attributes

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborSerializableAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborMapAttribute : Attribute
{
    public CborMapAttribute() { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborNullableAttribute : Attribute
{
    public CborNullableAttribute() { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborListAttribute : Attribute
{
    public CborListAttribute() { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborUnionAttribute : Attribute
{
    public CborUnionAttribute() { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborConstrAttribute : Attribute
{
    public CborConstrAttribute()
    {
        ConstructorIndex = -1;
    }

    public CborConstrAttribute(int constructorIndex)
    {
        ConstructorIndex = constructorIndex;
    }

    public int ConstructorIndex { get; }

    public bool IsAnyIndexAllowed => ConstructorIndex == -1;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborTagAttribute(int tag) : Attribute
{
    public int Tag { get; } = tag;
}

// Property/parameter attributes

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborPropertyAttribute : Attribute
{
    public string StringKey { get; }
    public int? IntKey { get; }
    public bool IsIntKey { get; }

    public CborPropertyAttribute(string key)
    {
        StringKey = key;
        IntKey = null;
        IsIntKey = false;
    }

    public CborPropertyAttribute(int key)
    {
        StringKey = key.ToString();
        IntKey = key;
        IsIntKey = true;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborSizeAttribute(int size) : Attribute
{
    public int Size { get; } = size;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborIndefiniteAttribute : Attribute
{
    public CborIndefiniteAttribute() { }
}