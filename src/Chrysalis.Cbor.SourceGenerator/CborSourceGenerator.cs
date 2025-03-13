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
            var emitter = new Emitter(context);

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

                // Emit source code
                emitter.Emit(serializationContext);
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
}