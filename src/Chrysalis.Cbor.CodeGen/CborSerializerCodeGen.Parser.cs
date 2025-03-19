using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.CodeGen;


public sealed partial class CborSerializerCodeGen
{
    private sealed class Parser(SourceProductionContext context)
    {
        public const string CborSerializableAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute";
        public const string CborSerializableAttribute = "CborSerializableAttribute";
        public const string CborMapAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborMapAttribute";
        public const string CborMapAttribute = "CborMapAttribute";
        public const string CborNullableAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborNullableAttribute";
        public const string CborNullableAttribute = "CborNullableAttribute";
        public const string CborListAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborListAttribute";
        public const string CborListAttribute = "CborListAttribute";
        public const string CborUnionAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborUnionAttribute";
        public const string CborUnionAttribute = "CborUnionAttribute";
        public const string CborConstrAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborConstrAttribute";
        public const string CborConstrAttribute = "CborConstrAttribute";
        public const string CborTagAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborTagAttribute";
        public const string CborTagAttribute = "CborTagAttribute";
        public const string CborOrderAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborOrderAttribute";
        public const string CborOrderAttribute = "CborOrderAttribute";
        public const string CborPropertyAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborPropertyAttribute";
        public const string CborPropertyAttribute = "CborPropertyAttribute";
        public const string CborSizeAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborSizeAttribute";
        public const string CborSizeAttribute = "CborSizeAttribute";
        public const string CborIndefiniteAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborIndefiniteAttribute";
        public const string CborIndefiniteAttribute = "CborIndefiniteAttribute";
        public const string CborValidateExactAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborValidateExactAttribute";
        public const string CborValidateExactAttribute = "CborValidateExactAttribute";
        public const string CborValidateRangeAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborValidateRangeAttribute";
        public const string CborValidateRangeAttribute = "CborValidateRangeAttribute";
        public const string CborValidateAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborValidateAttribute";
        public const string CborValidateAttribute = "CborValidateAttribute";

        private readonly SourceProductionContext _context = context;

        public SerializableTypeMetadata? ParseSerialazableType(TypeDeclarationSyntax tds, SemanticModel model)
        {
            try
            {
                // Placeholder for actual logic that generates the metadata.
                // We'll log diagnostics at different stages

                // Example diagnostics to inspect the process:
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CBOR001", "Parser Started", $"Parsing {tds.Identifier.Text}...", "info", DiagnosticSeverity.Info, true),
                    tds.GetLocation()));

