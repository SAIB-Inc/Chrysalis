using Chrysalis.Blueprint.CodeGen.Models;

namespace Chrysalis.Blueprint.CodeGen.Analysis;

/// <summary>
/// Classifies what kind of C# type each schema node should map to.
/// </summary>
internal enum TypeCategory
{
    /// <summary>Byte string primitive.</summary>
    PrimitiveBytes,

    /// <summary>Integer primitive.</summary>
    PrimitiveInteger,

    /// <summary>Boolean primitive.</summary>
    PrimitiveBool,

    /// <summary>Opaque Plutus data.</summary>
    PrimitiveData,

    /// <summary>Single-constructor record type.</summary>
    SingleConstructor,

    /// <summary>Multi-constructor union type.</summary>
    Union,

    /// <summary>Option type (Some/None).</summary>
    Option,

    /// <summary>Fixed-length tuple (CBOR list with typed items).</summary>
    Tuple,

    /// <summary>Homogeneous list.</summary>
    List,

    /// <summary>Forward reference placeholder.</summary>
    Ref
}

/// <summary>
/// A fully resolved type ready for code generation.
/// </summary>
internal sealed class ResolvedType
{
    /// <summary>Original definition key from the blueprint.</summary>
    public string DefinitionKey { get; set; } = "";

    /// <summary>Generated C# type name.</summary>
    public string TypeName { get; set; } = "";

    /// <summary>Classification of this type.</summary>
    public TypeCategory Category { get; set; }

    /// <summary>Original schema node.</summary>
    public SchemaNode? Schema { get; set; }

    /// <summary>Constructors (for single-constructor and union types).</summary>
    public List<ResolvedConstructor>? Constructors { get; set; }

    /// <summary>Fields for tuple types.</summary>
    public List<ResolvedField>? TupleFields { get; set; }

    /// <summary>Element type for list types.</summary>
    public ResolvedType? ListElementType { get; set; }

    /// <summary>Inner type for Option types.</summary>
    public ResolvedType? OptionInnerType { get; set; }
}

/// <summary>
/// A resolved constructor variant.
/// </summary>
internal sealed class ResolvedConstructor
{
    /// <summary>C# name for this constructor.</summary>
    public string Name { get; set; } = "";

    /// <summary>Plutus constructor index.</summary>
    public int Index { get; set; }

    /// <summary>Constructor fields.</summary>
    public List<ResolvedField> Fields { get; set; } = [];
}

/// <summary>
/// A resolved field within a constructor or tuple.
/// </summary>
internal sealed class ResolvedField
{
    /// <summary>C# property name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Field order for CBOR serialization.</summary>
    public int Order { get; set; }

    /// <summary>Fully qualified C# type name.</summary>
    public string CSharpType { get; set; } = "";

    /// <summary>Whether this field should be nullable.</summary>
    public bool IsNullable { get; set; }
}

/// <summary>
/// Resolves $ref pointers and builds a flat list of types to generate.
/// </summary>
internal sealed class SchemaResolver
{
    private readonly Dictionary<string, SchemaNode> _definitions;
    private readonly Dictionary<string, ResolvedType> _resolved = [];
    private readonly HashSet<string> _inProgress = [];
    private readonly TypeClassifier _classifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaResolver"/> class.
    /// </summary>
    /// <param name="definitions">Blueprint definitions to resolve.</param>
    public SchemaResolver(Dictionary<string, SchemaNode> definitions)
    {
        _definitions = definitions;
        _classifier = new TypeClassifier(this);
    }

    /// <summary>
    /// Resolves all definitions and returns the type map.
    /// </summary>
    public Dictionary<string, ResolvedType> ResolveAll()
    {
        foreach (KeyValuePair<string, SchemaNode> kv in _definitions)
        {
            _ = ResolveDefinition(kv.Key);
        }

        return _resolved;
    }

    /// <summary>
    /// Resolves a single definition by key, with cycle detection.
    /// </summary>
    public ResolvedType? ResolveDefinition(string defKey)
    {
        if (_resolved.TryGetValue(defKey, out ResolvedType? existing))
        {
            return existing;
        }

        if (!_definitions.TryGetValue(defKey, out SchemaNode? schema))
        {
            return null;
        }

        if (_inProgress.Contains(defKey))
        {
            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = Generation.NamingConventions.TypeNameFromDefinitionKey(defKey, schema),
                Category = TypeCategory.Ref,
                Schema = schema
            };
        }

        _ = _inProgress.Add(defKey);
        ResolvedType resolved = _classifier.Classify(defKey, schema);
        _resolved[defKey] = resolved;
        _ = _inProgress.Remove(defKey);

        return resolved;
    }

    /// <summary>
    /// Decodes a $ref JSON Pointer into a definition key.
    /// </summary>
    public static string? ResolveRef(string? refPath)
    {
        const string prefix = "#/definitions/";
        if (refPath == null || !refPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string defKey = refPath.Substring(prefix.Length);
        defKey = defKey.Replace("~1", "/").Replace("~0", "~");
        return defKey;
    }

    /// <summary>
    /// Returns the C# type name for a $ref path.
    /// </summary>
    public string GetCSharpTypeForRef(string? refPath)
    {
        string? defKey = ResolveRef(refPath);
        if (defKey == null)
        {
            return "IPlutusData";
        }

        ResolvedType? resolved = ResolveDefinition(defKey);
        if (resolved == null)
        {
            return "IPlutusData";
        }

        return GetCSharpTypeName(resolved);
    }

    /// <summary>
    /// Maps a resolved type to its C# type name string.
    /// </summary>
    public static string GetCSharpTypeName(ResolvedType type)
    {
        switch (type.Category)
        {
            case TypeCategory.PrimitiveBytes:
                return "PlutusBoundedBytes";
            case TypeCategory.PrimitiveInteger:
                return "IPlutusBigInt";
            case TypeCategory.PrimitiveBool:
                return "IPlutusBool";
            case TypeCategory.PrimitiveData:
                return "IPlutusData";
            case TypeCategory.Option:
                string inner = type.OptionInnerType != null
                    ? GetCSharpTypeName(type.OptionInnerType)
                    : "IPlutusData";
                return $"ICborOption<{inner}>";
            case TypeCategory.List:
                string elem = type.ListElementType != null
                    ? GetCSharpTypeName(type.ListElementType)
                    : "IPlutusData";
                return $"CborDefList<{elem}>";
            case TypeCategory.Union:
                return "I" + type.TypeName;
            case TypeCategory.SingleConstructor:
            case TypeCategory.Tuple:
            case TypeCategory.Ref:
                return type.TypeName;
            default:
                return "IPlutusData";
        }
    }

    /// <summary>
    /// Returns the C# type for an inline schema node (field-level).
    /// </summary>
    public string GetCSharpTypeForSchema(SchemaNode schema)
    {
        if (schema.Ref != null)
        {
            return GetCSharpTypeForRef(schema.Ref);
        }

        if (schema.DataType == "bytes")
        {
            return "PlutusBoundedBytes";
        }

        if (schema.DataType == "integer")
        {
            return "IPlutusBigInt";
        }

        return "IPlutusData";
    }
}
