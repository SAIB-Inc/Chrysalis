using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.SourceGenerator;

public sealed partial class CborSourceGenerator
{
    private sealed class Parser
    {
        // Tracking processed types to avoid duplicates
        private readonly Dictionary<ITypeSymbol, SerializableType> _processedTypes =
            new(SymbolEqualityComparer.Default);

        // Queue of types waiting to be processed
        private readonly Queue<TypeToProcess> _typesToProcess = new();
        
        // Reference to the compilation for type lookup across assemblies
        private Compilation? _compilation;

        /// <summary>
        /// Parses a type declaration and creates serialization metadata for it and all related types
        /// </summary>
        public SerializationContext ParseType(
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken
        )
        {
            // Reset our tracking collections
            _processedTypes.Clear();
            _typesToProcess.Clear();

            // Get the symbol for the type declaration
            if (semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is not INamedTypeSymbol contextType)
            {
                return new SerializationContext();
            }

            // Store compilation for validator detection and type lookup
            _compilation = semanticModel.Compilation;

            // Create the context
            var context = new SerializationContext
            {
                ContextType = new TypeInfo(contextType)
            };

            // Discover serializable types starting from the context
            DiscoverInitialTypes(contextType);

            // Process all discovered types
            ProcessTypeQueue(cancellationToken, _compilation);

            // Add processed types to context
            context.Types = _processedTypes.Values.ToList();

            return context;
        }

        /// <summary>
        /// Processes all types in the queue
        /// </summary>
        private void ProcessTypeQueue(CancellationToken cancellationToken, Compilation? compilation = null)
        {
            while (_typesToProcess.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var typeToProcess = _typesToProcess.Dequeue();

                // Skip if already processed (might have been added multiple times)
                if (_processedTypes.ContainsKey(typeToProcess.Type))
                    continue;

                // Extract metadata and add to processed types
                var typeMetadata = ExtractTypeMetadata(typeToProcess, compilation);
                _processedTypes.Add(typeToProcess.Type, typeMetadata);

                // Queue dependent types for processing
                QueueDependentTypes(typeMetadata);
            }
        }

        /// <summary>
        /// Extracts metadata from a type
        /// </summary>
        private SerializableType ExtractTypeMetadata(TypeToProcess typeToProcess, Compilation? compilation = null)
        {
            var type = typeToProcess.Type;
            var logBuilder = new StringBuilder();

            logBuilder.AppendLine($"// Extracting metadata for type: {type.ToDisplayString()}");

            // Create the base serializable type
            SerializableType metadata = new SerializableType
            {
                Type = new TypeInfo(type),
                InfoPropertyName = type.Name
            };

            // Set format based on nullable flag
            if (typeToProcess.IsNullable)
            {
                metadata.Format = SerializationType.Nullable;
                logBuilder.AppendLine($"// Set initial format to Nullable based on processing flag");
            }

            // Extract format and other attributes
            ProcessTypeAttributes(type, metadata, compilation, logBuilder);

            // Process type members based on kind
            if (type is INamedTypeSymbol namedType)
            {
                // Extract properties
                ExtractProperties(namedType, metadata);
                logBuilder.AppendLine($"// Extracted {metadata.Properties.Count} properties");

                // Extract constructor parameters
                ExtractConstructorParameters(namedType, metadata);
                logBuilder.AppendLine($"// Extracted {metadata.Parameters.Count} constructor parameters");

                // Handle union types
                if (metadata.Format == SerializationType.Union)
                {
                    ExtractUnionCases(namedType, metadata);
                    logBuilder.AppendLine($"// Extracted {metadata.UnionCases.Count} union cases");
                }

                // Handle collection types
                ExtractCollectionTypeInfo(namedType, metadata);

                if (metadata.ElementType != null)
                    logBuilder.AppendLine($"// Extracted element type: {metadata.ElementType.FullName}");

                if (metadata.KeyType != null)
                    logBuilder.AppendLine($"// Extracted key type: {metadata.KeyType.FullName}");

                // Check if type has base Write/Read methods
                metadata.HasBaseWriteMethod = true;
                metadata.HasBaseReadMethod = true;
            }
            else if (type is IArrayTypeSymbol arrayType)
            {
                // Handle array types
                metadata.ElementType = new TypeInfo(arrayType.ElementType);
                metadata.Format = SerializationType.Array;
                logBuilder.AppendLine($"// Set format to Array for array type with element type: {metadata.ElementType.FullName}");
            }

            logBuilder.AppendLine($"// Final metadata extraction result:");
            logBuilder.AppendLine($"//   Format: {metadata.Format}");
            logBuilder.AppendLine($"//   ValidatorTypeName: {metadata.ValidatorTypeName ?? "None"}");
            logBuilder.AppendLine($"//   HasValidator: {metadata.HasValidator}");

            // Store the debug info in the metadata for logging
            metadata.DebugInfo = logBuilder.ToString();

            return metadata;
        }

