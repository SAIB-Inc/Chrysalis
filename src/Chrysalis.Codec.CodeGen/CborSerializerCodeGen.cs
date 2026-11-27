using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        // A Plutus data field whose byte strings sit inside a generic type is written without chunks.
        context.RegisterSourceOutput(candidates.Where(static candidate => candidate.UnchunkedBytes.Count > 0), static (output, candidate) =>
        {
            foreach ((Location location, string field) in candidate.UnchunkedBytes)
            {
                output.ReportDiagnostic(Diagnostic.Create(UnchunkedPlutusBytes, location, field));
            }
        });
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

        TypeDeclarationSyntax[] parts = symbol.DeclaringSyntaxReferences.Length == 1 ? [declaration] :
            [.. symbol.DeclaringSyntaxReferences.OrderBy(reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                .ThenBy(reference => reference.Span.Start).Select(reference => (TypeDeclarationSyntax)reference.GetSyntax(token))];
        TypeDeclarationSyntax payload = parts.OfType<RecordDeclarationSyntax>().FirstOrDefault(part => part.ParameterList is not null) ?? declaration;
        Compilation compilation = context.SemanticModel.Compilation;
        SemanticModel model = payload.SyntaxTree == declaration.SyntaxTree ? context.SemanticModel : compilation.GetSemanticModel(payload.SyntaxTree);
        SerializableTypeMetadata metadata = Parser.ParseSerializableType(payload, model, parts);

        ImmutableArray<AttributeData> symbolAttrs = symbol.GetAttributes();
        AttributeData? cborUnionAttr = symbolAttrs.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == Parser.CborUnionFullName);
        bool isUnion = cborUnionAttr is not null || metadata.SerializationType == SerializationType.Union;

        if (symbol.TypeKind == TypeKind.Struct && cborUnionAttr is not null)
        {
            bool hasUnionAttr = symbolAttrs.Any(a => a.AttributeClass?.ToDisplayString() == Parser.UnionAttributeFullName);
            bool implementsICborType = symbol.AllInterfaces.Any(i => i.ToDisplayString() == Parser.ICborTypeFullName);
            bool hasConflict = symbolAttrs.Any(a => a.AttributeClass?.ToDisplayString() is
                Parser.CborMapFullName or Parser.CborListFullName or Parser.CborConstrFullName or Parser.PlutusData);
            bool allPartial = parts.All(p => p.Modifiers.Any(SyntaxKind.PartialKeyword));
            bool validDecl = symbol.IsReadOnly && !symbol.IsRecord && !symbol.IsRefLikeType
                && symbol.ContainingType is null && symbol.DeclaredAccessibility == Accessibility.Public
                && allPartial && hasUnionAttr && implementsICborType && !hasConflict;
            if (!validDecl)
            {
                metadata = metadata with { UnionError = InvalidOfficialUnionDeclaration };
            }
            else if (!TryParseStructUnionCases(symbol, cborUnionAttr, compilation,
                out DiagnosticDescriptor? caseError, out string? caseErrorDetail, out EquatableArray<StructUnionCaseRef> caseRefs))
            {
                metadata = metadata with { UnionError = caseError, UnionErrorDetail = caseErrorDetail };
            }
            else
            {
                metadata = metadata with
                {
                    StructUnionCases = caseRefs,
                    UnionReader = GetUnionReaderKind(symbol, compilation)
                };
            }
        }
        else if (cborUnionAttr is not null)
        {
            bool hasExplicitArgs = cborUnionAttr.ConstructorArguments.Any(arg =>
                arg.Kind == TypedConstantKind.Array && !arg.Values.IsDefaultOrEmpty);
            metadata = hasExplicitArgs
                ? metadata with { UnionError = InvalidOfficialUnionDeclaration }
                : metadata with { UnionReader = GetUnionReaderKind(symbol, compilation) };
        }
        else if (isUnion)
        {
            metadata = metadata with { UnionReader = GetUnionReaderKind(symbol, compilation) };
        }

        ParsedType parsed = new(metadata, SymbolKey(symbol), declaration.SyntaxTree.FilePath, declaration.SpanStart,
            new(ContainerKeys(symbol)), new(AncestorDefinitions(symbol)
                .Where(ancestor => SymbolEqualityComparer.Default.Equals(ancestor.ContainingAssembly, symbol.ContainingAssembly))
                .Select(SymbolKey)));
        IEnumerable<SerializablePropertyMetadata> unchunked = metadata.UnionError is null && metadata.SerializationType == SerializationType.Constr
            ? metadata.Properties.Where(Parser.HidesUnchunkedBytes)
            : [];
        Location? unionLocation = (isUnion || metadata.UnionError is not null) ? declaration.Identifier.GetLocation() : null;
        return new(parsed, unionLocation, new(unchunked.Select(property =>
            (symbol.GetMembers(property.PropertyName).FirstOrDefault()?.Locations.FirstOrDefault() ?? declaration.Identifier.GetLocation(), property.PropertyName))));
    }

    private static bool TryParseStructUnionCases(
        INamedTypeSymbol unionSymbol,
        AttributeData cborUnionAttr,
        Compilation compilation,
        out DiagnosticDescriptor? error,
        out string? errorDetail,
        out EquatableArray<StructUnionCaseRef> caseRefs)
    {
        error = null;
        errorDetail = null;
        caseRefs = default;

        ImmutableArray<TypedConstant> cases = cborUnionAttr.ConstructorArguments.Length > 0
            && cborUnionAttr.ConstructorArguments[0].Kind == TypedConstantKind.Array
            ? cborUnionAttr.ConstructorArguments[0].Values
            : [];

        if (cases.IsDefaultOrEmpty) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseList, "at least one case type is required"); }
        if (cases.Length > 255) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseList, "at most 255 case types are supported"); }

        HashSet<INamedTypeSymbol> seen = [with(SymbolEqualityComparer.Default)];
        List<StructUnionCaseRef> collected = [with(cases.Length)];

        foreach (TypedConstant item in cases)
        {
            if (item.Value is not INamedTypeSymbol caseSymbol || HasErrorType(caseSymbol)) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseList, "case type could not be resolved"); }

            INamedTypeSymbol boundSymbol;
            if (caseSymbol.IsUnboundGenericType)
            {
                if (!unionSymbol.IsGenericType || caseSymbol.Arity != unionSymbol.Arity)
                { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseList, $"open generic case '{caseSymbol.ToDisplayString()}' does not match union arity"); }
                boundSymbol = caseSymbol.OriginalDefinition.Construct([.. unionSymbol.TypeParameters]);
            }
            else
            {
                boundSymbol = caseSymbol;
            }

            if (SymbolEqualityComparer.Default.Equals(boundSymbol, unionSymbol)) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseList, "union cannot include itself as a case"); }
            if (!seen.Add(boundSymbol))
            { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseList, $"duplicate case type '{boundSymbol.ToDisplayString()}'"); }

            string displayName = boundSymbol.ToDisplayString();
            if (boundSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct) || boundSymbol.IsAbstract || boundSymbol.IsStatic || boundSymbol.IsRefLikeType
                || Parser.HasCborUnionAttribute(boundSymbol)
                || boundSymbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == Parser.UnionAttributeFullName)
                || !boundSymbol.AllInterfaces.Any(i => i.ToDisplayString() == Parser.ICborTypeFullName)) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseType, displayName); }

            bool hasCodegenAttr = boundSymbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() is Parser.CborSerializable or Parser.PlutusData);
            bool isConstructedOrExternal = !SymbolEqualityComparer.Default.Equals(boundSymbol, boundSymbol.OriginalDefinition)
                || !SymbolEqualityComparer.Default.Equals(boundSymbol.ContainingAssembly, unionSymbol.ContainingAssembly);
            bool hasManualReadWrite = HasPublicStaticReadWrite(boundSymbol, compilation);

            SerializableTypeMetadata? boundMetadata = null;
            if (!hasCodegenAttr || isConstructedOrExternal)
            {
                boundMetadata = Parser.ParseSymbolWireShape(boundSymbol, compilation);
            }
            else if (boundSymbol.GetAttributes().All(a =>
                a.AttributeClass?.ToDisplayString() is not (Parser.CborMapFullName or Parser.CborListFullName or Parser.CborConstrFullName or Parser.PlutusData)))
            {
                SerializableTypeMetadata localContainer = Parser.ParseSymbolWireShape(boundSymbol, compilation);
                if (localContainer.SerializationType == SerializationType.Container && localContainer.Properties.Count == 0 && !hasManualReadWrite) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseType, displayName); }
            }

            if (boundMetadata?.HasAmbiguousExternalWireOrder == true
                || ((!hasCodegenAttr || (boundMetadata is { SerializationType: SerializationType.Container } && boundMetadata.Properties.Count == 0))
                    && !hasManualReadWrite)) { return Fail(out error, out errorDetail, InvalidOfficialUnionCaseType, displayName); }

            string boundFullName = boundSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string boundIdentifier = boundSymbol.TypeArguments.Length == 0
                ? boundSymbol.Name
                : $"{boundSymbol.Name}<{string.Join(", ", boundSymbol.TypeArguments.Select(ta => ta.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))}>";
            collected.Add(new StructUnionCaseRef(SymbolKey(boundSymbol), boundFullName, boundIdentifier, boundSymbol.Name, boundSymbol.IsValueType, boundMetadata));
        }

        caseRefs = new(collected);
        return true;
    }

    private static bool HasErrorType(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Error
        || (type is INamedTypeSymbol named && !named.IsUnboundGenericType
            && (named.TypeArguments.Any(HasErrorType) || (named.ContainingType is { } containing && HasErrorType(containing))))
        || (type is IArrayTypeSymbol arr && HasErrorType(arr.ElementType));

    private static bool Fail(out DiagnosticDescriptor? error, out string? errorDetail, DiagnosticDescriptor descriptor, string detail)
    {
        error = descriptor;
        errorDetail = detail;
        return false;
    }

    private static bool HasPublicStaticReadWrite(INamedTypeSymbol caseSymbol, Compilation compilation)
    {
        ITypeSymbol byteType = compilation.GetSpecialType(SpecialType.System_Byte);
        INamedTypeSymbol? memory = compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1")?.Construct(byteType);
        INamedTypeSymbol? writer = compilation.GetTypeByMetadataName("System.Buffers.IBufferWriter`1")?.Construct(byteType);
        if (memory is null || writer is null) { return false; }

        bool hasRead = caseSymbol.GetMembers("Read").OfType<IMethodSymbol>().Any(m =>
            m.IsStatic && !m.IsAbstract && !m.IsVirtual && m.DeclaredAccessibility == Accessibility.Public && m.Arity == 0
            && m.Parameters.Length == 2 && m.ReturnNullableAnnotation != NullableAnnotation.Annotated
            && SymbolEqualityComparer.Default.Equals(m.ReturnType, caseSymbol)
            && m.Parameters[0].RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, memory)
            && m.Parameters[1].RefKind == RefKind.Out && m.Parameters[1].Type.SpecialType == SpecialType.System_Int32);

        bool hasWrite = caseSymbol.GetMembers("Write").OfType<IMethodSymbol>().Any(m =>
            m.IsStatic && !m.IsAbstract && !m.IsVirtual && m.DeclaredAccessibility == Accessibility.Public && m.Arity == 0
            && m.ReturnsVoid && m.Parameters.Length == 2
            && m.Parameters[0].RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, writer)
            && m.Parameters[1].RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, caseSymbol));

        return hasRead && hasWrite;
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
        Dictionary<string, ParsedType> bySymbolKey = [];
        foreach (ParsedType item in OrderByDeclaration(unique))
        {
            if (!bySymbolKey.ContainsKey(item.SymbolName))
            {
                bySymbolKey[item.SymbolName] = item;
            }
        }
        ILookup<string, ParsedType> nested = unique.SelectMany(type => type.Containers.Select(parent => (parent, type)))
            .ToLookup(entry => entry.parent, entry => entry.type, StringComparer.Ordinal);
        ILookup<string, ParsedType> derived = unique.SelectMany(type => type.Ancestors.Select(parent => (parent, type)))
            .ToLookup(entry => entry.parent, entry => entry.type, StringComparer.Ordinal);
        Dictionary<ParsedType, SerializableTypeMetadata> resolved = [];
        HashSet<ParsedType> visiting = [];
        return new(unique.Select(Resolve));

        SerializableTypeMetadata Resolve(ParsedType type)
        {
            if (type.Metadata.SerializationType != SerializationType.Union || type.Metadata.UnionError is not null || !visiting.Add(type))
            {
                return type.Metadata;
            }
            if (!resolved.TryGetValue(type, out SerializableTypeMetadata metadata))
            {
                if (type.Metadata.IsStructUnion)
                {
                    List<SerializableTypeMetadata> cases = [with(type.Metadata.StructUnionCases.Count)];
                    foreach (StructUnionCaseRef caseRef in type.Metadata.StructUnionCases)
                    {
                        SerializableTypeMetadata? caseMeta = caseRef.BoundMetadata;
                        if (caseMeta is null && bySymbolKey.TryGetValue(caseRef.SymbolKey, out ParsedType? found))
                        {
                            caseMeta = found.Metadata;
                        }
                        if (caseMeta is null)
                        {
                            _ = visiting.Remove(type);
                            return type.Metadata with { UnionError = InvalidOfficialUnionCaseType, UnionErrorDetail = caseRef.BoundIdentifier };
                        }
                        cases.Add(caseMeta with
                        {
                            FullyQualifiedName = caseRef.BoundTypeFullName,
                            Indentifier = caseRef.BoundIdentifier,
                            BaseIdentifier = caseRef.BaseIdentifier,
                            IsValueType = caseRef.IsValueType
                        });
                    }
                    metadata = type.Metadata with { ChildTypes = new(cases) };
                }
                else
                {
                    HashSet<string> seen = [];
                    metadata = type.Metadata with
                    {
                        ChildTypes = new(OrderByDeclaration(nested[type.SymbolName]).Concat(OrderByDeclaration(derived[type.SymbolName]))
                            .Where(candidate => seen.Add(candidate.Metadata.FullyQualifiedName)).Select(Resolve))
                    };
                }
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
        return [.. models.Select(model => new EmissionInput(model,
            model.UnionError is null && model.SerializationType == SerializationType.Union && model.UnionReader == UnionReaderKind.Generated
                ? UnionEmitter.GetShapeDependencies(model, registry) : default))];
    }

    private sealed record Candidate(ParsedType Type, Location? UnionLocation, EquatableArray<(Location Location, string Field)> UnchunkedBytes);
    private sealed record ParsedType(SerializableTypeMetadata Metadata, string SymbolName, string Path, int Start,
        EquatableArray<string> Containers, EquatableArray<string> Ancestors);
    private sealed record EmissionInput(SerializableTypeMetadata Metadata, EquatableArray<SerializableTypeMetadata> ShapeDependencies);
}
