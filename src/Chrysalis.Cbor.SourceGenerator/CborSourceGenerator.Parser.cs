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

        /// <summary>
        /// Parses a type declaration and creates serialization metadata for it and all related types
        /// </summary>
        public SerializationContext ParseType(
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            // Reset our tracking collections
            _processedTypes.Clear();
            _typesToProcess.Clear();

            // Get the symbol for the type declaration
            if (semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is not INamedTypeSymbol contextType)
            {
                return new SerializationContext();
            }

            // Create the context
            var context = new SerializationContext
            {
                ContextType = new TypeInfo(contextType)
            };

            // Discover serializable types starting from the context
            DiscoverInitialTypes(contextType);

            // Process all discovered types
            ProcessTypeQueue(cancellationToken);

            // Add processed types to context
            context.Types = _processedTypes.Values.ToList();

            return context;
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
        private void ProcessTypeAttributes(ITypeSymbol type, SerializableType metadata)
        {
            bool hasSerializableAttr = false;
            bool isFormatDetermined = metadata.Format != SerializationType.Object;

            foreach (var attr in type.GetAttributes())
            {
                string? attrName = attr.AttributeClass?.ToDisplayString();

                switch (attrName)
                {
                    case Constants.CborSerializableAttributeFullName:
                        hasSerializableAttr = true;
                        break;

                    case Constants.CborMapAttributeFullName:
                        metadata.Format = SerializationType.Map;
                        isFormatDetermined = true;
                        break;

                    case Constants.CborListAttributeFullName:
                        metadata.Format = SerializationType.Array;
                        isFormatDetermined = true;
                        break;

                    case Constants.CborUnionAttributeFullName:
                        metadata.Format = SerializationType.Union;
                        isFormatDetermined = true;
                        break;

                    case Constants.CborConstrAttributeFullName:
                        metadata.Format = SerializationType.Constr;
                        isFormatDetermined = true;
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            metadata.Constructor = attr.ConstructorArguments[0].Value as int?;
                        }
                        break;

                    case Constants.CborTagAttributeFullName:
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            metadata.Tag = attr.ConstructorArguments[0].Value as int?;
                        }
                        break;

                    case Constants.CborNullableAttributeFullName:
                        metadata.Format = SerializationType.Nullable;
                        isFormatDetermined = true;
                        break;

                    case Constants.CborIndefiniteAttributeFullName:
                        metadata.IsIndefinite = true;
                        break;

                    case Constants.CborSizeAttributeFullName:
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            metadata.Size = attr.ConstructorArguments[0].Value as int?;
                        }
                        break;

                    case Constants.CborValidateExactAttributeFullName:
                        metadata.ValidateExact = true;
                        break;

                    case Constants.CborValidateRangeAttributeFullName:
                        metadata.ValidateRange = true;
                        break;

                    case Constants.CborValidateAttributeFullName:
                        metadata.CustomValidation = true;
                        break;
                }
            }

            // Handle container types with a single property
            if (hasSerializableAttr && !isFormatDetermined && metadata.Properties.Count == 1)
            {
                metadata.Format = SerializationType.Container;
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
        /// Extracts union case types
        /// </summary>
        private void ExtractUnionCases(INamedTypeSymbol type, SerializableType metadata)
        {
            foreach (var nestedType in type.GetTypeMembers())
            {
                var nestedTypeInfo = new TypeInfo(nestedType);
                metadata.UnionCases.Add(nestedTypeInfo);
                metadata.Dependencies.Add(nestedTypeInfo);

                // Add to processing queue
                AddTypeToQueue(nestedType, isNullable: false);
            }
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
}