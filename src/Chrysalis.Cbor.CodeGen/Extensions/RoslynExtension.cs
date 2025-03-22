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
}