using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Chrysalis.Cbor.CodeGen;

[Generator]
public sealed partial class CborSerializerCodeGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Create a syntax provider to select candidate types with attributes
        IncrementalValueProvider<ImmutableArray<TypeDeclarationSyntax>> typeProvider =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) =>
                {
                    return node is TypeDeclarationSyntax tds &&
                           tds.AttributeLists.Count > 0;
                },
                transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node)
            .Collect();

        // Combine with compilation
        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> combined =
            context.CompilationProvider.Combine(typeProvider);

        // Register output
        context.RegisterSourceOutput(combined, GenerateMetadata);
    }

    private static void GenerateMetadata(SourceProductionContext context, (Compilation compilation, ImmutableArray<TypeDeclarationSyntax> types) source)
    {
        // Get the compilation and types
        Compilation compilation = source.compilation;
        ImmutableArray<TypeDeclarationSyntax> types = source.types;

        foreach (TypeDeclarationSyntax type in types)
        {
            // Parse the types
            SemanticModel semanticModel = compilation.GetSemanticModel(type.SyntaxTree);
            SerializableTypeMetadata? metadata = Parser.ParseSerialazableType(type, semanticModel);

            if (metadata is not null)
            {
                context.AddSource($"{metadata?.FullyQualifiedName}.g.cs", metadata?.ToString()!);
            }
        }
    }
}

