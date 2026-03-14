namespace Chrysalis.Codec.CodeGen.Blueprint.Models;

/// <summary>
/// A node in the CIP-0057 type schema.
/// Represents any of: $ref, constructor, list, bytes, integer, or union (anyOf).
/// </summary>
internal sealed class SchemaNode
{
    /// <summary>Human-readable name for this schema node.</summary>
    public string? Title { get; set; }

    /// <summary>Description text from the blueprint.</summary>
    public string? Description { get; set; }

    /// <summary>JSON Pointer reference to another definition (the "$ref" field).</summary>
    public string? Ref { get; set; }

    /// <summary>Data type discriminator: "constructor", "bytes", "integer", "list".</summary>
    public string? DataType { get; set; }

    /// <summary>Constructor index for Plutus data encoding.</summary>
    public int? Index { get; set; }

    /// <summary>Constructor fields (when dataType is "constructor").</summary>
    public List<SchemaNode>? Fields { get; set; }

    /// <summary>Union variants (multiple constructors).</summary>
    public List<SchemaNode>? AnyOf { get; set; }

    /// <summary>Tuple items when items is an array of schemas.</summary>
    public List<SchemaNode>? Items { get; set; }

    /// <summary>Homogeneous list element schema when items is a single schema.</summary>
    public SchemaNode? ItemsSchema { get; set; }
}
