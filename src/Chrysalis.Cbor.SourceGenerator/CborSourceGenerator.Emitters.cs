using System.Formats.Cbor;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Chrysalis.Cbor.SourceGenerator;

public sealed partial class CborSourceGenerator
{
    /// <summary>
    /// Handles emitting source code for serialization
    /// </summary>
    private class Emitter(SourceProductionContext context, Compilation? compilation = null)
    {
        private readonly SourceProductionContext _context = context;
        private readonly Compilation? _compilation = compilation;
        private readonly HashSet<string> _usedHintNames = [];
        private readonly HashSet<string> _typesWithExistingMethods = new();

        /// <summary>
        /// Emits source code for all types in the serialization context
        /// </summary>
        public void Emit(SerializationContext serializationContext)
        {
            foreach (var type in serializationContext.Types)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();

                // Check if the type already has CBOR serialization methods to avoid duplicate generation
                string typeFullName = type.Type.FullName;
                
                // Skip types we've already processed
                if (_typesWithExistingMethods.Contains(typeFullName))
                {
                    continue;
                }
                
                // Check if the type already has Write/Read methods defined
                if (_compilation != null)
                {
                    INamedTypeSymbol? typeSymbol = FindTypeSymbol(typeFullName);
                    if (typeSymbol != null)
                    {
                        // Look for static Write and Read methods with the right signatures
                        bool hasWriteMethod = HasMethod(typeSymbol, "Write", new[] { "CborWriter", typeFullName });
                        bool hasReadMethod = HasMethod(typeSymbol, "Read", new[] { "ReadOnlyMemory<byte>", "bool" });
                        
                        if (hasWriteMethod || hasReadMethod)
                        {
                            // Skip generation for this type as it already has serialization methods
                            _typesWithExistingMethods.Add(typeFullName);
                            continue;
                        }
                    }
                }

                // Get the appropriate emitter strategy for the type
                var strategy = GetEmitterStrategy(type);

                // Generate the source code
                string sourceCode = GenerateSourceCode(type, strategy);

                // Generate file name, handling generic types specially
                string baseName;
                if (type.Type.IsGeneric)
                {
                    // For generic types, include the arity in the name (List_1, Dictionary_2, etc.)
                    int typeParamCount = type.Type.TypeParameters.Count;
                    string sanitizedTypeName = type.Type.Name.Replace("`", "_");
                    baseName = $"{sanitizedTypeName}_{typeParamCount}_Cbor.g.cs";
                }
                else
                {
                    baseName = $"{type.Type.Name}_Cbor.g.cs";
                }

                if (_usedHintNames.Contains(baseName)) continue;
                _usedHintNames.Add(baseName);

                // Add the source
                _context.AddSource(baseName, SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        /// <summary>
        /// Find a type symbol by its full name
        /// </summary>
        private INamedTypeSymbol? FindTypeSymbol(string typeFullName)
        {
            if (_compilation == null)
                return null;

            // Parse the namespace and name
            string namespaceName = "";
            string typeName = typeFullName;

            int lastDot = typeFullName.LastIndexOf('.');
            if (lastDot >= 0)
            {
                namespaceName = typeFullName.Substring(0, lastDot);
                typeName = typeFullName.Substring(lastDot + 1);
            }

            // Handle generic types
            if (typeName.Contains("<"))
            {
                typeName = typeName.Substring(0, typeName.IndexOf("<"));
            }

            // Look up the type in compilation
            return _compilation.GetTypeByMetadataName($"{namespaceName}.{typeName}");
        }

        /// <summary>
        /// Check if a type has a method with the specified name and parameter types
        /// </summary>
        private bool HasMethod(INamedTypeSymbol typeSymbol, string methodName, string[] parameterTypes)
        {
            if (typeSymbol == null)
                return false;

            foreach (var member in typeSymbol.GetMembers(methodName))
            {
                if (member is IMethodSymbol method && method.IsStatic)
                {
                    if (method.Parameters.Length == parameterTypes.Length)
                    {
                        bool paramsMatch = true;
                        for (int i = 0; i < parameterTypes.Length; i++)
                        {
                            string paramTypeName = method.Parameters[i].Type.ToDisplayString();
                            if (paramTypeName != parameterTypes[i] && !paramTypeName.EndsWith("." + parameterTypes[i]))
                            {
                                paramsMatch = false;
                                break;
                            }
                        }

                        if (paramsMatch)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a unique hint name for the source file
        /// </summary>
        private string GetUniqueHintName(SerializableType type)
        {
            // Start with a simple name
            string baseName = $"{type.Type.Name}_Cbor.g.cs";

            // If it's unique, use it
            if (!_usedHintNames.Contains(baseName))
            {
                _usedHintNames.Add(baseName);
                return baseName;
            }

            // Otherwise, create a name with namespace
            string sanitizedNamespace = type.Type.Namespace.Replace(".", "_");
            string nameWithNamespace = $"{sanitizedNamespace}_{type.Type.Name}_Cbor.g.cs";

            if (!_usedHintNames.Contains(nameWithNamespace))
            {
                _usedHintNames.Add(nameWithNamespace);
                return nameWithNamespace;
            }

            // If still not unique, add a counter
            int counter = 1;
            string nameWithCounter;
            do
            {
                nameWithCounter = $"{sanitizedNamespace}_{type.Type.Name}_{counter}_Cbor.g.cs";
                counter++;
            } while (_usedHintNames.Contains(nameWithCounter));

            _usedHintNames.Add(nameWithCounter);
            return nameWithCounter;
        }

        /// <summary>
        /// Get the appropriate emitter strategy for a type
        /// </summary>
        private IEmitterStrategy GetEmitterStrategy(SerializableType type)
        {
            // Switch on the serialization format to get the right strategy
            return type.Format switch
            {
                SerializationType.Map => new MapEmitterStrategy(),
                SerializationType.Array => new ArrayEmitterStrategy(),
                SerializationType.Constr => new ConstrEmitterStrategy(),
                SerializationType.Union => new UnionEmitterStrategy(),
                SerializationType.Container => new ContainerEmitterStrategy(),
                SerializationType.Nullable => CreateNullableStrategy(type),
                SerializationType.Encoded => new EncodedEmitterStrategy(),
                _ => new MapEmitterStrategy() // Default
            };
        }

        /// <summary>
        /// Creates a nullable strategy with the appropriate inner strategy
        /// </summary>
        private NullableEmitterStrategy CreateNullableStrategy(SerializableType type)
        {
            // Ensure we have a valid inner type
            if (type.InnerType == null)
            {
                // Default to the same type if inner type is not set
                return new NullableEmitterStrategy(type.Type);
            }
            return new NullableEmitterStrategy(type.InnerType);
        }


        /// <summary>
        /// Generates the complete source code for a type
        /// </summary>
        private string GenerateSourceCode(SerializableType type, IEmitterStrategy strategy)
        {
            // For generic types, use specialized code generation
            if (type.Type.IsGeneric)
            {
                return GenerateGenericSourceCode(type, strategy);
            }

            // Standard non-generic code generation
            string serializerCode = strategy.EmitSerializer(type);
            string deserializerCode = strategy.EmitDeserializer(type);

            var validatorSb = new StringBuilder();
            ValidationHelpers.AddValidatorInstance(validatorSb, type);
            string validatorCode = validatorSb.ToString();

            // Define the 'new' keyword based on whether we need to hide methods
            string writeNewKeyword = type.HasBaseWriteMethod ? "new " : "";
            string readNewKeyword = type.HasBaseReadMethod ? "new " : "";

            // Check if this type needs the WriteGenericValue method
            bool needsGenericValueHelper = NeedsGenericValueHelper(type);
            string genericValueHelper = needsGenericValueHelper ? GenerateWriteGenericValueMethod() : "";

            // Add any helper methods at the class level
            string helperMethods = string.Empty;
            if (!string.IsNullOrEmpty(type.SerializerHelperMethods))
            {
                helperMethods += type.SerializerHelperMethods;
            }
            if (!string.IsNullOrEmpty(type.DeserializerHelperMethods))
            {
                helperMethods += type.DeserializerHelperMethods;
            }

            return $$"""
                #nullable enable
                // <auto-generated/>
                #pragma warning disable CS0109 // Ignore warnings about unnecessary 'new' keyword
                #pragma warning disable CS8604
                #pragma warning disable CS8603 
                #pragma warning disable CS8600
                #pragma warning disable CS0693
                #pragma warning disable CS8625
                #pragma warning disable CS8601
                #pragma warning disable CS8629

                using System;
                using System.Collections.Generic;
                using System.Formats.Cbor;
                using System.Reflection;
                using Chrysalis.Cbor.Types;
                using Chrysalis.Cbor.Types.Custom;
                using Chrysalis.Cbor.Serialization.Attributes;
                

                namespace {{type.Type.Namespace}};

                public partial {{GetTypeKeyword(type)}} {{type.Type.Name}}
                {
                    {{validatorCode}}
                    // Serialization implementation
                    public static {{writeNewKeyword}}void Write(CborWriter writer, {{type.Type.FullName}} value)
                    {
                        {{serializerCode}}
                    }

                    // Deserialization implementation
                    public static {{readNewKeyword}}{{type.Type.FullName}}? Read(ReadOnlyMemory<byte> data, bool preserveRaw = false)
                    {
                        {{deserializerCode}}
                    }

                    {{helperMethods}}
                    {{genericValueHelper}}
                }
                
                #pragma warning restore CS0109 
                #pragma warning restore CS8604
                #pragma warning restore CS8603 
                #pragma warning restore CS8600
                #pragma warning restore CS0693
                #pragma warning restore CS8625
                #pragma warning restore CS8601
                #pragma warning restore CS8629
            """;
        }

        /// <summary>
        /// Generates the complete source code for a generic type
        /// </summary>
        private string GenerateGenericSourceCode(SerializableType type, IEmitterStrategy strategy)
        {
            string serializerCode = strategy.EmitSerializer(type);
            string deserializerCode = strategy.EmitDeserializer(type);

            var validatorSb = new StringBuilder();
            ValidationHelpers.AddValidatorInstance(validatorSb, type);
            string validatorCode = validatorSb.ToString();

            // For generics, include the helper methods for generic deserialization
            string deserializationHelper = GenerateGenericDeserializationHelper();

            // Add any additional helper methods at the class level
            string helperMethods = deserializationHelper;
            if (!string.IsNullOrEmpty(type.SerializerHelperMethods))
            {
                helperMethods += "\n" + type.SerializerHelperMethods;
            }
            if (!string.IsNullOrEmpty(type.DeserializerHelperMethods))
            {
                helperMethods += "\n" + type.DeserializerHelperMethods;
            }

            // We need to make sure we have a clean and useful type parameter list
            // If no type parameters are available, use T as a placeholder
            string typeParams = string.Join(", ", type.Type.TypeParameters.Select(tp => tp.Name));
            if (string.IsNullOrEmpty(typeParams))
            {
                typeParams = "T";
            }

            // Extract all unique type parameters that appear in the FullName
            var allTypeParams = new HashSet<string>();

            // Add the explicitly declared type parameters
            foreach (var param in typeParams.Split(',').Select(p => p.Trim()))
            {
                allTypeParams.Add(param);
            }

            // Scan the full type name for other type parameters that might be used
            // This helps with complex nested types like Option<T>.Some<U>
            if (type.Type.FullName.Contains("<"))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(type.Type.FullName, "<([A-Za-z0-9_]+)>");
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        string param = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(param))
                        {
                            allTypeParams.Add(param);
                        }
                    }
                }
            }

            // Create using directives for all type parameters to make them available in the file
            var typeParamDirectives = new List<string>();
            foreach (var param in allTypeParams)
            {
                typeParamDirectives.Add($"using {param} = global::System.Object; // Type parameter");
            }

            // Join with newlines for proper formatting
            string typeParamDeclarations = string.Join("\n", typeParamDirectives);

            // Define the 'new' keyword based on whether we need to hide methods
            string writeNewKeyword = type.HasBaseWriteMethod ? "new " : "";
            string readNewKeyword = type.HasBaseReadMethod ? "new " : "";

            // Check if this type needs the WriteGenericValue method
            bool needsGenericValueHelper = NeedsGenericValueHelper(type);
            string genericValueHelper = needsGenericValueHelper ? GenerateWriteGenericValueMethod() : "";

            return $$"""
                #nullable enable
                // <auto-generated/>
                #pragma warning disable CS0109 // Ignore warnings about unnecessary 'new' keyword
                #pragma warning disable CS8604
                #pragma warning disable CS8603 
                #pragma warning disable CS8600
                #pragma warning disable CS0693
                #pragma warning disable CS8625
                #pragma warning disable CS8601
                #pragma warning disable CS8629

                using System;
                using System.Collections.Generic;
                using System.Formats.Cbor;
                using System.Reflection;
                using Chrysalis.Cbor.Types;
                using Chrysalis.Cbor.Types.Custom;
                using Chrysalis.Cbor.Serialization.Attributes;
                

                namespace {{type.Type.Namespace}};
                
                {{typeParamDeclarations}}

                public partial {{GetTypeKeyword(type)}} {{type.Type.Name}}<{{typeParams}}>
                {
                    {{validatorCode}}
                    
                    // Serialization implementation
                    public static {{writeNewKeyword}}void Write(CborWriter writer, {{type.Type.FullName}} value)
                    {
                        {{serializerCode}}
                    }

                    // Deserialization implementation
                    public static {{readNewKeyword}}{{type.Type.FullName}}? Read(ReadOnlyMemory<byte> data, bool preserveRaw = false)
                    {
                        {{deserializerCode}}
                    }
                    
                    {{helperMethods}}
                    
                    {{genericValueHelper}}
                }

                #pragma warning restore CS0109 
                #pragma warning restore CS8604
                #pragma warning restore CS8603 
                #pragma warning restore CS8600
                #pragma warning restore CS0693
                #pragma warning restore CS8625
                #pragma warning restore CS8601
                #pragma warning restore CS8629
            """;
        }

        /// <summary>
        /// Determines if a type needs the WriteGenericValue helper method
        /// </summary>
        private bool NeedsGenericValueHelper(SerializableType type)
        {
            // If it's not generic, check for list/dictionary properties that contain generic types
            if (!type.Type.IsGeneric)
            {
                foreach (var prop in type.Properties)
                {
                    if (IsCollection(prop.Type.FullName))
                    {
                        // Check if element or key type is a generic parameter
                        if (prop.ElementType != null && ContainsGenericParameters(prop.ElementType.FullName))
                            return true;

                        if (prop.KeyType != null && ContainsGenericParameters(prop.KeyType.FullName))
                            return true;
                    }
                }

                return false;
            }

            // For generic types, always include the helper
            return true;
        }

        /// <summary>
        /// Determines if a type is a collection
        /// </summary>
        private bool IsCollection(string typeName)
        {
            return IsListType(typeName) || IsDictionaryType(typeName);
        }

        /// <summary>
        /// Determines if a type is a list-like collection
        /// </summary>
        private bool IsListType(string typeName)
        {
            return typeName.Contains("System.Collections.Generic.List<") ||
                   typeName.Contains("System.Collections.Generic.IList<") ||
                   typeName.Contains("System.Collections.Generic.ICollection<") ||
                   typeName.Contains("System.Collections.Generic.IEnumerable<");
        }

        /// <summary>
        /// Determines if a type is a dictionary-like collection
        /// </summary>
        private bool IsDictionaryType(string typeName)
        {
            return typeName.Contains("System.Collections.Generic.Dictionary<") ||
                   typeName.Contains("System.Collections.Generic.IDictionary<");
        }

        /// <summary>
        /// Determines if a type name contains any generic parameters like T, U, etc.
        /// </summary>
        private bool ContainsGenericParameters(string typeName)
        {
            // Check for common type parameter patterns
            if (typeName.Length == 1 && char.IsUpper(typeName[0]))
                return true;

            // Check for standalone type parameters
            if (typeName == "T" || typeName == "U" || typeName == "V" ||
                typeName == "TKey" || typeName == "TValue")
                return true;

            // Check for generic parameters inside angle brackets
            if (typeName.Contains('<') && typeName.Contains('>'))
            {
                int startBracket = typeName.IndexOf('<');
                int endBracket = typeName.LastIndexOf('>');

                if (startBracket >= 0 && endBracket > startBracket)
                {
                    string paramList = typeName.Substring(startBracket + 1, endBracket - startBracket - 1);
                    string[] parameters = paramList.Split(',');

                    foreach (var param in parameters)
                    {
                        string trimmedParam = param.Trim();

                        // Check if this parameter itself is a generic parameter
                        if (trimmedParam.Length == 1 && char.IsUpper(trimmedParam[0]))
                            return true;

                        // Check for common type parameter names
                        if (trimmedParam == "T" || trimmedParam == "U" || trimmedParam == "V" ||
                            trimmedParam == "TKey" || trimmedParam == "TValue")
                            return true;

                        // Recursively check for nested generic parameters
                        if (ContainsGenericParameters(trimmedParam))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Generates the WriteGenericValue method for serializing generic values
        /// </summary>
        private string GenerateWriteGenericValueMethod()
        {
            return """
                /// <summary>
                /// Writes any generic type to a CBOR writer based on runtime type
                /// </summary>
                private static void WriteGenericValue<T>(CborWriter writer, T value)
                {
                    if (value == null)
                    {
                        writer.WriteNull();
                        return;
                    }

                    Type valueType = value.GetType();

                    // Handle primitive types first (most common case)
                    if (valueType == typeof(int))
                        writer.WriteInt32((int)(object)value);
                    else if (valueType == typeof(uint))
                        writer.WriteUInt32((uint)(object)value);
                    else if (valueType == typeof(long))
                        writer.WriteInt64((long)(object)value);
                    else if (valueType == typeof(ulong))
                        writer.WriteUInt64((ulong)(object)value);
                    else if (valueType == typeof(float))
                        writer.WriteSingle((float)(object)value);
                    else if (valueType == typeof(double))
                        writer.WriteDouble((double)(object)value);
                    else if (valueType == typeof(decimal))
                        writer.WriteDouble((double)(decimal)(object)value);
                    else if (valueType == typeof(bool))
                        writer.WriteBoolean((bool)(object)value);
                    else if (valueType == typeof(string))
                        writer.WriteTextString((string)(object)value);
                    else if (valueType == typeof(byte[]))
                        writer.WriteByteString((byte[])(object)value);
                    else if (valueType == typeof(DateTime))
                        writer.WriteTextString(((DateTime)(object)value).ToString("o"));
                    else if (valueType == typeof(Guid))
                        writer.WriteTextString(((Guid)(object)value).ToString());
                    else
                    {
                        // Check if the type has a Raw property for direct serialization
                        PropertyInfo rawProperty = valueType.GetProperty("Raw");
                        if (rawProperty != null && 
                            rawProperty.PropertyType.IsGenericType && 
                            rawProperty.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                            rawProperty.PropertyType.GetGenericArguments()[0] == typeof(ReadOnlyMemory<byte>))
                        {
                            var rawValue = rawProperty.GetValue(value);
                            if (rawValue != null)
                            {
                                var memory = (ReadOnlyMemory<byte>?)rawValue;
                                if (memory.HasValue)
                                {
                                    writer.WriteEncodedValue(memory.Value.Span);
                                    return;
                                }
                            }
                        }

                        // Try to find and invoke a static Write method
                        MethodInfo writeMethod = valueType.GetMethod("Write", 
                            BindingFlags.Public | BindingFlags.Static, 
                            null, 
                            new[] { typeof(CborWriter), valueType }, 
                            null);
                            
                        if (writeMethod != null)
                        {
                            writeMethod.Invoke(null, new object[] { writer, value });
                            return;
                        }
                        
                        // Final fallback
                        writer.WriteTextString(value.ToString());
                    }
                }
            """;
        }

        /// <summary>
        /// Determines the C# keyword for the type declaration
        /// </summary>
        private string GetTypeKeyword(SerializableType type)
        {
            if (type.Type.IsRecord)
                return "record";
            else if (!type.Type.IsValueType)
                return "class";
            else
                return "struct";
        }
    }

    /// <summary>
    /// Strategy interface for emitting different types of CBOR serialization
    /// </summary>
    private interface IEmitterStrategy
    {
        string EmitSerializer(SerializableType type);
        string EmitDeserializer(SerializableType type);
    }


    /// <summary>
    /// Base implementation for emitter strategies
    /// </summary>
    private abstract class EmitterStrategyBase : IEmitterStrategy
    {
        public virtual string EmitSerializer(SerializableType type)
        {
            return "// Not implemented";
        }

        public virtual string EmitDeserializer(SerializableType type)
        {
            return "// Not implemented";
        }
    }

    /// <summary>
    /// Strategy for emitting Map type serialization
    /// </summary>
    private class MapEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Add validation if needed
            ValidationHelpers.AddSerializationValidation(sb, type);

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"writer.WriteTag((CborTag){type.Tag.Value});");
            }

            // Start the map
            sb.AppendLine("writer.WriteStartMap(null);");

            // Write each property
            foreach (var prop in type.Properties)
            {
                sb.AppendLine($"// Write property: {prop.Name}");
                sb.AppendLine($"writer.WriteTextString(\"{prop.Key}\");");
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, prop.IsCborNullable));
            }

            sb.AppendLine("writer.WriteEndMap();");

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Store the original data for later
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("var reader = new CborReader(data);");

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"if (reader.ReadTag() != (CborTag){type.Tag.Value}) throw new Exception(\"Invalid tag\");");
            }

