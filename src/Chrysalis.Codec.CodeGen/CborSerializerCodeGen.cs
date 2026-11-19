using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Codec.CodeGen;

/// <summary>
/// Incremental source generator that emits CBOR serializers and metadata
/// for types annotated with <c>[CborSerializable]</c>.
/// </summary>
[Generator]
public sealed partial class CborSerializerCodeGen : IIncrementalGenerator
{
    /// <summary>
    /// Registers syntax and compilation providers for incremental code generation.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Candidate> serializable = FindCandidates(context, Parser.CborSerializable);
        IncrementalValuesProvider<Candidate> plutus = FindCandidates(context, Parser.PlutusData);
        IncrementalValuesProvider<Candidate> candidates = serializable.Collect().Combine(plutus.Collect())
            .SelectMany(static (inputs, _) => inputs.Left.AddRange(inputs.Right));

        IncrementalValuesProvider<Emitter.GeneratedType> sources = candidates
            .Select(static (candidate, _) => candidate.Type)
            .Collect()
            .Select(static (types, _) => JoinCases(types))
            .SelectMany(static (types, _) => CreateEmissionInputs(types))
            .Select(static (input, _) => Emitter.Generate(input.Metadata, input.ShapeDependencies.Count == 0 ? null
                : input.ShapeDependencies.ToImmutableDictionary(model => NormalizeTypeName(model.FullyQualifiedName), StringComparer.Ordinal)))
            .WithTrackingName("CborSources");
        context.RegisterSourceOutput(sources, static (output, source) => source.AddSources(output));

