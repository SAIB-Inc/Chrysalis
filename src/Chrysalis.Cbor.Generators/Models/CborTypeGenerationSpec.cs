namespace Chrysalis.Cbor.Generators.Models;


/// <summary>
/// Categorizes types based on their CBOR serialization pattern
/// </summary>
public enum CborTypeCategory
{
    /// <summary>Basic types directly representable in CBOR (numbers, strings, etc.)</summary>
    Primitive,

    /// <summary>Types serialized as CBOR arrays (lists, tuples, etc.)</summary>
    Array,

    /// <summary>Types serialized as CBOR maps (dictionaries, objects with named fields)</summary>
    Map,

    /// <summary>Types that can be multiple different subtypes (using tags or constructors)</summary>
    Union,

    /// <summary>Regular C# objects</summary>
    Object,

    /// <summary>Enum types (serialized as integers or strings)</summary>
    Enum,

    /// <summary>Nullable value types (int?, DateTime?, etc.)</summary>
    Nullable,

    /// <summary>Dictionary-style types with key/value pairs</summary>
    Dictionary,

    /// <summary>Constructor types with named fields</summary>
    Constr,

    /// <summary>Types that are serialized only by its contents</summary>
    Container
}

/// <summary>
/// Main specification for a type that needs CBOR serialization/deserialization
/// </summary>
public sealed class CborTypeGenerationSpec
{
    // Basic type information
    public TypeRef TypeRef { get; set; } = null!;
    public CborTypeCategory Category { get; set; }
    public string TypeInfoPropertyName { get; set; } = null!;

    // For nullable types, we need to know what the inner type is
    public CborTypeCategory InnerTypeCategory { get; set; } = CborTypeCategory.Object;
    public TypeRef? InnerTypeRef { get; set; }

    public int? Tag { get; set; }
    public bool IsDefinite { get; } = true;
    public int? Constructor { get; set; }

    // Structure information
    public List<PropertyGenerationSpec> Properties { get; } = [];
    public List<ParameterGenerationSpec> ConstructorParameters { get; } = [];

    // Collection type information
    public TypeRef? ElementType { get; set; }
    public TypeRef? KeyType { get; set; }

    // Union type information
    public List<TypeRef> UnionCases { get; } = [];

    // Serialization style
    public bool IsArray { get; }
    public bool IsMap { get; }

    // Dependency tracking
    public HashSet<TypeRef> Dependencies { get; } = [];
}