            // Start reading the map
            sb.AppendLine("reader.ReadStartMap();");

            // Declare local variables for properties
            foreach (var prop in type.Properties)
            {
                sb.AppendLine($"{prop.Type.FullName} {prop.Name} = default;");
            }

            // Read map entries - use a unique variable name for the map key
            sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            sb.AppendLine("{");
            sb.AppendLine("    string mapEntryKey = reader.ReadTextString();");
            sb.AppendLine("    switch (mapEntryKey)");
            sb.AppendLine("    {");

            foreach (var prop in type.Properties)
            {
                sb.AppendLine($"        case \"{prop.Key}\":");

                // Get the read code and ensure variable names don't conflict
                string readCode = GenericEmitterStrategy.GenerateReadCode(prop.Type.FullName, prop.Name, prop.IsPropertyNullable);
                // If there are dictionaries inside, we need to make sure their variable names don't conflict
                readCode = readCode.Replace(" key = ", $" dictKey_{prop.Name} = ");
                readCode = readCode.Replace(" value = ", $" dictValue_{prop.Name} = ");

                sb.AppendLine($"            {readCode}");
                sb.AppendLine("            break;");
            }

            sb.AppendLine("        default:");
            sb.AppendLine("            reader.SkipValue();");
            sb.AppendLine("            break;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("reader.ReadEndMap();");

            // Create the result and store raw bytes
            sb.AppendLine($"var result = new {type.Type.FullName}(");
            for (int i = 0; i < type.Properties.Count; i++)
            {
                var prop = type.Properties[i];
                string suffix = i < type.Properties.Count - 1 ? "," : "";
                sb.AppendLine($"    {prop.Name}: {prop.Name}{suffix}");
            }
            sb.AppendLine(");");

            // Add validation if needed
            ValidationHelpers.AddDeserializationValidation(sb, type);

            // If preserveRaw is true, store the raw data
            sb.AppendLine("if (preserveRaw)");
            sb.AppendLine("{");
            sb.AppendLine("    result.Raw = originalData;");
            sb.AppendLine("}");

            sb.AppendLine("return result;");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Strategy for emitting Array type serialization
    /// </summary>
    private class ArrayEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Add validation if needed
            ValidationHelpers.AddSerializationValidation(sb, type);

            // Handle tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"writer.WriteTag((CborTag){type.Tag.Value});");
            }

