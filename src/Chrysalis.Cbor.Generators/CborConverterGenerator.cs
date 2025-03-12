using System.Collections.Immutable;
using Chrysalis.Cbor.Generators.Utils;
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
                TypeDeclarationSyntax syntax = (TypeDeclarationSyntax)ctx.Node;
                SemanticModel semanticModel = ctx.SemanticModel;

                if (semanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol) return null;

                bool implementsIConverter = symbol.Interfaces.Any(i => i.ToDisplayString() == "Chrysalis.Cbor.Serialization.ICborConverter");
                return implementsIConverter ? syntax : null;
            }
        ).Where(m => m is not null)!;

        IncrementalValuesProvider<TypeDeclarationSyntax> cborBaseProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax || node is RecordDeclarationSyntax,
            transform: static (ctx, _) =>
            {
                TypeDeclarationSyntax syntax = (TypeDeclarationSyntax)ctx.Node;
                SemanticModel semanticModel = ctx.SemanticModel;

                if (semanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol) return null;

                INamedTypeSymbol? baseType = symbol.BaseType;
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

        IncrementalValueProvider<(Compilation Left, (ImmutableArray<TypeDeclarationSyntax> Left, ImmutableArray<TypeDeclarationSyntax> Right) Right)> compilation = context.CompilationProvider.Combine(converterProvider.Collect().Combine(cborBaseProvider.Collect()));

        context.RegisterSourceOutput(compilation, GenerateCborConverter!);
    }

    private void GenerateCborConverter(SourceProductionContext ctx, (Compilation Compilation, (ImmutableArray<TypeDeclarationSyntax> Converters, ImmutableArray<TypeDeclarationSyntax> Types)) tuple)
    {
        (Compilation compilation, (ImmutableArray<TypeDeclarationSyntax> converters, ImmutableArray<TypeDeclarationSyntax> types)) = tuple;

        // Type -> Converter mapping
        Dictionary<string, TypeDeclarationSyntax> convertersByName = converters
            .ToDictionary(
                c => compilation.GetSemanticModel(c.SyntaxTree).GetDeclaredSymbol(c)?.ToDisplayString() ?? string.Empty,
                c => c
            );

        // Type -> Union Types mapping
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> allUnionTypes = UnionTypeResolver.ResolveAllUnionTypes(types, compilation);

        foreach (TypeDeclarationSyntax typeSyntax in types)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(typeSyntax.SyntaxTree);

            if (semanticModel.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol typeSymbol) continue;

            // Check if already inherits from CborBase to avoid redundant inheritance
            INamedTypeSymbol? baseType = typeSymbol.BaseType;
            List<string> interfaces = [.. typeSymbol.Interfaces.Select(i => i.ToDisplayString())];

            // Normalized typpe
            // 1. Get normalized type
            string normalizedTypeName;
            if (typeSymbol.IsGenericType)
            {
                INamedTypeSymbol unboundType = typeSymbol.ConstructUnboundGenericType();
                normalizedTypeName = unboundType.ToDisplayString();
            }
            else
            {
                normalizedTypeName = typeSymbol.ToDisplayString();
            }

            // 2. Get the CBOR options attribute and extract values
            int index = -1;
            bool isDefinite = false;
            long tag = -1;
            int size = -1;

            var optionsAttr = typeSymbol
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "CborOptionsAttribute" ||
                        a.AttributeClass?.ToDisplayString() == "Chrysalis.Cbor.Attributes.CborOptionsAttribute");

            if (optionsAttr != null)
            {
                // Extract values from named arguments
                foreach (var arg in optionsAttr.NamedArguments)
                {
                    if (arg.Key == "Index" && arg.Value.Value is int i)
                        index = i;
                    else if (arg.Key == "IsDefinite" && arg.Value.Value is bool d)
                        isDefinite = d;
                    else if (arg.Key == "Tag" && arg.Value.Value is long t)
                        tag = t;
                    else if (arg.Key == "Size" && arg.Value.Value is int s)
                        size = s;
                }

                // Check constructor arguments as well
                for (int i = 0; i < optionsAttr.ConstructorArguments.Length; i++)
                {
                    var arg = optionsAttr.ConstructorArguments[i];
                    if (i == 0 && arg.Value is int idx)
                        index = idx;
                    else if (i == 1 && arg.Value is bool def)
                        isDefinite = def;
                    else if (i == 2 && arg.Value is long t)
                        tag = t;
                    else if (i == 3 && arg.Value is int s)
                        size = s;
                }
            }


            // Runtime type
            string runtimeTypeName = typeSymbol.ToDisplayString();

            // Add all namespaces from base types and interfaces
            HashSet<string> additionalNamespaces = [];
            if (typeSymbol.BaseType != null && !string.IsNullOrEmpty(typeSymbol.BaseType.ContainingNamespace?.ToDisplayString()))
            {
                additionalNamespaces.Add($"using {typeSymbol.BaseType.ContainingNamespace!.ToDisplayString()};");
            }

            foreach (INamedTypeSymbol iface in typeSymbol.Interfaces)
            {
                if (!string.IsNullOrEmpty(iface.ContainingNamespace?.ToDisplayString()))
                {
                    additionalNamespaces.Add($"using {iface.ContainingNamespace!.ToDisplayString()};");
                }
            }

            AttributeData? converterAttribute = typeSymbol
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
            if (!convertersByName.TryGetValue(converterTypeName, out TypeDeclarationSyntax? converterSyntax)) continue;

            // Extract Read and Write method implementations
            MethodDeclarationSyntax readMethod = converterSyntax.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "Read");

            MethodDeclarationSyntax writeMethod = converterSyntax.Members.OfType<MethodDeclarationSyntax>()
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
            SyntaxTree converterSyntaxTree = converterSyntax.SyntaxTree;
            SyntaxNode converterRoot = converterSyntaxTree.GetRoot();
            List<string> usingDirectives = converterRoot.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .ToList();

            usingDirectives.Add("using Chrysalis.Cbor.Serialization;");
            usingDirectives.Add("using Chrysalis.Cbor.Types;");
            usingDirectives.Add("using System;");
            usingDirectives.Add("using System.Collections.Generic;");
            usingDirectives.Add("using System.Formats.Cbor;");
            usingDirectives.Add("using Chrysalis.Cbor.Utils;");
            usingDirectives.AddRange(additionalNamespaces);

            string typeName = typeSymbol.Name;
            string namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
            string typeSyntaxString = typeSyntax is ClassDeclarationSyntax
                ? "class"
                : "record";

            string typeParameters = "";
            if (typeSymbol.IsGenericType)
            {
                IEnumerable<string> typeParams = typeSymbol.TypeParameters.Select(p => p.Name);
                typeParameters = $"<{string.Join(", ", typeParams)}>";
            }

            bool isUnionConverter = converterTypeSymbol.Name == "UnionConverter" ||
                          converterTypeSymbol.ToDisplayString() == "Chrysalis.Cbor.Serialization.Converters.Custom.UnionConverter";

            // Get union types if applicable (this would come from our pre-computed dictionary)
            List<string> unionTypeNames = [];
            if (isUnionConverter)
            {
                // Use TryGetValue instead of GetValueOrDefault
                if (allUnionTypes.TryGetValue(typeSymbol, out var unionTypes))
                {
                    unionTypeNames = unionTypes.Select(t => t.ToDisplayString()).ToList();
                }
            }

            var propertyMappings = PropertyResolver.ResolvePropertyMappings(typeSymbol);

            string optionsInitCode = GenerateCborOptionsCode(
                    normalizedTypeName,
                    runtimeTypeName,
                    (index, isDefinite, tag, size),
                    converterTypeName,
                    unionTypeNames,
                    propertyMappings,
                    typeSymbol
                );

            string unionTypesCode = GenerateUnionTypesCode(typeSymbol, unionTypeNames);

            // CborOptions
            // Type normalizedType = typeSymbol.IsGenericType ? typeSymbol.GetGenericTypeDefinition() : typeSymbol;
            // Type normalizedType = type.NormalizeType();
            //     CborOptionsAttribute? optionsAttr = type.GetCustomAttribute<CborOptionsAttribute>();
            //     CborConverterAttribute? converterTypeAttr = type.GetCustomAttribute<CborConverterAttribute>();
            //     IReadOnlyCollection<Type>? unionTypes = UnionResolver.ResolveUnionTypes(type, converterTypeAttr?.ConverterType);
            //     (IReadOnlyDictionary<int, (Type Type, object? ExpectedValue)> IndexMap, IReadOnlyDictionary<string, (Type Type, object? ExpectedValue)> NamedMap, ConstructorInfo Constructor) = PropertyResolver.ResolvePropertyMappings(type);

            string converterCode = $$"""
                #nullable enable
                // This code is generated
                {{string.Join("\n", usingDirectives.Distinct())}}

                namespace {{namespaceName}};
                
                public partial {{typeSyntaxString}} {{typeName}}{{typeParameters}}
                {
                    public new static object? Read(CborReader reader)
                    {
                        {{optionsInitCode}}
                        CborUtil.ReadAndVerifyTag(reader, {{tag}});
                        {{readBody}}
                    }
                    
                    public override void Write(CborWriter writer, List<object?> value)
                    {
                        {{optionsInitCode}}
                        CborUtil.WriteTag(writer, {{tag}});
                        {{writeBody}}
                    }
                }
            """;

            ctx.AddSource($"{namespaceName}.{typeName}.g.cs", converterCode);
        }
    }

    // Helper method to generate CborOptions initialization code
    private string GenerateCborOptionsCode(
    string normalizedTypeName,
    string runtimeTypeName,
    (int Index, bool IsDefinite, long Tag, int Size) options,
    string converterTypeName,
    List<string> unionTypeNames,
    (
        Dictionary<int, (string Type, string Name, string? DefaultValue)> IndexMap,
        Dictionary<string, (string Type, string Name, string? DefaultValue)> NamedMap,
        IMethodSymbol? Constructor
    ) propertyMappings,
    INamedTypeSymbol typeSymbol)
    {
        // Generate code for index mappings
        string indexMapCode = GenerateIndexMapCode(propertyMappings.IndexMap);

        // Generate code for named mappings
        string namedMapCode = GenerateNamedMapCode(propertyMappings.NamedMap);

        // Generate code for union types with special handling for generic types
        string unionTypesCode = GenerateUnionTypesCode(typeSymbol, unionTypeNames);

        // Generate constructor reference
        string constructorCode = propertyMappings.Constructor != null
            ? $"typeof({runtimeTypeName}).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First()"
            : "null";

        // Generate runtime type reference - handle nullable types
        string runtimeTypeRef = $"typeof({runtimeTypeName})";

        // Generate options initialization
        return $@"var options = new CborOptions(
        index: {options.Index},
        isDefinite: {options.IsDefinite.ToString().ToLowerInvariant()},
        tag: {options.Tag},
        size: {options.Size},
        objectType: {runtimeTypeRef},
        normalizedType: {runtimeTypeRef},
        converterType: typeof({converterTypeName}),
        indexPropertyMapping: {indexMapCode},
        namedPropertyMapping: {namedMapCode},
        unionTypes: {unionTypesCode},
        constructor: {constructorCode}
    );";
    }

    //Helper method to generate index map code - fixed for nullable types
    private string GenerateIndexMapCode(Dictionary<int, (string Type, string Name, string? DefaultValue)> indexMap)
    {
        if (indexMap.Count == 0)
            return "new Dictionary<int, (Type Type, object? ExpectedValue)>()";

        var entries = new List<string>();

        foreach (var entry in indexMap)
        {
            string defaultValue = entry.Value.DefaultValue ?? "null";

            // Handle nullable types by removing the '?' suffix for typeof
            string typeForTypeof = entry.Value.Type;
            if (typeForTypeof.EndsWith("?"))
            {
                typeForTypeof = typeForTypeof.Substring(0, typeForTypeof.Length - 1);
            }

            entries.Add($"{{ {entry.Key}, (typeof({typeForTypeof}), {defaultValue}) }}");
        }

        return $"new Dictionary<int, (Type Type, object? ExpectedValue)> {{\n            {string.Join(",\n            ", entries)}\n        }}";
    }

    // Helper method to generate named map code - fixed for nullable types
    private string GenerateNamedMapCode(Dictionary<string, (string Type, string Name, string? DefaultValue)> namedMap)
    {
        if (namedMap.Count == 0)
            return "new Dictionary<string, (Type Type, object? ExpectedValue)>()";

        var entries = new List<string>();

        foreach (var entry in namedMap)
        {
            string defaultValue = entry.Value.DefaultValue ?? "null";

            // Handle nullable types by removing the '?' suffix for typeof
            string typeForTypeof = entry.Value.Type;
            if (typeForTypeof.EndsWith("?"))
            {
                typeForTypeof = typeForTypeof.Substring(0, typeForTypeof.Length - 1);
            }

            entries.Add($"{{ \"{entry.Key}\", (typeof({typeForTypeof}), {defaultValue}) }}");
        }

        return $"new Dictionary<string, (Type Type, object? ExpectedValue)> {{\n            {string.Join(",\n            ", entries)}\n        }}";
    }

    // Helper method to handle union types with proper generic constraints
    private string GenerateUnionTypesCode(
        INamedTypeSymbol typeSymbol,
        List<string> unionTypeNames)
    {
        if (unionTypeNames.Count == 0)
            return "null";

        // For generic types with union types, we need special handling
        if (typeSymbol.IsGenericType)
        {
            // For generic types, we need to use the open type version
            var typeArguments = string.Join(", ", typeSymbol.TypeParameters.Select(p => p.Name));

            var unionTypes = new List<string>();
            foreach (var unionTypeName in unionTypeNames)
            {
                // If the union type contains generic parameters, use the open type version
                if (unionTypeName.Contains("<"))
                {
                    int genericStartIndex = unionTypeName.IndexOf('<');
                    int genericEndIndex = unionTypeName.LastIndexOf('>');

                    if (genericStartIndex > 0 && genericEndIndex > genericStartIndex)
                    {
                        // Get the base name without generic parameters
                        string baseTypeName = unionTypeName.Substring(0, genericStartIndex);
                        // Add the open generic marker
                        unionTypes.Add($"typeof({baseTypeName}<>)");
                    }
                    else
                    {
                        // Fallback if parsing fails
                        unionTypes.Add($"typeof({unionTypeName})");
                    }
                }
                else
                {
                    // Non-generic union type
                    unionTypes.Add($"typeof({unionTypeName})");
                }
            }

            return $"new[] {{ {string.Join(", ", unionTypes)} }}";
        }
        else
        {
            // For non-generic types, use the normal approach
            return $"new[] {{ {string.Join(", ", unionTypeNames.Select(t => $"typeof({t})"))} }}";
        }
    }

}
