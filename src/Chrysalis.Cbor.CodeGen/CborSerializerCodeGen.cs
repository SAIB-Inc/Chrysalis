using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.CodeGen;

[Generator]
public sealed partial class CborSerializerCodeGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<TypeDeclarationSyntax>> typeProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax tds && tds.AttributeLists.Count > 0,
                transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node
            ).Collect();

        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> combined = context.CompilationProvider.Combine(typeProvider);

        context.RegisterSourceOutput(combined, EmitSerializersAndMetadata);
    }

    private static void EmitSerializersAndMetadata(SourceProductionContext context, (Compilation compilation, ImmutableArray<TypeDeclarationSyntax> types) source)
    {
        Compilation compilation = source.compilation;
        ImmutableArray<TypeDeclarationSyntax> types = source.types;

        foreach (TypeDeclarationSyntax type in types)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(type.SyntaxTree);
            SerializableTypeMetadata? metadata = Parser.ParseSerialazableType(type, semanticModel);

            if (metadata is not null)
            {
                Emitter.EmitSerializerAndMetadata(context, metadata);
            }
        }
    }
}