            // Handle indefinite arrays
            bool isIndefinite = type.IsIndefinite;

            // Start the array
            sb.AppendLine(isIndefinite
                ? "writer.WriteStartArray(null);"
                : $"writer.WriteStartArray({type.Properties.Count});");

            // Write properties in order
            var orderedProperties = type.Properties
                .OrderBy(p => p.Order ?? int.MaxValue)
                .ToList();

            foreach (SerializableProperty? prop in orderedProperties)
            {
                sb.AppendLine($"// Write property: {prop.Name}");
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, prop.IsCborNullable));
            }

            sb.AppendLine("writer.WriteEndArray();");

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Store original data for later
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("var reader = new CborReader(data);");

            // Handle tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"if (reader.ReadTag() != (CborTag){type.Tag.Value}) throw new Exception(\"Invalid tag\");");
            }

            // Start reading array
            sb.AppendLine("reader.ReadStartArray();");

            // Declare local variables for properties
            var orderedProperties = type.Properties
                .OrderBy(p => p.Order ?? int.MaxValue)
                .ToList();

            foreach (var prop in orderedProperties)
            {
                sb.AppendLine($"{prop.Type.FullName} {prop.Name} = default;");
            }

            // Read array values in order
            for (int i = 0; i < orderedProperties.Count; i++)
            {
                var prop = orderedProperties[i];
                sb.AppendLine($"// Read property: {prop.Name}");
                sb.AppendLine(GenericEmitterStrategy.GenerateReadCode(prop.Type.FullName, prop.Name, prop.IsPropertyNullable));
            }

            sb.AppendLine("reader.ReadEndArray();");

            // Create the result and store raw bytes
            sb.AppendLine($"var result = new {type.Type.FullName}(");
            for (int i = 0; i < orderedProperties.Count; i++)
            {
                var prop = orderedProperties[i];
                string suffix = i < orderedProperties.Count - 1 ? "," : "";
                sb.AppendLine($"    {prop.Name}: {prop.Name}{suffix}");
            }
            sb.AppendLine(");");

            // Add validation if needed
            ValidationHelpers.AddDeserializationValidation(sb, type);

            // If preserveRaw is true, store the raw data
            sb.AppendLine("if (preserveRaw)");
            sb.AppendLine("{");
            sb.AppendLine("    result.Raw = originalData;");
            sb.AppendLine("}");

            sb.AppendLine("return result;");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Strategy for emitting Constructor type serialization
    /// </summary>
    private class ConstrEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Add validation if needed
            ValidationHelpers.AddSerializationValidation(sb, type);

            sb.AppendLine($"writer.WriteTag((CborTag){ResolveTag(type.Constructor)});");

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"writer.WriteTag((CborTag){type.Tag.Value});");
            }

            sb.AppendLine($"writer.WriteStartArray({(type.IsIndefinite ? "null" : type.Properties.Count)});");

            // Write properties in order
            var orderedProperties = type.Properties
                .OrderBy(p => p.Order ?? int.MaxValue)
                .ToList();

            foreach (var prop in orderedProperties)
            {
                sb.AppendLine($"// Write property: {prop.Name}");
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, prop.IsCborNullable));
            }

            // End the array
            sb.AppendLine("writer.WriteEndArray();");

            return sb.ToString();
        }

        public static int ResolveTag(int? index)
        {
            int finalIndex = index > 6 ? 1280 - 7 : 121;
            return finalIndex + (index ?? 0);
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Store the original data for later
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("var reader = new CborReader(data);");
            sb.AppendLine("var cborTag = reader.ReadTag();");

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"if (reader.ReadTag() != (CborTag){type.Tag.Value}) throw new Exception(\"Invalid tag\");");
            }

            // Start reading the array
            sb.AppendLine("reader.ReadStartArray();");

            // Read constructor index
            int expectedIndex = ResolveTag(type.Constructor);
            sb.AppendLine($"if ((int)cborTag != {expectedIndex}) throw new Exception($\"Invalid constructor index: {{cborTag}}, expected: {expectedIndex}\");");

            // Declare local variables for properties
            var orderedProperties = type.Properties
                .OrderBy(p => p.Order ?? int.MaxValue)
                .ToList();

            foreach (var prop in orderedProperties)
            {
                sb.AppendLine($"{prop.Type.FullName} {prop.Name} = default;");
            }

            // Read properties in order
            for (int i = 0; i < orderedProperties.Count; i++)
            {
                var prop = orderedProperties[i];
                sb.AppendLine($"// Read property: {prop.Name}");
                sb.AppendLine(GenericEmitterStrategy.GenerateReadCode(prop.Type.FullName, prop.Name, !prop.Type.IsValueType));
            }

            sb.AppendLine("reader.ReadEndArray();");

            // Create the result and store raw bytes
            sb.AppendLine($"var result = new {type.Type.FullName}(");
            for (int i = 0; i < orderedProperties.Count; i++)
            {
                var prop = orderedProperties[i];
                string suffix = i < orderedProperties.Count - 1 ? "," : "";
                sb.AppendLine($"    {prop.Name}: {prop.Name}{suffix}");
            }
            sb.AppendLine(");");

            // Add validation if needed
            ValidationHelpers.AddDeserializationValidation(sb, type);

            // If preserveRaw is true, store the raw data
            sb.AppendLine("if (preserveRaw)");
            sb.AppendLine("{");
            sb.AppendLine("    result.Raw = originalData;");
            sb.AppendLine("}");

            sb.AppendLine("return result;");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Strategy for emitting Union type serialization
    /// </summary>
    private class UnionEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();
            
            // Add helper methods for nested types, but now try to reuse existing methods
            var nestedHelperMethods = new StringBuilder();
            var helperMethodDeclarations = new StringBuilder();
            
            // We no longer need to create stub implementations for nested types
            // Only if we need to forward from child serializers to parent serializers

            // Add validation if needed
            ValidationHelpers.AddSerializationValidation(sb, type);

            // Main switch statement for determining which serializer to use
            sb.AppendLine("// Determine the concrete type from its type name");
            sb.AppendLine("switch (value.CborTypeName)");
            sb.AppendLine("{");

            foreach (var unionCase in type.UnionCases)
            {
                bool isNestedType = unionCase.FullName.StartsWith(type.Type.FullName + ".");
                sb.AppendLine($"    case \"{unionCase.Name}\":");
                
                // Whether nested or not, use the type's own Write method if available
                string fullyQualifiedName = unionCase.FullName.Contains(".") ? unionCase.FullName : $"global::{unionCase.FullName}";
                sb.AppendLine($"        {fullyQualifiedName}.Write(writer, ({unionCase.FullName})value);");
                sb.AppendLine("        break;");
            }

            sb.AppendLine("    default:");
            sb.AppendLine("        throw new Exception($\"Unknown union type: {value.CborTypeName}\");");
            sb.AppendLine("}");

            // We no longer need helper methods
            type.SerializerHelperMethods = string.Empty;

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();
            
            // We no longer need to create helper methods for nested types
            // Instead we'll directly use each subtype's Read method

            // The main deserialization code
            sb.AppendLine("// Try each union case");
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("Exception lastException = null;");

            foreach (var unionCase in type.UnionCases)
            {
                sb.AppendLine($"// Try {unionCase.Name}");
                sb.AppendLine("try");
                sb.AppendLine("{");
                
                // Whether nested or not, use the type's own Read method
                string fullyQualifiedName = unionCase.FullName.Contains(".") ? unionCase.FullName : $"global::{unionCase.FullName}";
                sb.AppendLine($"    var result = ({type.Type.FullName}){fullyQualifiedName}.Read(originalData, preserveRaw);");

                // If we get here, it means the deserialization was successful
                // Add validation if needed - immediately after a successful deserialization
                if (type.HasValidator)
                {
                    sb.AppendLine();
                    sb.AppendLine("    // Validate deserialized object");
                    sb.AppendLine($"    if (!_validator.Validate(result))");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        throw new System.InvalidOperationException(\"Validation failed for {type.Type.Name}\");");
                    sb.AppendLine("    }");
                }

                sb.AppendLine("    return result; // Successfully deserialized and validated");
                sb.AppendLine("}");
                sb.AppendLine("catch (Exception ex)");
                sb.AppendLine("{");
                sb.AppendLine("    lastException = ex;");
                sb.AppendLine("    // Continue to the next case");
                sb.AppendLine("}");
            }

            // If we get here, all cases failed
            sb.AppendLine("throw new Exception(\"Could not deserialize union type\", lastException);");

            // We no longer need helper methods
            type.DeserializerHelperMethods = string.Empty;

            return sb.ToString();
        }
    }

    /// <summary>
    /// Strategy for emitting Container type serialization
    /// </summary>
    private class ContainerEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Check if we have a single property
            if (type.Properties.Count != 1)
            {
                sb.AppendLine("throw new Exception(\"Container type must have exactly one property\");");
                return sb.ToString();
            }

            var prop = type.Properties[0];

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"writer.WriteTag((CborTag){type.Tag.Value});");
            }

            // Write the property value directly
            sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, prop.IsCborNullable));

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Check if we have a single property
            if (type.Properties.Count != 1)
            {
                sb.AppendLine("throw new Exception(\"Container type must have exactly one property\");");
                return sb.ToString();
            }

            var prop = type.Properties[0];

            // Store original data for later
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("var reader = new CborReader(data);");

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"if (reader.ReadTag() != (CborTag){type.Tag.Value}) throw new Exception(\"Invalid tag\");");
            }

            // Read the property value
            sb.AppendLine($"{prop.Type.FullName} {prop.Name} = default;");
            sb.AppendLine(GenericEmitterStrategy.GenerateReadCode(prop.Type.FullName, prop.Name, !prop.Type.IsValueType));

            // Create the result and store raw bytes
            sb.AppendLine($"var result = new {type.Type.FullName}({prop.Name});");

            // If preserveRaw is true, store the raw data
            sb.AppendLine("if (preserveRaw)");
            sb.AppendLine("{");
            sb.AppendLine("    result.Raw = originalData;");
            sb.AppendLine("}");

            sb.AppendLine("return result;");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Strategy for emitting Nullable type serialization
    /// </summary>
    private class NullableEmitterStrategy(TypeInfo innerTypeInfo) : EmitterStrategyBase
    {
        private readonly TypeInfo _innerTypeInfo = innerTypeInfo;

        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Null check first
            sb.AppendLine("// Nullable serialization wrapper");
            sb.AppendLine("if (value == null)");
            sb.AppendLine("{");
            sb.AppendLine("    writer.WriteNull();");
            sb.AppendLine("    return;");
            sb.AppendLine("}");
            sb.AppendLine();

            // Inner serialization
            sb.AppendLine("// Delegate to inner type serialization");

            // Check if it's a primitive type
            if (GenericEmitterStrategy.IsPrimitive(_innerTypeInfo.FullName))
            {
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode("value", _innerTypeInfo.FullName, false));
            }
            else
            {
                sb.AppendLine($"{_innerTypeInfo.FullName}.Write(writer, value);");
            }

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Store original data for later
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("var reader = new CborReader(data);");

            // Check for null
            sb.AppendLine("// Nullable deserialization wrapper");
            sb.AppendLine("if (reader.PeekState() == CborReaderState.Null)");
            sb.AppendLine("{");
            sb.AppendLine("    reader.ReadNull();");
            sb.AppendLine("    return null;");
            sb.AppendLine("}");
            sb.AppendLine();

            // Inner deserialization
            sb.AppendLine("// Delegate to inner type deserialization");

            // Check if it's a primitive type
            if (GenericEmitterStrategy.IsPrimitive(_innerTypeInfo.FullName))
            {
                sb.AppendLine($"{_innerTypeInfo.FullName} result = default;");
                sb.AppendLine(GenericEmitterStrategy.GenerateReadCode(_innerTypeInfo.FullName, "result", false));
                sb.AppendLine("return result;");
            }
            else
            {
                sb.AppendLine($"return {_innerTypeInfo.FullName}.Read(originalData, preserveRaw);");
            }

            return sb.ToString();
        }
    }
    /// <summary>
    /// Strategy for emitting Encoded type serialization (CborEncodedValue)
    /// </summary>
    private class EncodedEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Add validation if needed
            ValidationHelpers.AddSerializationValidation(sb, type);

            // For CborEncodedValue, we write the tag and the byte array
            sb.AppendLine("// Write pre-encoded CBOR data");
            sb.AppendLine("writer.WriteTag(CborTag.EncodedCborDataItem);");
            sb.AppendLine("writer.WriteByteString(value.Value);");

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Store original data for later
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("var reader = new CborReader(data);");

            // Check for encoded CBOR tag
            sb.AppendLine("// Check for encoded CBOR tag");
            sb.AppendLine("if (reader.PeekState() == CborReaderState.Tag)");
            sb.AppendLine("{");
            sb.AppendLine("    var tag = reader.ReadTag();");
            sb.AppendLine("    if (tag != CborTag.EncodedCborDataItem)");
            sb.AppendLine("    {");
            sb.AppendLine("        throw new Exception($\"Expected EncodedCborDataItem tag, but got: {tag}\");");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            // Read the byte string
            sb.AppendLine("// Read the byte string");
            sb.AppendLine("byte[] rawValue = reader.ReadByteString();");

            // Create result
            sb.AppendLine("// Create result");
            sb.AppendLine($"var result = new {type.Type.FullName}(rawValue);");

            // Add validation if needed
            ValidationHelpers.AddDeserializationValidation(sb, type);

            // Store raw data if needed
            sb.AppendLine();
            sb.AppendLine("// Store raw data if needed");
            sb.AppendLine("if (preserveRaw)");
            sb.AppendLine("{");
            sb.AppendLine("    result.Raw = originalData;");
            sb.AppendLine("}");

            sb.AppendLine();
            sb.AppendLine("return result;");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Helper class containing validation code generation methods
    /// </summary>
    private static class ValidationHelpers
    {
        /// <summary>
        /// Adds validator instance field to a class
        /// </summary>
        public static void AddValidatorInstance(StringBuilder sb, SerializableType type)
        {
            if (!type.HasValidator)
                return;

            sb.AppendLine("    // Validator singleton instance");
            sb.AppendLine($"    private static readonly {type.ValidatorTypeName} _validator = new {type.ValidatorTypeName}();");
            sb.AppendLine();
        }

        /// <summary>
        /// Adds validation code for serialization
        /// </summary>
        public static void AddSerializationValidation(StringBuilder sb, SerializableType type)
        {
            if (!type.HasValidator)
                return;

            sb.AppendLine("// Validate object before serialization");
            sb.AppendLine($"if (!_validator.Validate(value))");
            sb.AppendLine("{");
            sb.AppendLine($"    throw new System.InvalidOperationException(\"Validation failed for {type.Type.Name}\");");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        /// <summary>
        /// Adds validation code for deserialization
        /// </summary>
        public static void AddDeserializationValidation(StringBuilder sb, SerializableType type, string resultVariable = "result")
        {
            if (!type.HasValidator)
                return;

            sb.AppendLine();
            sb.AppendLine("// Validate deserialized object");
            sb.AppendLine($"if (!_validator.Validate({resultVariable}))");
            sb.AppendLine("{");
            sb.AppendLine($"    throw new System.InvalidOperationException(\"Validation failed for {type.Type.Name}\");");
            sb.AppendLine("}");
        }
    }

    /// <summary>
    /// Generates code to write a collection of generic elements
    /// </summary>
    private static string GenerateGenericCollectionWriteCode(string collectionName, string elementTypeName)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"writer.WriteStartArray({collectionName}.Count);");
        sb.AppendLine($"foreach (var item in {collectionName})");
        sb.AppendLine("{");

        // Handle the generic element carefully - need type checking to avoid invalid code
        if (elementTypeName == "T" || elementTypeName.Contains("<T>") || elementTypeName.EndsWith("<T"))
        {
            // For pure generic type parameters, we need reflection to find the Write method
            sb.AppendLine("    // Use reflection to find the appropriate Write method for generic type");
            sb.AppendLine("    var elementType = item?.GetType();");
            sb.AppendLine("    if (item == null)");
            sb.AppendLine("    {");
            sb.AppendLine("        writer.WriteNull();");
            sb.AppendLine("    }");
            sb.AppendLine("    else if (elementType != null)");
            sb.AppendLine("    {");
            sb.AppendLine("        // Try to find a static Write method on the type");
            sb.AppendLine("        var writeMethod = elementType.GetMethod(\"Write\", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(CborWriter), elementType });");
            sb.AppendLine("        if (writeMethod != null)");
            sb.AppendLine("        {");
            sb.AppendLine("            writeMethod.Invoke(null, new object[] { writer, item });");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            // Fallback for primitive types");
            sb.AppendLine("            WriteValue(writer, item);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        else
        {
            // For concrete types, we can call their Write method directly
            sb.AppendLine($"    if (item == null)");
            sb.AppendLine("    {");
            sb.AppendLine("        writer.WriteNull();");
            sb.AppendLine("    }");
            sb.AppendLine("    else");
            sb.AppendLine("    {");
            sb.AppendLine($"        {elementTypeName}.Write(writer, item);");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        sb.AppendLine("writer.WriteEndArray();");

        return sb.ToString();
    }

    /// <summary>
    /// Generates code to read a collection of generic elements
    /// </summary>
    private static string GenerateGenericCollectionReadCode(string collectionVarName, string elementTypeName, string resultCollectionType)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"if (reader.PeekState() == CborReaderState.Null)");
        sb.AppendLine("{");
        sb.AppendLine("    reader.ReadNull();");
        sb.AppendLine($"    {collectionVarName} = null;");
        sb.AppendLine("}");
        sb.AppendLine("else");
        sb.AppendLine("{");
        sb.AppendLine($"    {collectionVarName} = new {resultCollectionType}();");
        sb.AppendLine("    reader.ReadStartArray();");
        sb.AppendLine("    while (reader.PeekState() != CborReaderState.EndArray)");
        sb.AppendLine("    {");

        // Handle generic element type carefully
        if (elementTypeName == "T" || elementTypeName.Contains("<T>") || elementTypeName.EndsWith("<T"))
        {
            // For pure generic parameters, we need a different approach
            sb.AppendLine("        // Handling generic type parameter");
            sb.AppendLine("        if (reader.PeekState() == CborReaderState.Null)");
            sb.AppendLine("        {");
            sb.AppendLine("            reader.ReadNull();");
            sb.AppendLine("            // Need to use default for type parameter to handle value types");
            sb.AppendLine($"            {collectionVarName}.Add(default);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            // Read the encoded value for later use");
            sb.AppendLine("            var encodedValue = reader.ReadEncodedValue();");
            sb.AppendLine("            ");
            sb.AppendLine("            // Since T is a type parameter, we need to find the appropriate deserialization");
            sb.AppendLine("            // This will be handled by the runtime based on the concrete type being used");
            sb.AppendLine("            // For now, we need to rely on external deserialization");
            sb.AppendLine("            var deserializedValue = DeserializeGenericValue<T>(encodedValue);");
            sb.AppendLine($"            {collectionVarName}.Add(deserializedValue);");
            sb.AppendLine("        }");
        }
        else
        {
            // For concrete types, we can call their Read method directly
            sb.AppendLine($"        {elementTypeName} element = default;");
            sb.AppendLine("        if (reader.PeekState() == CborReaderState.Null)");
            sb.AppendLine("        {");
            sb.AppendLine("            reader.ReadNull();");
            sb.AppendLine("            element = null;");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            // Read the encoded value as ReadOnlyMemory<byte>");
            sb.AppendLine("            var encodedValue = reader.ReadEncodedValue();");
            sb.AppendLine("            ");
            sb.AppendLine($"            // Deserialize using the type's Read method");
            sb.AppendLine($"            element = {elementTypeName}.Read(encodedValue);");
            sb.AppendLine("        }");
            sb.AppendLine($"        {collectionVarName}.Add(element);");
        }

        sb.AppendLine("    }");
        sb.AppendLine("    reader.ReadEndArray();");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Adds a helper method for deserializing generic values
    /// </summary>
    private static string GenerateGenericDeserializationHelper()
    {
        return @"
            // Generic deserialization helper
            private static T DeserializeGenericValue<T>(ReadOnlyMemory<byte> data)
            {
                // This is a placeholder - in real code, you would need to 
                // route to the appropriate deserializer based on the runtime type
                
                // For primitive types, handle directly
                var reader = new CborReader(data);
                
                // Detect the data type and convert accordingly
                switch (reader.PeekState())
                {
                    case CborReaderState.UnsignedInteger:
                        var uint64Value = reader.ReadUInt64();
                        // Try to convert to T
                        return (T)Convert.ChangeType(uint64Value, typeof(T));
                        
                    case CborReaderState.NegativeInteger:
                        var int64Value = reader.ReadInt64();
                        return (T)Convert.ChangeType(int64Value, typeof(T));
                        
                    case CborReaderState.TextString:
                        var strValue = reader.ReadTextString();
                        return (T)Convert.ChangeType(strValue, typeof(T));
                        
                    case CborReaderState.ByteString:
                        var bytesValue = reader.ReadByteString();
                        // If T is byte[] or compatible
                        if (typeof(T) == typeof(byte[]))
                            return (T)(object)bytesValue;
                        break;
                        
                    // Add cases for other CBOR types as needed
                }
                
                // For complex types, try to find and invoke the Read method
                var type = typeof(T);
                var readMethod = type.GetMethod(""Read"", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, 
                    null, 
                    new[] { typeof(ReadOnlyMemory<byte>), typeof(bool) }, 
                    null);
                    
                if (readMethod != null)
                {
                    return (T)readMethod.Invoke(null, new object[] { data, false });
                }
                
                // Default fallback
                throw new InvalidOperationException($""Cannot deserialize to type {typeof(T).Name}"");
            }";
    }
}