        /// <summary>
        /// Discovers the initial types to process based on attributes
        /// </summary>
        private void DiscoverInitialTypes(INamedTypeSymbol contextType)
        {
            // Check for CBOR serialization attributes on the context type
            foreach (var attribute in contextType.GetAttributes())
            {
                string? attributeName = attribute.AttributeClass?.ToDisplayString();

                if (attributeName == Constants.CborSerializableAttributeFullName)
                {
                    AddTypeToQueue(contextType, isNullable: false);
                }
                else if (attributeName == Constants.CborNullableAttributeFullName)
                {
                    AddTypeToQueue(contextType, isNullable: true);
                }
            }
        }

        /// <summary>
        /// Adds a type to the processing queue
        /// </summary>
        private void AddTypeToQueue(ITypeSymbol type, bool isNullable)
        {
            // Skip if already processed or queued
            if (_processedTypes.ContainsKey(type))
                return;

            var typeProcess = new TypeToProcess
            {
                Type = type,
                IsNullable = isNullable
            };

            _typesToProcess.Enqueue(typeProcess);
        }

        /// <summary>
        /// Processes all types in the queue
        /// </summary>
        private void ProcessTypeQueue(CancellationToken cancellationToken)
        {
            while (_typesToProcess.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var typeToProcess = _typesToProcess.Dequeue();

                // Skip if already processed (might have been added multiple times)
                if (_processedTypes.ContainsKey(typeToProcess.Type))
                    continue;

                // Extract metadata and add to processed types
                var typeMetadata = ExtractTypeMetadata(typeToProcess);
                _processedTypes.Add(typeToProcess.Type, typeMetadata);

                // Queue dependent types for processing
                QueueDependentTypes(typeMetadata);
            }
        }

        /// <summary>
        /// Extracts metadata from a type
        /// </summary>
        private SerializableType ExtractTypeMetadata(TypeToProcess typeToProcess)
        {
            var type = typeToProcess.Type;

            // Create the base serializable type
            var metadata = new SerializableType
            {
                Type = new TypeInfo(type),
                InfoPropertyName = type.Name
            };

            // Set format based on nullable flag
            if (typeToProcess.IsNullable)
                metadata.Format = SerializationType.Nullable;

            // Extract format and other attributes
            ProcessTypeAttributes(type, metadata);

            // Process type members based on kind
            if (type is INamedTypeSymbol namedType)
            {
                // Extract properties
                ExtractProperties(namedType, metadata);

                // Extract constructor parameters
                ExtractConstructorParameters(namedType, metadata);

                // Handle union types
                if (metadata.Format == SerializationType.Union)
                {
                    ExtractUnionCases(namedType, metadata);
                }

                // Handle collection types
                ExtractCollectionTypeInfo(namedType, metadata);

                // Check if type has base Write/Read methods
                metadata.HasBaseWriteMethod = true;
                metadata.HasBaseReadMethod = true;
            }
            else if (type is IArrayTypeSymbol arrayType)
            {
                // Handle array types
                metadata.ElementType = new TypeInfo(arrayType.ElementType);
                metadata.Format = SerializationType.Array;
            }

            return metadata;
        }

