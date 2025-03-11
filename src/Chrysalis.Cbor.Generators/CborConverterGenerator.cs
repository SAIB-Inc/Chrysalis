using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.Generators;

[Generator]
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

    private void GenerateCborConverter(SourceProductionContext ctx, (Compilation Compilation, (ImmutableArray<TypeDeclarationSyntax> Converters, ImmutableArray<TypeDeclarationSyntax> Types)) tuple)
    {
        var (compilation, (converters, types)) = tuple;

        Dictionary<string, TypeDeclarationSyntax> convertersByName = converters
            .ToDictionary(
                c => compilation.GetSemanticModel(c.SyntaxTree).GetDeclaredSymbol(c)?.ToDisplayString() ?? string.Empty,
                c => c
            );

        foreach (var typeSyntax in types)
        {
            var semanticModel = compilation.GetSemanticModel(typeSyntax.SyntaxTree);

            if (semanticModel.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol typeSymbol) continue;

            // Check if already inherits from CborBase to avoid redundant inheritance
            var baseType = typeSymbol.BaseType;
            var interfaces = typeSymbol.Interfaces.Select(i => i.ToDisplayString()).ToList();

            // Add all namespaces from base types and interfaces
            var additionalNamespaces = new HashSet<string>();
            if (typeSymbol.BaseType != null && !string.IsNullOrEmpty(typeSymbol.BaseType.ContainingNamespace?.ToDisplayString()))
            {
                additionalNamespaces.Add($"using {typeSymbol.BaseType.ContainingNamespace!.ToDisplayString()};");
            }

            foreach (var iface in typeSymbol.Interfaces)
            {
                if (!string.IsNullOrEmpty(iface.ContainingNamespace?.ToDisplayString()))
                {
                    additionalNamespaces.Add($"using {iface.ContainingNamespace!.ToDisplayString()};");
                }
            }

            var converterAttribute = typeSymbol
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "CborConverterAttribute" ||
                                   a.AttributeClass?.ToDisplayString() == "Chrysalis.Cbor.Attributes.CborConverterAttribute");

            if (converterAttribute == null) continue;

            // Get the converter type from the attribute
            if (converterAttribute.ConstructorArguments.Length == 0) continue;
            if (converterAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol converterTypeSymbol) continue;
            string converterTypeName = converterTypeSymbol.ToDisplayString();
            string converterNamespace = converterTypeSymbol.ContainingNamespace.ToDisplayString();

            // Find the converter implementation
            if (!convertersByName.TryGetValue(converterTypeName, out var converterSyntax)) continue;

            // Extract Read and Write method implementations
            var readMethod = converterSyntax.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "Read");

            var writeMethod = converterSyntax.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "Write");

            if (readMethod == null || writeMethod == null) continue;

            string readBody = readMethod.Body?.ToString() ?? "{ throw new NotImplementedException(); }";
            string writeBody = writeMethod.Body?.ToString() ?? "{ throw new NotImplementedException(); }";

            // Clean up braces from the extracted body
            readBody = readBody.Trim();
            if (readBody.StartsWith("{")) readBody = readBody.Substring(1);
            if (readBody.EndsWith("}")) readBody = readBody.Substring(0, readBody.Length - 1);

            writeBody = writeBody.Trim();
            if (writeBody.StartsWith("{")) writeBody = writeBody.Substring(1);
            if (writeBody.EndsWith("}")) writeBody = writeBody.Substring(0, writeBody.Length - 1);

            // Extract using directives from the converter's syntax tree
            var converterSyntaxTree = converterSyntax.SyntaxTree;
            var converterRoot = converterSyntaxTree.GetRoot();
            var usingDirectives = converterRoot.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .ToList();

            usingDirectives.Add("using Chrysalis.Cbor.Serialization;");
            usingDirectives.Add("using Chrysalis.Cbor.Types;");
            usingDirectives.AddRange(additionalNamespaces);

            string typeName = typeSymbol.Name;
            string namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
            string typeSyntaxString = typeSyntax is ClassDeclarationSyntax
                ? "class"
                : "record";

            string typeParameters = "";
            if (typeSymbol.IsGenericType)
            {
                var typeParams = typeSymbol.TypeParameters.Select(p => p.Name);
                typeParameters = $"<{string.Join(", ", typeParams)}>";
            }


            var converterCode = $$"""
                #nullable enable
                // This code is generated
                {{string.Join("\n", usingDirectives.Distinct())}}

                namespace {{namespaceName}};
                
                public partial {{typeSyntaxString}} {{typeName}}{{typeParameters}}
                {
                    public override object? Read(CborReader reader, CborOptions options)
                    {
                        {{readBody}}
                    }
                    
                    public override void Write(CborWriter writer, List<object?> value, CborOptions options)
                    {
                        {{writeBody}}
                    }
                }
            """;

            ctx.AddSource($"{namespaceName}.{typeName}.g.cs", converterCode);
        }
    }
}