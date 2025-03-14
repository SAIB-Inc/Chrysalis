using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Chrysalis.Cbor.Generators.Models;
using Chrysalis.Cbor.Generators.Converters;

namespace Chrysalis.Cbor.Generators;

[Generator]
public sealed partial class CborSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Create a syntax provider to select candidate context classes 
        // (e.g. classes decorated with // [CborSerializable])
        IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<TypeDeclarationSyntax>> contextTypeProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, cancellationToken) =>
            {
                // Select any type declaration (class, record, etc.) that has at least one attribute
                return node is TypeDeclarationSyntax tds &&
                       tds.AttributeLists.Count > 0 &&
                       tds.AttributeLists.Any(al => al.Attributes.Count > 0);
            },
            transform: static (ctx, cancellationToken) => (TypeDeclarationSyntax)ctx.Node)
            .Collect();

        // Combine the context classes with the compilation
        IncrementalValueProvider<Compilation> compilationProvider = context.CompilationProvider;
        IncrementalValueProvider<(Compilation Left, System.Collections.Immutable.ImmutableArray<TypeDeclarationSyntax> Right)> combined = compilationProvider.Combine(contextTypeProvider);

        context.RegisterSourceOutput(combined, Compose);
    }

    private void Compose(SourceProductionContext spc, (Compilation Left, System.Collections.Immutable.ImmutableArray<TypeDeclarationSyntax> Right) combinedResult)
    {
        (Compilation compilation, System.Collections.Immutable.ImmutableArray<TypeDeclarationSyntax> contextClasses) = combinedResult;

        foreach (TypeDeclarationSyntax? contextClass in contextClasses)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(contextClass.SyntaxTree);
            // Use our built-in Parser to create a ContextGenerationSpec from the context class.
            Parser parser = new();

            ContextGenerationSpec? contextSpec = parser.ParseContextGenerationSpec(contextClass, semanticModel, default);
            if (contextSpec == null) continue;

            // Get all the specs for all types
            var allSpecs = contextSpec.Types;

            // Iterate over each type that needs CBOR serialization
            foreach (CborTypeGenerationSpec typeSpec in allSpecs)
            {
                string deserializerCode;
                string serializerCode;

                // Switch on the type's category to select the appropriate generator.
                ICborTypeGenerator? typeGenerator;

                // First, determine the base generator based on the category
                ICborTypeGenerator baseGenerator;
                switch (typeSpec.Category)
                {
                    case CborTypeCategory.Constr:
                    case CborTypeCategory.Array:
                        baseGenerator = new ListTypeGenerator();
                        break;
                    case CborTypeCategory.Map:
                        baseGenerator = new MapTypeGenerator();
                        break;
                    case CborTypeCategory.Union:
                        baseGenerator = new UnionTypeGenerator();
                        break;
                    case CborTypeCategory.Container:
                        baseGenerator = new ContainerTypeGenerator();
                        break;
                    case CborTypeCategory.Nullable:
                        // For nullable types, we need to determine the inner type's generator
                        var innerCategory = typeSpec.InnerTypeCategory != CborTypeCategory.Object
                            ? typeSpec.InnerTypeCategory
                            : CborTypeCategory.Map; // Default to map if not specified

                        ICborTypeGenerator innerGenerator;
                        switch (innerCategory)
                        {
                            case CborTypeCategory.Constr:
                            case CborTypeCategory.Array:
                                innerGenerator = new ListTypeGenerator();
                                break;
                            case CborTypeCategory.Map:
                                innerGenerator = new MapTypeGenerator();
                                break;
                            case CborTypeCategory.Union:
                                innerGenerator = new UnionTypeGenerator();
                                break;
                            case CborTypeCategory.Container:
                                innerGenerator = new ContainerTypeGenerator();
                                break;
                            default:
                                continue; // Skip if we can't determine the inner type
                        }

                        // Wrap the inner generator with the nullable generator
                        baseGenerator = new NullableTypeGenerator(innerGenerator);
                        break;
                    default:
                        continue; // Skip unsupported categories
                }

                // Use the determined generator
                typeGenerator = baseGenerator;

                serializerCode = typeGenerator.GenerateSerializer(typeSpec);
                deserializerCode = typeGenerator.GenerateDeserializer(typeSpec);

                // Compose the final source file with explicit implementation 
                string source = $$"""
                        // This is a generated code
                        using System;
                        using System.Collections.Generic;
                        using System.Formats.Cbor;
                        using Chrysalis.Cbor.Types;
                        namespace {{typeSpec.TypeRef.Namespace}};
                        
                        public partial {{GetTypeKeyword(contextClass)}} {{typeSpec.TypeRef.Name}}
                        {
                            // Serialization implementation
                            public static new void Write(CborWriter writer, {{typeSpec.TypeRef.FullyQualifiedName}} data)
                            {
                                {{serializerCode}}
                            }
            
                            // Deserialization implementation
                            public static new {{typeSpec.TypeRef.FullyQualifiedName}} Read(CborReader reader)
                            {
                                {{deserializerCode}}
                            }   
                        }
                    """;
                spc.AddSource($"{typeSpec.TypeRef.Name}_Cbor.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    string GetTypeKeyword(TypeDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration switch
        {
            RecordDeclarationSyntax => "record",
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            _ => "class"
        };
    }
}
