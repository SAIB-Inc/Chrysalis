namespace Chrysalis.Blueprint.CodeGen.Models;

/// <summary>
/// Top-level CIP-0057 blueprint file.
/// </summary>
internal sealed class BlueprintFile
{
    /// <summary>Blueprint metadata.</summary>
    public BlueprintPreamble? Preamble { get; set; }

    /// <summary>Validator definitions.</summary>
    public List<BlueprintValidator> Validators { get; set; } = [];

    /// <summary>Type schema definitions, keyed by definition path.</summary>
    public Dictionary<string, SchemaNode> Definitions { get; set; } = [];
}

/// <summary>
/// Blueprint preamble metadata.
/// </summary>
internal sealed class BlueprintPreamble
{
    /// <summary>Contract title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Contract description.</summary>
    public string Description { get; set; } = "";

    /// <summary>Contract version.</summary>
    public string Version { get; set; } = "";

    /// <summary>Plutus language version (e.g., "v3").</summary>
    public string PlutusVersion { get; set; } = "";

    /// <summary>Compiler information.</summary>
    public BlueprintCompiler? Compiler { get; set; }

    /// <summary>License identifier.</summary>
    public string License { get; set; } = "";
}

/// <summary>
/// Compiler that produced the blueprint.
/// </summary>
internal sealed class BlueprintCompiler
{
    /// <summary>Compiler name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Compiler version.</summary>
    public string Version { get; set; } = "";
}

/// <summary>
/// A single validator entry in the blueprint.
/// </summary>
internal sealed class BlueprintValidator
{
    /// <summary>Validator title (e.g., "wizard.script.spend").</summary>
    public string Title { get; set; } = "";

    /// <summary>Datum schema reference.</summary>
    public SchemaNode? Datum { get; set; }

    /// <summary>Redeemer schema reference.</summary>
    public SchemaNode? Redeemer { get; set; }

    /// <summary>Parameter schemas for parameterized validators.</summary>
    public List<SchemaNode>? Parameters { get; set; }

    /// <summary>Hex-encoded compiled UPLC flat bytes.</summary>
    public string CompiledCode { get; set; } = "";

    /// <summary>Blake2b-224 script hash.</summary>
    public string Hash { get; set; } = "";
}
