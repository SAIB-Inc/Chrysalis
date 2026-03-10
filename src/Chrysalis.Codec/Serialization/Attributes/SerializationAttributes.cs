using System.Globalization;

namespace Chrysalis.Codec.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborSerializableAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborMapAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborNullableAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborListAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborUnionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborConstrAttribute : Attribute
{
    public CborConstrAttribute() => ConstructorIndex = -1;
    public CborConstrAttribute(int constructorIndex) => ConstructorIndex = constructorIndex;
    public int ConstructorIndex { get; }
    public bool IsAnyIndexAllowed => ConstructorIndex == -1;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborTagAttribute(int tag) : Attribute
{
    public int Tag { get; } = tag;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborIndexAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborPropertyAttribute : Attribute
{
    public string StringKey { get; }
    public string Key => StringKey;
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
        StringKey = key.ToString(CultureInfo.InvariantCulture);
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
public sealed class CborIndefiniteAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborDefiniteAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = true)]
public sealed class CborUnionHintAttribute(string discriminantProperty, int discriminantValue, Type concreteType) : Attribute
{
    public string DiscriminantProperty { get; } = discriminantProperty;
    public int DiscriminantValue { get; } = discriminantValue;
    public Type ConcreteType { get; } = concreteType;
}