                if (tds.AttributeLists.Count == 0)
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CBOR002", "No Attributes", $"No attributes found on {tds.Identifier.Text}.", "warning", DiagnosticSeverity.Warning, true),
                        tds.GetLocation()));
                    return null;
                }

                // Given a type declaration syntax, parse the type to serializable type metadata
                if (!tds.AttributeLists.Any(a => a.Attributes.Any(b => b.Name.ToString() == CborSerializableAttribute))) return null;

                string typeName = tds.Identifier.ToString();
                string @namespace = tds.Parent switch
                {
                    NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                    FileScopedNamespaceDeclarationSyntax fns => fns.Name.ToString(),
                    _ => string.Empty
                };
                string fullName = $"{@namespace}.{typeName}";
                string typeDeclaration = tds switch
                {
                    ClassDeclarationSyntax => "class",
                    RecordDeclarationSyntax => "record",
                    StructDeclarationSyntax => "struct",
                    _ => throw new NotSupportedException($"Type {tds.GetType()} is not supported")
                };

                // Extract type attributes
                IEnumerable<AttributeSyntax> attributes = tds.AttributeLists.SelectMany(a => a.Attributes);
                (AttributeSyntax? cborMapAttribute,
                    AttributeSyntax? cborListAttribute,
                    AttributeSyntax? cborUnionAttribute,
                    AttributeSyntax? cborConstrAttribute,
                    AttributeSyntax? cborTagAttribute
                ) = ExtractTypeAttributes(attributes);

                SerializationType serializationType = GetSerializationType(cborMapAttribute, cborListAttribute, cborUnionAttribute, cborConstrAttribute);
                int? constrIndex = cborConstrAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
                int? cborTag = cborTagAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;

                string? validatorTypeName = null;
                if (model.GetDeclaredSymbol(tds) is INamedTypeSymbol typeSymbol)
                {
                    INamedTypeSymbol? validatorSymbol = FindValidatorForType(typeSymbol, model.Compilation);
                    if (validatorSymbol != null)
                    {
                        validatorTypeName = validatorSymbol.ToDisplayString();
                    }
                }

                // Extract serialazable properties based on the serialization type, for constr/list types 
                // it goes to the order property mapping, for map types it goes to the string/int key mapping
                // for container types it only has one property and it goes to the ienumerable property mapping
                // for union type, it doesn't have any properties but it has child types which are type declaration still
                SerializableTypeMetadata typeMetadata = new(
                    typeName,
                    @namespace,
                    fullName,
                    typeDeclaration,
                    cborTag,
                    constrIndex,
                    serializationType,
                    validatorTypeName
                );

                //Step1: Extract property metadata
                IList<SerializablePropertyMetadata> properties = ExtractPropertyMetadata(tds, model);
                foreach (SerializablePropertyMetadata property in properties)
                {
                    // Add to the main properties collection
                    typeMetadata.Properties.Add(property);

                    // Add to appropriate mappings based on attributes
                    if (property.Order.HasValue)
                    {
                        typeMetadata.PropertyOrderMapping[property.Order.Value] = property;
                    }
                    if (property.PropertyKeyString != null)
                    {
                        typeMetadata.PropertyStringKeyMapping[property.PropertyKeyString] = property;
                    }
                    if (property.PropertyKeyInt.HasValue)
                    {
                        typeMetadata.PropertyIndexKeyMapping[property.PropertyKeyInt.Value.ToString()] = property;
                    }
                }

                //Step2: Handle union types by extracting child types
                if (serializationType == SerializationType.Union)
                {
                    ExtractUnionChildTypes(tds, model, typeMetadata, context);
                }

                _context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CBOR003", "Metadata Generated", $"Metadata generated for {tds.Identifier.Text}.", "info", DiagnosticSeverity.Info, true),
                        tds.GetLocation()));

                // Return dummy metadata as an example
                return typeMetadata;
            }
            catch (Exception ex)
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CBOR004", "Parsing Exception", $"Error parsing {tds.Identifier.Text}: {ex.Message}", "error", DiagnosticSeverity.Error, true),
                    tds.GetLocation()));
                return null;
            }
        }
    }

    private static SerializationType GetSerializationType(AttributeSyntax? cborMapAttribute, AttributeSyntax? cborListAttribute, AttributeSyntax? cborUnionAttribute, AttributeSyntax? cborConstrAttribute)
    {
        if (cborMapAttribute != null) return SerializationType.Map;
        if (cborListAttribute != null) return SerializationType.Array;
        if (cborUnionAttribute != null) return SerializationType.Union;
        if (cborConstrAttribute != null) return SerializationType.Constr;

        return SerializationType.Container;
    }

    private static (
        AttributeSyntax? cborMapAttribute,
        AttributeSyntax? cborListAttribute,
        AttributeSyntax? cborUnionAttribute,
        AttributeSyntax? cborConstrAttribute,
        AttributeSyntax? cborTagAttribute
    ) ExtractTypeAttributes(IEnumerable<AttributeSyntax> attributes)
    {
        AttributeSyntax cborMapAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborMapAttribute);
        AttributeSyntax cborListAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborListAttribute);
        AttributeSyntax cborUnionAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborUnionAttribute);
        AttributeSyntax cborConstrAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborConstrAttribute);
        AttributeSyntax cborTagAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborTagAttribute);

        return (cborMapAttribute, cborListAttribute, cborUnionAttribute, cborConstrAttribute, cborTagAttribute);
    }

    private static IList<SerializablePropertyMetadata> ExtractPropertyMetadata(TypeDeclarationSyntax tds, SemanticModel model)
    {
        List<SerializablePropertyMetadata> properties = [];

        // Process properties from record constructor parameters
        if (tds is RecordDeclarationSyntax recordDecl && recordDecl.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in recordDecl.ParameterList.Parameters)
            {
                SerializablePropertyMetadata? property = ExtractPropertyFromParameter(parameter, model);
                if (property != null)
                {
                    properties.Add(property);
                }
            }
        }

        // Process normal properties
        foreach (PropertyDeclarationSyntax member in tds.Members.OfType<PropertyDeclarationSyntax>())
        {
            SerializablePropertyMetadata? property = ExtractPropertyFromPropertyDeclaration(member, model);
            if (property != null)
            {
                properties.Add(property);
            }
        }

        return properties;
    }

    private static SerializablePropertyMetadata? ExtractPropertyFromParameter(ParameterSyntax parameter, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(parameter) is not IParameterSymbol paramSymbol)
            return null;

        string typeName = paramSymbol.Type.Name;
        string typeFullName = paramSymbol.Type.ToDisplayString();
        string typeNamespace = paramSymbol.Type.ContainingNamespace.ToDisplayString();
        string propertyName = parameter.Identifier.Text;

        // Extract attributes
        IEnumerable<AttributeSyntax> attributes = parameter.AttributeLists.SelectMany(a => a.Attributes);

        // Check for nullable attribute
        bool isNullable = attributes.Any(a => a.Name.ToString() == Parser.CborNullableAttribute);

        // Check for size attribute
        AttributeSyntax sizeAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborSizeAttribute);
        int? size = null;
        if (sizeAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax sizeArg)
        {
            if (model.GetConstantValue(sizeArg.Expression).HasValue)
            {
                size = (int?)model.GetConstantValue(sizeArg.Expression).Value;
            }
        }

        // Check for indefinite attribute
        bool isIndefinite = attributes.Any(a => a.Name.ToString() == Parser.CborIndefiniteAttribute);

        // Create the property metadata
        SerializablePropertyMetadata propertyMetadata = new SerializablePropertyMetadata(
            propertyName,
            typeName,
            typeFullName,
            typeNamespace,
            isNullable,
            size,
            isIndefinite
        );

        // Get Order attribute
        AttributeSyntax orderAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborOrderAttribute);
        if (orderAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax orderArg)
        {
            if (model.GetConstantValue(orderArg.Expression).HasValue)
            {
                propertyMetadata.Order = (int?)model.GetConstantValue(orderArg.Expression).Value;
            }
        }

        // Get Property key attribute
        AttributeSyntax propertyAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborPropertyAttribute);
        if (propertyAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax propertyArg)
        {
            object? constValue = model.GetConstantValue(propertyArg.Expression).Value;
            if (constValue is int intKey)
            {
                propertyMetadata.PropertyKeyInt = intKey;
                propertyMetadata.PropertyKeyString = intKey.ToString();
            }
            else if (constValue is string strKey)
            {
                propertyMetadata.PropertyKeyString = strKey;
            }
        }

        // Extract validation attributes
        ExtractValidationAttributes(propertyMetadata, attributes, model);

        return propertyMetadata;
    }

    private static SerializablePropertyMetadata? ExtractPropertyFromPropertyDeclaration(PropertyDeclarationSyntax property, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(property) is not IPropertySymbol propSymbol)
            return null;

        string typeName = propSymbol.Type.Name;
        string typeFullName = propSymbol.Type.ToDisplayString();
        string typeNamespace = propSymbol.Type.ContainingNamespace.ToDisplayString();
        string propertyName = property.Identifier.Text;

        // Extract attributes
        IEnumerable<AttributeSyntax> attributes = property.AttributeLists.SelectMany(a => a.Attributes);

        // Check for nullable attribute
        bool isNullable = attributes.Any(a => a.Name.ToString() == Parser.CborNullableAttribute);

        // Check for size attribute
        AttributeSyntax sizeAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborSizeAttribute);
        int? size = null;
        if (sizeAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax sizeArg)
        {
            if (model.GetConstantValue(sizeArg.Expression).HasValue)
            {
                size = (int?)model.GetConstantValue(sizeArg.Expression).Value;
            }
        }

        // Check for indefinite attribute
        bool isIndefinite = attributes.Any(a => a.Name.ToString() == Parser.CborIndefiniteAttribute);

        // Create the property metadata
        SerializablePropertyMetadata propertyMetadata = new SerializablePropertyMetadata(
            propertyName,
            typeName,
            typeFullName,
            typeNamespace,
            isNullable,
            size,
            isIndefinite
        );

        // Get Order attribute
        AttributeSyntax orderAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborOrderAttribute);
        if (orderAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax orderArg)
        {
            if (model.GetConstantValue(orderArg.Expression).HasValue)
            {
                propertyMetadata.Order = (int?)model.GetConstantValue(orderArg.Expression).Value;
            }
        }

        // Get Property key attribute
        AttributeSyntax propertyAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborPropertyAttribute);
        if (propertyAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax propertyArg)
        {
            object? constValue = model.GetConstantValue(propertyArg.Expression).Value;
            if (constValue is int intKey)
            {
                propertyMetadata.PropertyKeyInt = intKey;
                propertyMetadata.PropertyKeyString = intKey.ToString();
            }
            else if (constValue is string strKey)
            {
                propertyMetadata.PropertyKeyString = strKey;
            }
        }

        // Extract validation attributes
        ExtractValidationAttributes(propertyMetadata, attributes, model);

        return propertyMetadata;
    }

    private static void ExtractValidationAttributes(SerializablePropertyMetadata propertyMetadata, IEnumerable<AttributeSyntax> attributes, SemanticModel model)
    {
        // Extract validate exact attribute
        AttributeSyntax exactAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborValidateExactAttribute);
        if (exactAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax exactArg)
        {
            if (model.GetConstantValue(exactArg.Expression).HasValue)
            {
                propertyMetadata.ExpectedValue = model.GetConstantValue(exactArg.Expression).Value;
            }
        }

        // Extract validate range attribute
        AttributeSyntax rangeAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborValidateRangeAttribute);
        if (rangeAttr?.ArgumentList?.Arguments.Count >= 2)
        {
            AttributeArgumentSyntax minArg = rangeAttr.ArgumentList.Arguments[0];
            AttributeArgumentSyntax maxArg = rangeAttr.ArgumentList.Arguments[1];

            if (model.GetConstantValue(minArg.Expression).HasValue)
            {
                propertyMetadata.MinimumValue = Convert.ToDouble(model.GetConstantValue(minArg.Expression).Value);
            }

            if (model.GetConstantValue(maxArg.Expression).HasValue)
            {
                propertyMetadata.MaximumValue = Convert.ToDouble(model.GetConstantValue(maxArg.Expression).Value);
            }
        }

        // Extract validator type
        AttributeSyntax validateAttr = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborValidateAttribute);
        if (validateAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax validateArg)
        {
            if (model.GetSymbolInfo(validateArg.Expression).Symbol is ITypeSymbol validatorType)
            {
                propertyMetadata.ValidatorTypeName = validatorType.ToDisplayString();
            }
        }
    }

    private static void ExtractUnionChildTypes(TypeDeclarationSyntax tds, SemanticModel model, SerializableTypeMetadata typeMetadata, SourceProductionContext context)
    {
        if (model.GetDeclaredSymbol(tds) is not INamedTypeSymbol typeSymbol)
            return;

        // Get all derived types in the current compilation
        IEnumerable<INamedTypeSymbol> derivedTypes = FindDerivedTypes(typeSymbol, model.Compilation);

        // Create child type metadata and add to parent
        foreach (INamedTypeSymbol derivedType in derivedTypes)
        {
            // Find the syntax declaration for the derived type
            TypeDeclarationSyntax? derivedSyntax = FindTypeDeclaration(derivedType, model.Compilation);
            if (derivedSyntax != null)
            {
                Parser parser = new(context);
                SerializableTypeMetadata? childMetadata = parser.ParseSerialazableType(derivedSyntax, model.Compilation.GetSemanticModel(derivedSyntax.SyntaxTree));

                if (childMetadata != null)
                {
                    typeMetadata.ChildTypes.Add(childMetadata);
                }
            }
        }
    }

    private static TypeDeclarationSyntax? FindTypeDeclaration(INamedTypeSymbol typeSymbol, Compilation compilation)
    {
        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            SyntaxNode root = syntaxTree.GetRoot();
            SemanticModel model = compilation.GetSemanticModel(syntaxTree);

            foreach (TypeDeclarationSyntax typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol declaredSymbol &&
                    SymbolEqualityComparer.Default.Equals(declaredSymbol, typeSymbol))
                {
                    return typeDecl;
                }
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> FindDerivedTypes(INamedTypeSymbol baseType, Compilation compilation)
    {
        List<INamedTypeSymbol> derivedTypes = [];

        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(syntaxTree);
            SyntaxNode root = syntaxTree.GetRoot();

            foreach (TypeDeclarationSyntax typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol typeSymbol)
                {
                    // Check if it derives from the base type
                    if (IsTypeDerivedFrom(typeSymbol, baseType))
                    {
                        derivedTypes.Add(typeSymbol);
                    }
                }
            }
        }

        return derivedTypes;
    }

    private static bool IsTypeDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        INamedTypeSymbol? currentType = type.BaseType;

        while (currentType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(currentType, baseType))
            {
                return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }

    private static INamedTypeSymbol? FindValidatorForType(INamedTypeSymbol typeSymbol, Compilation compilation)
    {
        // Get the generic interface symbol for ICborValidator<T>
        INamedTypeSymbol? validatorInterface = compilation.GetTypeByMetadataName("Chrysalis.Cbor.Serialization.ICborValidator`1");
        if (validatorInterface == null)
        {
            return null;
        }

        // Iterate through all syntax trees in the compilation to find candidate classes.
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            IEnumerable<ClassDeclarationSyntax> classDeclarations = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (ClassDeclarationSyntax classDecl in classDeclarations)
            {
                if (semanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol candidateSymbol) continue;
                // Check if candidate implements any interface whose definition is ICborValidator<T>
                foreach (INamedTypeSymbol iface in candidateSymbol.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, validatorInterface))
                    {
                        // Check if the type argument is our type
                        if (iface.TypeArguments.Length == 1 &&
                            SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], typeSymbol))
                        {
                            return candidateSymbol;
                        }
                    }
                }
            }
        }
        return null;
    }
}