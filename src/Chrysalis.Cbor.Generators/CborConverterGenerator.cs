using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.Generators;

public class CborConverterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TypeDeclarationSyntax> converterProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) =>
            {
                var syntax = (TypeDeclarationSyntax)ctx.Node;
                var semanticModel = ctx.SemanticModel;

                if (semanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol) return null;

                var implementsIConverter = symbol.Interfaces.Any(i => i.ToDisplayString() == "Chrysalis.Cbor.Serialization.ICborConverter");
                return implementsIConverter ? syntax : null;
            }
        ).Where(m => m is not null)!;

        IncrementalValuesProvider<TypeDeclarationSyntax> cborBaseProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax || node is RecordDeclarationSyntax,
            transform: static (ctx, _) =>
            {
                var syntax = (TypeDeclarationSyntax)ctx.Node;
                var semanticModel = ctx.SemanticModel;

                if (semanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol) return null;

                var baseType = symbol.BaseType;
                while (baseType != null)
                {
                    if (baseType.ToDisplayString() == "Chrysalis.Cbor.Types.CborBase")
                    {
                        return syntax;
                    }
                    baseType = baseType.BaseType;
                }
                return null;
            }
        ).Where(m => m is not null)!;

        var compilation = context.CompilationProvider.Combine(converterProvider.Collect().Combine(cborBaseProvider.Collect()));

        context.RegisterSourceOutput(compilation, GenerateCborConverter!);
    }

    private void GenerateCborConverter(SourceProductionContext ctx,
    (Compilation Compilation, (ImmutableArray<TypeDeclarationSyntax> Converters, ImmutableArray<TypeDeclarationSyntax> Types)) tuple)
    {
        var (compilation, (converters, types)) = tuple;

        foreach (var typeSyntax in types)
        {
            var semanticModel = compilation.GetSemanticModel(typeSyntax.SyntaxTree);

            if (semanticModel.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol typeSymbol) continue;

            System.Diagnostics.Debug.WriteLine($"Checking {typeSymbol.ToDisplayString()} for IConverter");

            var converterAttribute = typeSymbol
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "Chrysalis.Cbor.Attributes.CborConverterAttribute");

            if (converterAttribute == null || converterAttribute.ConstructorArguments.Length == 0) continue;
            if (converterAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol converterType) continue;

            string typeName = typeSymbol.Name;
            string converterName = converterType.Name;
            string namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();

            var converterCode = $$"""
                using System.Formats.Cbor;
                using Chrysalis.Cbor.Types;
                using {{namespaceName}};

                namespace Chrysalis.Cbor.Generated;

                public static class {{typeName}}Extension
                {
                    public static {{typeName}} Read(this {{typeName}} self, CborReader reader) => throw new NotImplementedException();
                    public static void Write(this {{typeName}} self, CborWriter writer, {{typeName}} value) => throw new NotImplementedException();
                }
            """;

            ctx.AddSource($"{typeName}Extension.g.cs", converterCode);
        }
    }
}