        /// <summary>
        /// Processes attributes on a type to determine format and settings
        /// </summary>
        private void ProcessTypeAttributes(ITypeSymbol type, SerializableType metadata, Compilation? compilation = null, StringBuilder? logBuilder = null)
        {
            logBuilder?.AppendLine($"// Processing attributes for type: {type.ToDisplayString()}");

            bool hasSerializableAttr = false;
            bool isFormatDetermined = metadata.Format != SerializationType.Object;

            foreach (var attr in type.GetAttributes())
            {
                string? attrName = attr.AttributeClass?.ToDisplayString();
                logBuilder?.AppendLine($"//   Attribute: {attrName}");

                switch (attrName)
                {
                    case Constants.CborSerializableAttributeFullName:
                        hasSerializableAttr = true;

                        if (type.ToDisplayString() == "Chrysalis.Cbor.Types.Primitives.CborEncodedValue")
                        {
                            metadata.Format = SerializationType.Encoded;
                            isFormatDetermined = true;
                            logBuilder?.AppendLine($"//     Set format to Encoded");
                        }
                        break;

                    case Constants.CborMapAttributeFullName:
                        metadata.Format = SerializationType.Map;
                        isFormatDetermined = true;
                        logBuilder?.AppendLine($"//     Set format to Map");
                        break;

                    case Constants.CborListAttributeFullName:
                        metadata.Format = SerializationType.Array;
                        isFormatDetermined = true;
                        logBuilder?.AppendLine($"//     Set format to Array");
                        break;

                    case Constants.CborUnionAttributeFullName:
                        metadata.Format = SerializationType.Union;
                        isFormatDetermined = true;
                        logBuilder?.AppendLine($"//     Set format to Union");
                        break;

                    case Constants.CborConstrAttributeFullName:
                        metadata.Format = SerializationType.Constr;
                        isFormatDetermined = true;
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            metadata.Constructor = attr.ConstructorArguments[0].Value as int?;
                            logBuilder?.AppendLine($"//     Set format to Constr with index: {metadata.Constructor}");
                        }
                        break;

                    case Constants.CborTagAttributeFullName:
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            metadata.Tag = attr.ConstructorArguments[0].Value as int?;
                            logBuilder?.AppendLine($"//     Set tag: {metadata.Tag}");
                        }
                        break;

                    case Constants.CborNullableAttributeFullName:
                        metadata.Format = SerializationType.Nullable;
                        isFormatDetermined = true;
                        logBuilder?.AppendLine($"//     Set format to Nullable");
                        break;

                    case Constants.CborIndefiniteAttributeFullName:
                        metadata.IsIndefinite = true;
                        logBuilder?.AppendLine($"//     Set IsIndefinite = true");
                        break;

                    case Constants.CborSizeAttributeFullName:
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            metadata.Size = attr.ConstructorArguments[0].Value as int?;
                            logBuilder?.AppendLine($"//     Set Size = {metadata.Size}");
                        }
                        break;
                }
            }

            // Detect validator with detailed logging
            DetectValidator(type, metadata, compilation, logBuilder);

            // Handle container types with a single property
            if (hasSerializableAttr && !isFormatDetermined && metadata.Properties.Count == 1)
            {
                metadata.Format = SerializationType.Container;
                logBuilder?.AppendLine($"//   Set format to Container (single property)");
            }
        }


        /// <summary>
        /// Extracts property metadata from a type
        /// </summary>
        private void ExtractProperties(INamedTypeSymbol type, SerializableType metadata)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property && !property.IsStatic && !property.IsImplicitlyDeclared)
                {
                    var propertyMetadata = new SerializableProperty(property);
                    metadata.Properties.Add(propertyMetadata);

                    // Track dependencies
                    metadata.Dependencies.Add(propertyMetadata.Type);

                    if (propertyMetadata.ElementType != null)
                        metadata.Dependencies.Add(propertyMetadata.ElementType);

                    if (propertyMetadata.KeyType != null)
                        metadata.Dependencies.Add(propertyMetadata.KeyType);
                }
            }
        }

        /// <summary>
        /// Extracts constructor parameters
        /// </summary>
        private void ExtractConstructorParameters(INamedTypeSymbol type, SerializableType metadata)
        {
            // Find public constructors
            var constructors = type.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                .ToList();

            if (constructors.Any())
            {
                // Choose the constructor with the most parameters
                var constructor = constructors.OrderByDescending(c => c.Parameters.Length).First();

                foreach (var parameter in constructor.Parameters)
                {
                    var paramMetadata = new ConstructorParam(parameter);
                    metadata.Parameters.Add(paramMetadata);

                    // Track dependency
                    metadata.Dependencies.Add(paramMetadata.Type);
                }
            }
        }

        /// <summary>
        /// Extracts union case types for a union type
        /// </summary>
        private void ExtractUnionCases(INamedTypeSymbol type, SerializableType metadata, StringBuilder? logBuilder = null)
        {
            logBuilder?.AppendLine($"// Extracting union cases for: {type.ToDisplayString()}");

            // Get type assembly
            var assembly = type.ContainingAssembly;
            
            // Use a HashSet to track processed types by full name
            var processedTypeFullNames = new HashSet<string>();
            // Use a separate HashSet to track type names to avoid duplicate case labels
            var processedTypeNames = new HashSet<string>();

            // First, add direct nested types (for nested class approach)
            foreach (var nestedType in type.GetTypeMembers())
            {
                if (IsUnionCaseType(nestedType, type))
                {
                    string fullName = nestedType.ToDisplayString();
                    
                    // Skip if already processed
                    if (processedTypeFullNames.Contains(fullName))
                        continue;
                        
                    processedTypeFullNames.Add(fullName);
                    processedTypeNames.Add(nestedType.Name);
                    
                    var nestedTypeInfo = new TypeInfo(nestedType);
                    metadata.UnionCases.Add(nestedTypeInfo);
                    metadata.Dependencies.Add(nestedTypeInfo);

                    logBuilder?.AppendLine($"//   Added nested union case: {nestedTypeInfo.FullName}");

                    // Add to processing queue
                    AddTypeToQueue(nestedType, isNullable: false);
                }
            }

            // Check for implementations in the same namespace (for separate class approach)
            var typeNs = type.ContainingNamespace;
            if (typeNs != null)
            {
                foreach (var nsType in typeNs.GetTypeMembers())
                {
                    if (!SymbolEqualityComparer.Default.Equals(nsType, type) && IsUnionCaseType(nsType, type))
                    {
                        string fullName = nsType.ToDisplayString();
                        
                        // Skip if already processed
                        if (processedTypeFullNames.Contains(fullName))
                            continue;
                            
                        processedTypeFullNames.Add(fullName);
                        processedTypeNames.Add(nsType.Name);
                        
                        var caseTypeInfo = new TypeInfo(nsType);
                        metadata.UnionCases.Add(caseTypeInfo);
                        metadata.Dependencies.Add(caseTypeInfo);

                        logBuilder?.AppendLine($"//   Added namespace union case: {caseTypeInfo.FullName}");

                        // Add to processing queue
                        AddTypeToQueue(nsType, isNullable: false);
                    }
                }
            }

            // For separate implementations that might be in different namespaces,
            // search the assembly for other types that derive from this one
            if (assembly != null)
            {
                // If the type is abstract, log that we're searching deeper
                if (type.IsAbstract)
                {
                    logBuilder?.AppendLine($"//   Searching for implementations of abstract type: {type.ToDisplayString()}");
                }

                var compilation = _compilation;
                if (compilation != null)
                {
                    try
                    {
                        // Get all types from the compilation that might be candidates
                        // This uses a more direct approach to find types in all namespaces
                        foreach (var symbol in compilation.GetSymbolsWithName(
                            s => !s.Contains("<") && !s.StartsWith("_"), // Filter out generic instantiations and compiler-generated names
                            SymbolFilter.Type))
                        {
                            if (symbol is INamedTypeSymbol potentialType && 
                                !potentialType.IsAbstract && 
                                !SymbolEqualityComparer.Default.Equals(potentialType, type))
                            {
                                if (IsUnionCaseType(potentialType, type))
                                {
                                    string fullName = potentialType.ToDisplayString();
                                    
                                    // Skip if already processed
                                    if (processedTypeFullNames.Contains(fullName))
                                        continue;
                                        
                                    processedTypeFullNames.Add(fullName);
                                    
                                    // Check for duplicate type names
                                    if (processedTypeNames.Contains(potentialType.Name))
                                    {
                                        logBuilder?.AppendLine($"//   Skipping duplicate type name: {potentialType.Name}");
                                        continue;
                                    }
                                    
                                    processedTypeNames.Add(potentialType.Name);
                                    
                                    var caseTypeInfo = new TypeInfo(potentialType);
                                    metadata.UnionCases.Add(caseTypeInfo);
                                    metadata.Dependencies.Add(caseTypeInfo);

                                    logBuilder?.AppendLine($"//   Added cross-namespace union case: {caseTypeInfo.FullName}");

                                    // Add to processing queue
                                    AddTypeToQueue(potentialType, isNullable: false);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log any exceptions during the search but continue processing
                        logBuilder?.AppendLine($"//   Error during cross-namespace search: {ex.Message}");
                    }
                }
            }

            // Remove any duplicate union cases based on type name
            var uniqueCases = new Dictionary<string, TypeInfo>();
            foreach (var unionCase in metadata.UnionCases.ToList())
            {
                string typeName = unionCase.Name;
                if (!uniqueCases.ContainsKey(typeName))
                {
                    uniqueCases[typeName] = unionCase;
                }
                else
                {
                    logBuilder?.AppendLine($"//   Removed duplicate union case name: {typeName}");
                    metadata.UnionCases.Remove(unionCase);
                }
            }

            logBuilder?.AppendLine($"//   Total union cases found: {metadata.UnionCases.Count}");
        }

        /// <summary>
        /// Determines if a type is a valid union case for the given base type
        /// </summary>
        private bool IsUnionCaseType(ITypeSymbol candidateType, ITypeSymbol baseType)
        {
            // Must not be abstract
            if (candidateType.IsAbstract)
                return false;

            // Must not be the base type itself
            if (SymbolEqualityComparer.Default.Equals(candidateType, baseType))
                return false;

            // Check inheritance relationship
            bool inheritsFromBase = false;

            // Walk the inheritance chain
            var currentType = candidateType;
            while (currentType != null)
            {
                if (currentType.BaseType != null)
                {
                    // For generic types, we need to check the original definition
                    if (currentType.BaseType is INamedTypeSymbol baseTypeSymbol && baseTypeSymbol.IsGenericType)
                    {
                        var baseTypeDef = baseTypeSymbol.OriginalDefinition;
                        var unionBaseDef = baseType is INamedTypeSymbol namedBaseType && namedBaseType.IsGenericType
                            ? namedBaseType.OriginalDefinition
                            : baseType;

                        if (SymbolEqualityComparer.Default.Equals(baseTypeDef, unionBaseDef))
                        {
                            inheritsFromBase = true;
                            break;
                        }
                    }
                    else
                    {
                        // Check direct inheritance
                        if (SymbolEqualityComparer.Default.Equals(currentType.BaseType, baseType))
                        {
                            inheritsFromBase = true;
                            break;
                        }
                        
                        // For non-generic types, also check if base type name matches (for different assemblies)
                        if (currentType.BaseType.Name == baseType.Name && 
                            currentType.BaseType.ContainingNamespace?.ToString() == baseType.ContainingNamespace?.ToString())
                        {
                            inheritsFromBase = true;
                            break;
                        }
                    }
                }

                currentType = currentType.BaseType;
            }

            // If we still haven't found a match and both types are named types,
            // do a more thorough check for generic types
            if (!inheritsFromBase && 
                candidateType is INamedTypeSymbol candidateNamedType && 
                baseType is INamedTypeSymbol baseNamedType)
            {
                // Handle special case for generic types in different compilation units
                if (baseNamedType.IsGenericType)
                {
                    var baseTypeWithoutArgs = baseNamedType.Name.Split('`')[0];
                    var candidateBase = candidateNamedType.BaseType;
                    
                    while (candidateBase != null)
                    {
                        if (candidateBase is INamedTypeSymbol candidateBaseNamed && candidateBaseNamed.IsGenericType)
                        {
                            var candidateBaseName = candidateBaseNamed.Name.Split('`')[0];
                            if (candidateBaseName == baseTypeWithoutArgs && 
                                candidateBaseNamed.ContainingNamespace?.ToString() == baseNamedType.ContainingNamespace?.ToString())
                            {
                                inheritsFromBase = true;
                                break;
                            }
                        }
                        
                        candidateBase = candidateBase.BaseType;
                    }
                }
            }

            return inheritsFromBase;
        }

        /// <summary>
        /// Extracts collection type information
        /// </summary>
        private void ExtractCollectionTypeInfo(INamedTypeSymbol type, SerializableType metadata)
        {
            // Skip string (IEnumerable<char>)
            if (type.ToDisplayString() == "string")
                return;

            // Check for dictionary
            var dictionaryInterface = type.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");

            if (dictionaryInterface != null && dictionaryInterface.TypeArguments.Length == 2)
            {
                metadata.KeyType = new TypeInfo(dictionaryInterface.TypeArguments[0]);
                metadata.ElementType = new TypeInfo(dictionaryInterface.TypeArguments[1]);

                // Track dependencies
                metadata.Dependencies.Add(metadata.KeyType);
                metadata.Dependencies.Add(metadata.ElementType);
                return;
            }

            // Check for other collection types
            var enumerableInterface = type.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");

            if (enumerableInterface != null && enumerableInterface.TypeArguments.Length == 1)
            {
                metadata.ElementType = new TypeInfo(enumerableInterface.TypeArguments[0]);
                metadata.Dependencies.Add(metadata.ElementType);
            }
        }

        /// <summary>
        /// Queues dependent types for processing
        /// </summary>
        private void QueueDependentTypes(SerializableType metadata)
        {
            foreach (var dependency in metadata.Dependencies)
            {
                // Skip primitive types and types we've already processed
                if (IsPrimitiveType(dependency.FullName))
                    continue;

                // We need to find the original symbol for this dependency
                // In a real implementation, we'd maintain a map from TypeInfo to ITypeSymbol
                // For now, let's assume we can get it through semantic model lookups
            }
        }

        /// <summary>
        /// Determines if a type is a primitive that doesn't need special handling
        /// </summary>
        private bool IsPrimitiveType(string typeName)
        {
            return typeName switch
            {
                "System.Int32" or "int" => true,
                "System.Int64" or "long" => true,
                "System.UInt32" or "uint" => true,
                "System.UInt64" or "ulong" => true,
                "System.Int16" or "short" => true,
                "System.UInt16" or "ushort" => true,
                "System.Byte" or "byte" => true,
                "System.SByte" or "sbyte" => true,
                "System.Double" or "double" => true,
                "System.Single" or "float" => true,
                "System.Decimal" or "decimal" => true,
                "System.Boolean" or "bool" => true,
                "System.Char" or "char" => true,
                "System.String" or "string" => true,
                "System.DateTime" => true,
                "System.Guid" => true,
                _ => false
            };
        }
    }

    /// <summary>
    /// Helper class for tracking types to be processed
    /// </summary>
    private sealed class TypeToProcess
    {
        public ITypeSymbol Type { get; set; } = null!;
        public bool IsNullable { get; set; }
        public ITypeSymbol? InnerType { get; set; }
        public SerializationType? InnerFormat { get; set; }
    }

    /// <summary>
    /// Detects and sets validator information for a type
    /// </summary>
    private static void DetectValidator(ITypeSymbol type, SerializableType metadata, Compilation? compilation, StringBuilder? logBuilder = null)
    {
        if (type is not INamedTypeSymbol namedType || compilation == null)
            return;

        // Log start of validator detection if logging is enabled
        logBuilder?.AppendLine($"// Detecting validator for: {namedType.ToDisplayString()}");

        // 1. First approach: Check if the type implements ICborValidator<T> for itself
        bool implementsSelfValidator = false;
        foreach (var interfaceType in namedType.AllInterfaces)
        {
            string interfaceFullName = interfaceType.ToDisplayString();
            bool isValidator = interfaceFullName.StartsWith("Chrysalis.Cbor.Types.ICborValidator<") ||
                               interfaceFullName.StartsWith("Chrysalis.Cbor.Serialization.ICborValidator<") ||
                               interfaceFullName.Contains(".ICborValidator<");

            logBuilder?.AppendLine($"//    Interface: {interfaceFullName}, IsValidator: {isValidator}");

            if (isValidator && interfaceType.TypeArguments.Length == 1)
            {
                var typeArgFullName = interfaceType.TypeArguments[0].ToDisplayString();
                var selfFullName = namedType.ToDisplayString();

                logBuilder?.AppendLine($"//       Type arg: {typeArgFullName}, Self: {selfFullName}");

                if (typeArgFullName == selfFullName)
                {
                    implementsSelfValidator = true;
                    metadata.ValidatorTypeName = namedType.ToDisplayString();

                    logBuilder?.AppendLine($"//       Self-validation detected: {metadata.ValidatorTypeName}");
                    break;
                }
            }
        }

        // If the type doesn't validate itself, look for a dedicated validator
        if (!implementsSelfValidator)
        {
            // 2. Look for a validator with the pattern "{TypeName}Validator"
            string validatorName = $"{namedType.Name}Validator";
            logBuilder?.AppendLine($"//    Searching for validator named: {validatorName}");

            // Possible namespaces to check
            var namespacesToCheck = new List<string>
        {
            namedType.ContainingNamespace.ToDisplayString(), // Same namespace
            $"{namedType.ContainingNamespace}.Validators", // Dedicated validators namespace
            "Chrysalis.Cbor.Types.Validators", // Global validators namespace
            "Chrysalis.Cbor.Serialization.Validators" // Another common validators namespace
        };

            // Try specific namespace locations first
            foreach (var ns in namespacesToCheck)
            {
                string fullName = $"{ns}.{validatorName}";
                logBuilder?.AppendLine($"//    Checking: {fullName}");

                var validatorType = compilation.GetTypeByMetadataName(fullName);
                if (validatorType != null)
                {
                    logBuilder?.AppendLine($"//       Found validator type: {fullName}");

                    // Check if it implements ICborValidator<T> for our type
                    CheckIfImplementsValidator(validatorType, namedType, metadata, logBuilder);

                    if (metadata.ValidatorTypeName != null)
                        break;
                }
            }

            // If still not found, try a broader search
            if (metadata.ValidatorTypeName == null)
            {
                logBuilder?.AppendLine($"//    Performing broader search for: {validatorName}");

                // Search for any type that ends with our validator name
                foreach (var symbol in compilation.GetSymbolsWithName(n => n == validatorName, SymbolFilter.Type))
                {
                    if (symbol is INamedTypeSymbol validatorType)
                    {
                        logBuilder?.AppendLine($"//       Found potential validator: {validatorType.ToDisplayString()}");

                        // Check if it implements ICborValidator<T> for our type
                        CheckIfImplementsValidator(validatorType, namedType, metadata, logBuilder);

                        if (metadata.ValidatorTypeName != null)
                            break;
                    }
                }
            }
        }

        logBuilder?.AppendLine($"// Final validator detection result: {metadata.ValidatorTypeName ?? "None"}");
    }

    /// <summary>
    /// Checks if a type implements ICborValidator for another type
    /// </summary>
    private static void CheckIfImplementsValidator(INamedTypeSymbol validatorType, INamedTypeSymbol targetType, SerializableType metadata, StringBuilder? logBuilder = null)
    {
        foreach (var interfaceType in validatorType.AllInterfaces)
        {
            string interfaceFullName = interfaceType.ToDisplayString();
            bool isValidator = interfaceFullName.StartsWith("Chrysalis.Cbor.Types.ICborValidator<") ||
                               interfaceFullName.StartsWith("Chrysalis.Cbor.Serialization.ICborValidator<") ||
                               interfaceFullName.Contains(".ICborValidator<");

            logBuilder?.AppendLine($"//          Interface: {interfaceFullName}, IsValidator: {isValidator}");

            if (isValidator && interfaceType.TypeArguments.Length == 1)
            {
                var validatedTypeName = interfaceType.TypeArguments[0].ToDisplayString();
                var targetTypeName = targetType.ToDisplayString();

                logBuilder?.AppendLine($"//             Validates: {validatedTypeName}, Target: {targetTypeName}");

                if (validatedTypeName == targetTypeName)
                {
                    metadata.ValidatorTypeName = validatorType.ToDisplayString();
                    logBuilder?.AppendLine($"//             MATCH FOUND: {metadata.ValidatorTypeName}");
                    return;
                }
            }
        }
    }
}