using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.CodeGen.Extensions;

/// <summary>
/// Extension methods for Roslyn syntax types used during code generation.
/// </summary>
public static class RoslynExtension
{
    /// <summary>
    /// Gets the namespace of the given type declaration syntax node.
    /// </summary>
    /// <param name="self">The type declaration syntax node.</param>
    /// <returns>The namespace string, or null if none found.</returns>
    public static string? GetNamespace(this TypeDeclarationSyntax self)
    {
        if (self is null)
        {
            throw new ArgumentNullException(nameof(self));
        }

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
}
