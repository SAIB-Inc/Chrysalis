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
        IncrementalValueProvider<ImmutableArray<TypeDeclarationSyntax>> typeProvider =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) =>
                {
                    return node is TypeDeclarationSyntax tds &&
                           tds.AttributeLists.Count > 0;
                },
                transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node)
            .Collect();

        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> combined =
            context.CompilationProvider.Combine(typeProvider);

        context.RegisterSourceOutput(combined, GenerateMetadata);
    }

    private static void GenerateMetadata(SourceProductionContext context, (Compilation compilation, ImmutableArray<TypeDeclarationSyntax> types) source)
    {
        Compilation compilation = source.compilation;
        ImmutableArray<TypeDeclarationSyntax> types = source.types;

        foreach (TypeDeclarationSyntax type in types)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(type.SyntaxTree);
            SerializableTypeMetadata? metadata = Parser.ParseSerialazableType(type, semanticModel);

            if (metadata is not null)
            {
                // Metadata emission
                context.AddSource($"{metadata?.FullyQualifiedName.Replace("<", "`").Replace(">", "`")}.Metadata.g.cs", metadata?.ToString()!);

                // Serializer emission
                StringBuilder sb = new();
                sb.AppendLine("// Automatically generated file");
                sb.AppendLine("using System.Formats.Cbor;");

                if (metadata?.Namespace is not null)
                {
                    sb.AppendLine($"namespace {metadata?.Namespace};");
                }

                sb.AppendLine($"public partial {metadata?.Keyword} {metadata?.Indentifier}");
                sb.AppendLine("{");

                if (metadata?.SerializationType is SerializationType.Map)
                {
                    bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;
                    sb.AppendLine($"static Dictionary<{(isIntKey ? "int" : "string")}, Type> TypeMapping = new()");
                    sb.AppendLine("{");

                    var properties = metadata.Properties.ToList();
                    for (int i = 0; i < properties.Count; i++)
                    {
                        var prop = properties[i];
                        string? keyValue = isIntKey ? prop.PropertyKeyInt?.ToString() : prop.PropertyKeyString;

                        if (i == properties.Count - 1)
                        {
                            sb.AppendLine($"  {{{keyValue}, typeof({prop.PropertyTypeFullName})}}");
                        }
                        else
                        {
                            sb.AppendLine($"  {{{keyValue}, typeof({prop.PropertyTypeFullName})}},");
                        }
                    }

                    sb.AppendLine("};");
                    sb.AppendLine();
                }

                Emitter.EmitGenericCborReader(sb, metadata!);
                sb.AppendLine("}");

                if (metadata?.SerializationType is SerializationType.Constr ||
                    metadata?.SerializationType is SerializationType.Container ||
                    metadata?.SerializationType is SerializationType.List ||
                    metadata?.SerializationType is SerializationType.Map ||
                    metadata?.SerializationType is SerializationType.Union
                )
                {
                    context.AddSource($"{metadata?.FullyQualifiedName.Replace("<", "`").Replace(">", "`")}.Serializer.g.cs", sb.ToString());
                }
            }
        }
    }
}

