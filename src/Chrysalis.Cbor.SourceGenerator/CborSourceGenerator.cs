using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Chrysalis.Cbor.SourceGenerator;

/// <summary>
/// Source generator for CBOR serialization
/// </summary>
[Generator]
public sealed partial class CborSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {

        // Create a syntax provider to select candidate types with attributes
        IncrementalValueProvider<ImmutableArray<TypeDeclarationSyntax>> typeProvider =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) =>
                {
                    // Select any type declaration that has at least one attribute
                    return node is TypeDeclarationSyntax tds &&
                           tds.AttributeLists.Count > 0;
                },
                transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node)
                .Collect();

        // Combine with compilation
        IncrementalValueProvider<(Compilation, ImmutableArray<TypeDeclarationSyntax>)> combined =
            context.CompilationProvider.Combine(typeProvider);

        // Register output
        context.RegisterSourceOutput(combined, Compose);
    }

    private void Compose(SourceProductionContext context,
    (Compilation Compilation, ImmutableArray<TypeDeclarationSyntax> Types) input)
    {
        try
        {
            var (compilation, typeDeclarations) = input;

            // Create parser and emitter
            var parser = new Parser();
            var emitter = new Emitter(context, compilation);

            // Track generated types to avoid duplicating implementations
            var generatedTypes = new HashSet<string>();

            // Process each type declaration
            foreach (var typeDecl in typeDeclarations)
            {
                // Get semantic model
                var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);

                // Check if it has the CBOR serializable attribute
                if (!HasCborAttribute(typeDecl, semanticModel))
                    continue;

                // Parse type information
                var serializationContext = parser.ParseType(typeDecl, semanticModel, context.CancellationToken);

                // Skip if no types to process
                if (serializationContext == null || serializationContext.Types.Count == 0)
                    continue;

                // Filter out types we've already generated code for
                var typesToEmit = new List<SerializableType>();
                foreach (var type in serializationContext.Types)
                {
                    string typeKey = type.Type.FullName;

                    // For generic types, use the name with arity instead of the full type name
                    // as the full name will contain the concrete type arguments
                    if (type.Type.IsGeneric)
                    {
                        typeKey = $"{type.Type.Namespace}.{type.Type.Name}";
                    }

                    if (!generatedTypes.Contains(typeKey))
                    {
                        typesToEmit.Add(type);
                        generatedTypes.Add(typeKey);
                    }
                }

                // Create a new serialization context with just the types to emit
                var filteredContext = new SerializationContext
                {
                    ContextType = serializationContext.ContextType,
                    Types = typesToEmit
                };

                // Output metadata debug information
                GenerateMetadataDebugFile(context, filteredContext, typeDecl.Identifier.Text);

                // Emit source code only for filtered types
                emitter.Emit(filteredContext);
            }
        }
        catch (Exception ex)
        {
            // Generate an error source file so we can see what happened
            string errorContent = $"""
            // Error in CBOR Source Generator
            // Message: {ex.Message}
            // Stack Trace: {ex.StackTrace}
            """;
            context.AddSource("CborGeneratorError.g.cs", SourceText.From(errorContent, Encoding.UTF8));
        }
    }

    /// <summary>
    /// Check if a type has the CBOR serializable attribute
    /// </summary>
    private bool HasCborAttribute(TypeDeclarationSyntax typeDecl, SemanticModel semanticModel)
    {
        foreach (var attributeList in typeDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                // Get the symbol info for this attribute
                if (semanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol attributeSymbol)
                {
                    // Get the attribute class name
                    string attributeClassName = attributeSymbol.ContainingType.ToDisplayString();

                    // Check against all possible forms of the attribute name
                    if (attributeClassName == Constants.CborSerializableAttributeFullName ||
                        attributeClassName == Constants.CborSerializableAttribute ||
                        attributeClassName.EndsWith($".{Constants.CborSerializableAttribute}"))
                    {
                        return true;
                    }
                }

                // Also check for simple name match in case symbol resolution fails
                string attrName = attribute.Name.ToString();
                if (attrName == Constants.CborSerializableAttribute || attrName == "CborSerializable")
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Generate a detailed debug information file with serialization context details
    /// </summary>
    private void GenerateMetadataDebugFile(SourceProductionContext context, SerializationContext serializationContext, string baseName)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// CBOR Source Generator Metadata Debug Information");
            sb.AppendLine($"// Generated: {DateTime.Now}");
            sb.AppendLine($"// Context Type: {serializationContext.ContextType?.FullName ?? "Unknown"}");
            sb.AppendLine();
            sb.AppendLine($"// Types Count: {serializationContext.Types.Count}");
            sb.AppendLine();

            foreach (var type in serializationContext.Types)
            {
                sb.AppendLine($"// ======================================================");
                sb.AppendLine($"// Type: {type.Type.FullName}");
                sb.AppendLine($"// ======================================================");
                sb.AppendLine($"//   Format: {type.Format}");
                sb.AppendLine($"//   Tag: {type.Tag}");
                sb.AppendLine($"//   IsIndefinite: {type.IsIndefinite}");
                sb.AppendLine($"//   Constructor: {type.Constructor}");
                sb.AppendLine($"//   ValidatorTypeName: {type.ValidatorTypeName ?? "None"}");
                sb.AppendLine($"//   HasValidator: {type.HasValidator}");
                sb.AppendLine($"//   Properties: {type.Properties.Count}");
                sb.AppendLine();

                // Output the detailed debug info collected during metadata extraction
                sb.AppendLine("// ----- DETAILED DEBUG INFO -----");
                sb.AppendLine(type.DebugInfo);
                sb.AppendLine("// ----- END DETAILED DEBUG INFO -----");
                sb.AppendLine();

                foreach (var prop in type.Properties)
                {
                    sb.AppendLine($"//     Property: {prop.Name}");
                    sb.AppendLine($"//       Type: {prop.Type.FullName}");
                    sb.AppendLine($"//       Key: {prop.Key}");
                    sb.AppendLine($"//       IsCborNullable: {prop.IsCborNullable}");
                    sb.AppendLine($"//       IsPropertyNullable: {prop.IsPropertyNullable}");
                    sb.AppendLine($"//       Order: {prop.Order}");
                    sb.AppendLine($"//       IsCollection: {prop.IsCollection}");
                    sb.AppendLine($"//       IsDictionary: {prop.IsDictionary}");

                    if (prop.ElementType != null)
                        sb.AppendLine($"//       ElementType: {prop.ElementType.FullName}");

                    if (prop.KeyType != null)
                        sb.AppendLine($"//       KeyType: {prop.KeyType.FullName}");
                }

                sb.AppendLine();

                if (type.Format == SerializationType.Union)
                {
                    sb.AppendLine($"//   Union Cases: {type.UnionCases.Count}");
                    foreach (var unionCase in type.UnionCases)
                    {
                        sb.AppendLine($"//     Case: {unionCase.FullName}");
                    }
                    sb.AppendLine();
                }

                if (type.Format == SerializationType.Nullable && type.InnerType != null)
                {
                    sb.AppendLine($"//   InnerType: {type.InnerType.FullName}");
                    sb.AppendLine($"//   InnerFormat: {type.InnerFormat}");
                    sb.AppendLine();
                }
            }

            // Add the debug information source
            string fileName = $"{baseName}_Metadata_Debug.g.cs";
            context.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }
        catch (Exception ex)
        {
            // If there's an error, generate a simpler error file
            string errorContent = $"""
            // Error in CBOR Metadata Debug Generator
            // Message: {ex.Message}
            // Stack Trace: {ex.StackTrace}
            """;
            context.AddSource($"{baseName}_Metadata_Debug_Error.g.cs", SourceText.From(errorContent, Encoding.UTF8));
        }
    }
}

