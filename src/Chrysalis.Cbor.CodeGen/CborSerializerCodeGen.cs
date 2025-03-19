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

    private static void GenerateMetadata(SourceProductionContext context, (Compilation, ImmutableArray<TypeDeclarationSyntax>) source)
    {
        var (compilation, types) = source;
        var errorMessages = new StringBuilder(); // To collect error messages
        var diagnostics = new StringBuilder(); // To collect diagnostic messages
        var parser = new Parser(context);

        // Collect generated metadata for each type
        foreach (var typeDecl in types)
        {
            try
            {
                // Get the semantic model for the type declaration
                var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
                // Parse the type to generate the metadata
                SerializableTypeMetadata? typeMetadata = parser.ParseSerialazableType(typeDecl, semanticModel);

                if (typeMetadata != null)
                {
                    // Generate source code for the metadata
                    var metadataSource = GenerateMetadataSourceCode(typeMetadata);
                    context.AddSource($"{typeMetadata.FullName}_Metadata.g.cs", SourceText.From(metadataSource, Encoding.UTF8));
                }
                else
                {
                    errorMessages.AppendLine($"[Error] Failed to generate metadata for {typeDecl.Identifier.Text}. Metadata returned null.");
                }
            }
            catch (Exception ex)
            {
                // Capture exceptions and add to error messages
                errorMessages.AppendLine($"[Error] Exception processing {typeDecl.Identifier.Text}: {ex.Message}");
            }
        }

        // Emit error log
        if (errorMessages.Length > 0)
        {
            context.AddSource("CborSerializerCodeGen_Errors.g.cs", SourceText.From(errorMessages.ToString(), Encoding.UTF8));
        }

        // Emit diagnostics log
        if (diagnostics.Length > 0)
        {
            context.AddSource("CborSerializerCodeGen_Diagnostics.g.cs", SourceText.From(diagnostics.ToString(), Encoding.UTF8));
        }
    }

    private static string GenerateMetadataSourceCode(SerializableTypeMetadata typeMetadata)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"// Generated metadata for {typeMetadata.FullName}");
        sb.AppendLine($"public class {typeMetadata.TypeName}Metadata");
        sb.AppendLine("{");

        sb.AppendLine($"    public string TypeName => \"{typeMetadata.TypeName}\";");
        sb.AppendLine($"    public string Namespace => \"{typeMetadata.Namespace}\";");
        sb.AppendLine($"    public string FullName => \"{typeMetadata.FullName}\";");
        sb.AppendLine($"    public string TypeDeclaration => \"{typeMetadata.TypeDeclaration}\";");
        sb.AppendLine($"    public int? CborTag => {typeMetadata.CborTag?.ToString() ?? "null"};");
        sb.AppendLine($"    public int? CborIndex => {typeMetadata.CborIndex?.ToString() ?? "null"};");
        sb.AppendLine($"    public string? Validator => \"{typeMetadata.Validator ?? "null"}\";");

        sb.AppendLine("    public List<PropertyMetadata> Properties = new List<PropertyMetadata>();");
        foreach (var property in typeMetadata.Properties)
        {
            sb.AppendLine($"    public class PropertyMetadata");
            sb.AppendLine("    {");
            sb.AppendLine($"        public string PropertyName => \"{property.PropertyName}\";");
            sb.AppendLine($"        public string PropertyType => \"{property.PropertyType}\";");
            sb.AppendLine($"        public int? Order => {property.Order?.ToString() ?? "null"};");
            sb.AppendLine($"        public string? PropertyKeyString => \"{property.PropertyKeyString ?? "null"}\";");
            sb.AppendLine($"        public int? PropertyKeyInt => {property.PropertyKeyInt?.ToString() ?? "null"};");
            sb.AppendLine($"        public bool IsNullable => {property.IsNullable.ToString().ToLower()};");
            sb.AppendLine($"        public int? Size => {property.Size?.ToString() ?? "null"};");
            sb.AppendLine($"        public bool IsIndefinite => {property.IsIndefinite.ToString().ToLower()};");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }
}

