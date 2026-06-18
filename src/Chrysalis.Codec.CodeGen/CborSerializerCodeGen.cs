using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Codec.CodeGen;

/// <summary>
/// Incremental source generator that emits CBOR serializers and metadata
/// for types annotated with <c>[CborSerializable]</c>.
/// </summary>
[Generator]
public sealed partial class CborSerializerCodeGen : IIncrementalGenerator
{
    /// <summary>
    /// Registers syntax and compilation providers for incremental code generation.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<TypeDeclarationSyntax>> typeProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax tds && tds.AttributeLists.Count > 0,
                transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node
            ).Collect();

        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> combined = context.CompilationProvider.Combine(typeProvider);

        context.RegisterSourceOutput(combined, EmitSerializersAndMetadata);
    }

    /// <summary>
    /// Registry of all serializable types in the current compilation, keyed by normalized
    /// fully-qualified name. Populated before emission so the union emitter can classify the
    /// CBOR shape of a member's property when that property is itself a generated record
    /// (e.g. resolve a <c>[CborList]</c> record to an array) — needed for secondary structural
    /// probing when union members share a leading index.
    /// </summary>
    internal static IReadOnlyDictionary<string, SerializableTypeMetadata> TypeRegistry { get; private set; }
        = new Dictionary<string, SerializableTypeMetadata>();

    /// <summary>
    /// Normalizes a fully-qualified type name for registry keys and lookups by stripping the
    /// <c>global::</c> prefix, nullable annotations, and surrounding whitespace.
    /// </summary>
    internal static string NormalizeTypeName(string fullyQualifiedName) =>
        fullyQualifiedName.Replace("global::", "").Replace("?", "").Trim();

    private static void EmitSerializersAndMetadata(SourceProductionContext context, (Compilation compilation, ImmutableArray<TypeDeclarationSyntax> types) source)
    {
        Compilation compilation = source.compilation;
        ImmutableArray<TypeDeclarationSyntax> types = source.types;

        // First pass: parse every serializable type and build the registry. We parse once here
        // and emit from the parsed list below, so each type is parsed a single time.
        List<SerializableTypeMetadata> parsed = [];
        Dictionary<string, SerializableTypeMetadata> registry = [];
        foreach (TypeDeclarationSyntax type in types)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(type.SyntaxTree);
            SerializableTypeMetadata? metadata = Parser.ParseSerialazableType(type, semanticModel);

            if (metadata is not null)
            {
                parsed.Add(metadata);
                registry[NormalizeTypeName(metadata.FullyQualifiedName)] = metadata;
            }
        }

        TypeRegistry = registry;

        // Second pass: emit serializers and metadata with the registry available.
        foreach (SerializableTypeMetadata metadata in parsed)
        {
            Emitter.EmitSerializerAndMetadata(context, metadata);
        }
    }
}