        // A diagnostic must use the current compilation's tree, even when its source text output is cached.
        IncrementalValueProvider<ImmutableArray<(string FullyQualifiedName, Location? UnionLocation)>> locations = candidates.Where(static candidate => candidate.UnionLocation is not null)
            .Select(static (candidate, _) => (candidate.Type.Metadata.FullyQualifiedName, candidate.UnionLocation))
            .Collect();
        context.RegisterSourceOutput(sources.Where(static source => source.Diagnostic is not null).Combine(locations),
            static (output, input) => input.Left.ReportDiagnostic(output,
                input.Right.FirstOrDefault(location => location.FullyQualifiedName == input.Left.TypeName).UnionLocation));
    }

    /// <summary>
    /// Normalizes a fully-qualified type name for registry keys and lookups by stripping the
    /// <c>global::</c> prefix, nullable annotations, and surrounding whitespace.
    /// </summary>
    internal static string NormalizeTypeName(string fullyQualifiedName) =>
        fullyQualifiedName.Replace("global::", "").Replace("?", "").Trim();

    private static IncrementalValuesProvider<Candidate> FindCandidates(IncrementalGeneratorInitializationContext context, string attribute) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(attribute,
                static (node, _) => node is TypeDeclarationSyntax,
                (syntax, token) => ParseCandidate(syntax, attribute, token))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

    private static Candidate? ParseCandidate(GeneratorAttributeSyntaxContext context, string marker, CancellationToken token)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.TargetNode;
        INamedTypeSymbol symbol = (INamedTypeSymbol)context.TargetSymbol;
        // Each provider sees a declaration, but all markers and the payload belong to the symbol.
        (string? Name, SyntaxReference Syntax)[] markers = [.. symbol.GetAttributes()
            .Where(attribute => attribute.ApplicationSyntaxReference is not null)
            .Select(attribute => (Name: attribute.AttributeClass?.ToDisplayString(), Syntax: attribute.ApplicationSyntaxReference!))
            .Where(attribute => attribute.Name is Parser.CborSerializable or Parser.PlutusData)
            .OrderBy(attribute => attribute.Syntax.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Syntax.Span.Start)
            .ThenBy(attribute => attribute.Name, StringComparer.Ordinal)];
        if (markers.Length == 0 || markers[0].Name != marker || markers[0].Syntax.SyntaxTree != declaration.SyntaxTree
            || !declaration.Span.Contains(markers[0].Syntax.Span))
        {
            return null;
        }

        AttributeSyntax? plutus = markers.FirstOrDefault(attribute => attribute.Name == Parser.PlutusData).Syntax?.GetSyntax(token) as AttributeSyntax;
        TypeDeclarationSyntax[] parts = symbol.DeclaringSyntaxReferences.Length == 1 ? [declaration] :
            [.. symbol.DeclaringSyntaxReferences.OrderBy(reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                .ThenBy(reference => reference.Span.Start).Select(reference => (TypeDeclarationSyntax)reference.GetSyntax(token))];
        TypeDeclarationSyntax payload = parts.OfType<RecordDeclarationSyntax>().FirstOrDefault(part => part.ParameterList is not null) ?? declaration;
        Compilation compilation = context.SemanticModel.Compilation;
        SemanticModel model = payload.SyntaxTree == declaration.SyntaxTree ? context.SemanticModel : compilation.GetSemanticModel(payload.SyntaxTree);
        SerializableTypeMetadata metadata = Parser.ParseSerializableType(payload, model, parts, plutus);
        bool isUnion = metadata.SerializationType == SerializationType.Union;
        if (isUnion)
        {
            metadata = metadata with { UnionReader = GetUnionReaderKind(symbol, compilation) };
        }

        ParsedType parsed = new(metadata, SymbolKey(symbol), declaration.SyntaxTree.FilePath, declaration.SpanStart,
            new(ContainerKeys(symbol)), new(AncestorDefinitions(symbol)
                .Where(ancestor => SymbolEqualityComparer.Default.Equals(ancestor.ContainingAssembly, symbol.ContainingAssembly))
                .Select(SymbolKey)));
        return new(parsed, isUnion ? declaration.Identifier.GetLocation() : null);
    }

    private static UnionReaderKind GetUnionReaderKind(INamedTypeSymbol symbol, Compilation compilation)
    {
        ImmutableArray<ISymbol> members = symbol.GetMembers("Read");
        if (members.IsEmpty)
        {
            return UnionReaderKind.Generated;
        }

        ITypeSymbol byteType = compilation.GetSpecialType(SpecialType.System_Byte);
        INamedTypeSymbol? memory = compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1")?.Construct(byteType);
        INamedTypeSymbol? span = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1")?.Construct(byteType);
        UnionReaderKind kind = UnionReaderKind.Generated;
        foreach (ISymbol member in members)
        {
            if (member is not IMethodSymbol method || method.Parameters.Length is not (1 or 2))
            {
                continue;
            }
            IParameterSymbol input = method.Parameters[0];
            bool inputIsMemory = SymbolEqualityComparer.Default.Equals(input.Type, memory);
            bool byValueMemory = inputIsMemory && input.RefKind == RefKind.None;
            if (method.Parameters.Length == 1)
            {
                // The generator owns the convenience reader, even when the length overload is declared.
                if (byValueMemory && method.Arity == 0)
                {
                    return UnionReaderKind.Invalid;
                }
                continue;
            }

            IParameterSymbol count = method.Parameters[1];
            bool swapped = SymbolEqualityComparer.Default.Equals(count.Type, memory)
                && (input.RefKind != RefKind.None || IsCountType(input.Type.SpecialType));
            bool spanWithCount = count.RefKind != RefKind.None && SymbolEqualityComparer.Default.Equals(input.Type, span);
            if (!inputIsMemory && !swapped && !spanWithCount)
            {
                continue;
            }
            // By-value context, including an int era, does not occupy the consumed-length slot.
            if (byValueMemory && count.RefKind == RefKind.None
                && (!IsCountType(count.Type.SpecialType) || count.Type.SpecialType == SpecialType.System_Int32))
            {
                continue;
            }
            if (!byValueMemory || count.RefKind != RefKind.Out || count.Type.SpecialType != SpecialType.System_Int32
                || !method.IsStatic || method.IsAbstract || method.IsVirtual || method.Arity != 0
                || method.DeclaredAccessibility != Accessibility.Public || method.RefKind != RefKind.None
                || method.ReturnNullableAnnotation == NullableAnnotation.Annotated
                || !SymbolEqualityComparer.Default.Equals(method.ReturnType, symbol))
            {
                return UnionReaderKind.Invalid;
            }
            kind = UnionReaderKind.Declared;
        }
        return kind;
    }

    private static bool IsCountType(SpecialType type) => type is SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    // Only source-assembly ancestors can be union candidates. Use original metadata names
    // within that assembly (including generic arity and file-local identity), not display names.
    private static string SymbolKey(INamedTypeSymbol symbol)
    {
        INamedTypeSymbol definition = symbol.OriginalDefinition;
        return definition.ContainingType is { } parent ? SymbolKey(parent) + "+" + definition.MetadataName
            : definition.ContainingNamespace.IsGlobalNamespace ? definition.MetadataName
            : definition.ContainingNamespace.ToDisplayString() + "." + definition.MetadataName;
    }

    private static IEnumerable<string> ContainerKeys(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? parent = symbol.ContainingType; parent is not null; parent = parent.ContainingType)
        {
            yield return SymbolKey(parent);
        }
    }

    private static IEnumerable<INamedTypeSymbol> AncestorDefinitions(INamedTypeSymbol symbol)
    {
        if (symbol.BaseType is null)
        {
            yield break;
        }

        foreach (INamedTypeSymbol iface in symbol.AllInterfaces)
        {
            yield return iface.OriginalDefinition;
        }
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            yield return current.OriginalDefinition;
        }
    }

    private static IOrderedEnumerable<ParsedType> OrderByDeclaration(IEnumerable<ParsedType> types) => types
        .OrderBy(type => type.Path, StringComparer.Ordinal).ThenBy(type => type.Start).ThenBy(type => type.SymbolName, StringComparer.Ordinal);

    private static EquatableArray<SerializableTypeMetadata> JoinCases(ImmutableArray<ParsedType> types)
    {
        // Distinct symbols can still share an emitted name, e.g. file-local types or incomplete edits.
        ParsedType[] unique = [.. types.GroupBy(type => NormalizeTypeName(type.Metadata.FullyQualifiedName), StringComparer.Ordinal)
            .Select(group => OrderByDeclaration(group).First())];
        ILookup<string, ParsedType> nested = unique.SelectMany(type => type.Containers.Select(parent => (parent, type)))
            .ToLookup(entry => entry.parent, entry => entry.type, StringComparer.Ordinal);
        ILookup<string, ParsedType> derived = unique.SelectMany(type => type.Ancestors.Select(parent => (parent, type)))
            .ToLookup(entry => entry.parent, entry => entry.type, StringComparer.Ordinal);
        Dictionary<ParsedType, SerializableTypeMetadata> resolved = [];
        HashSet<ParsedType> visiting = [];
        return new(unique.Select(Resolve));

        SerializableTypeMetadata Resolve(ParsedType type)
        {
            if (type.Metadata.SerializationType != SerializationType.Union || !visiting.Add(type))
            {
                return type.Metadata;
            }
            if (!resolved.TryGetValue(type, out SerializableTypeMetadata metadata))
            {
                HashSet<string> seen = [];
                metadata = type.Metadata with
                {
                    ChildTypes = new(OrderByDeclaration(nested[type.SymbolName]).Concat(OrderByDeclaration(derived[type.SymbolName]))
                        .Where(candidate => seen.Add(candidate.Metadata.FullyQualifiedName)).Select(Resolve))
                };
                resolved.Add(type, metadata);
            }
            _ = visiting.Remove(type);
            return metadata;
        }
    }

    private static ImmutableArray<EmissionInput> CreateEmissionInputs(EquatableArray<SerializableTypeMetadata> models)
    {
        ImmutableDictionary<string, SerializableTypeMetadata> registry = models.ToImmutableDictionary(
            model => NormalizeTypeName(model.FullyQualifiedName), StringComparer.Ordinal);
        return [.. models.Select(model => new EmissionInput(model, model.SerializationType == SerializationType.Union && model.UnionReader == UnionReaderKind.Generated
            ? UnionEmitter.GetShapeDependencies(model, registry) : default))];
    }

    private sealed record Candidate(ParsedType Type, Location? UnionLocation);
    private sealed record ParsedType(SerializableTypeMetadata Metadata, string SymbolName, string Path, int Start,
        EquatableArray<string> Containers, EquatableArray<string> Ancestors);
    private sealed record EmissionInput(SerializableTypeMetadata Metadata, EquatableArray<SerializableTypeMetadata> ShapeDependencies);
}
