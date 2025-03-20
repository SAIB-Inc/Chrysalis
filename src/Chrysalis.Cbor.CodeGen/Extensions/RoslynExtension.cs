using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.CodeGen.Extensions;

public static class RoslynExtension
{
    public static string? GetNamespace(this TypeDeclarationSyntax self)
    {
        if (self.Parent is NamespaceDeclarationSyntax namespaceDecl)
        {
            return namespaceDecl.Name.ToString();
        }
        // Check if the parent is a FileScopedNamespaceDeclarationSyntax (C# 10+)
        else if (self.Parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDecl)
        {
            return fileScopedNamespaceDecl.Name.ToString();
        }

        return null;
    }

    // First argument
    private static T? GetFirstAttributeValue<T>(this SyntaxNode node, string attributeName, SemanticModel semanticModel)
    {
        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);
        AttributeData? attribute = symbol?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == attributeName ||
                               a.AttributeClass?.Name == attributeName.Replace("Attribute", ""));

        return attribute?.ConstructorArguments.FirstOrDefault().Value is T val ? val : default;
    }

    // Named property
    private static T? GetNamedAttributeValue<T>(this SyntaxNode node, string attributeName, string propertyName, SemanticModel semanticModel)
    {
        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);
        AttributeData? attribute = symbol?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == attributeName ||
                               a.AttributeClass?.Name == attributeName.Replace("Attribute", ""));

        KeyValuePair<string, TypedConstant>? namedArg = attribute?.NamedArguments
            .FirstOrDefault(na => na.Key == propertyName);

        return namedArg?.Value.Value is T val ? val : default;
    }
}