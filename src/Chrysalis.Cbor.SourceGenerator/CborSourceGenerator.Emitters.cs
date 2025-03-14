using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Chrysalis.Cbor.SourceGenerator;

public sealed partial class CborSourceGenerator
{
    /// <summary>
    /// Handles emitting source code for serialization
    /// </summary>
    private class Emitter(SourceProductionContext context)
    {
        private readonly SourceProductionContext _context = context;
        private readonly HashSet<string> _usedHintNames = [];

        /// <summary>
        /// Emits source code for all types in the serialization context
        /// </summary>
        public void Emit(SerializationContext serializationContext)
        {
            foreach (var type in serializationContext.Types)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();

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
                _ => new MapEmitterStrategy() // Default
            };
        }

        /// <summary>
        /// Creates a nullable strategy with the appropriate inner strategy
        /// </summary>
        private NullableEmitterStrategy CreateNullableStrategy(SerializableType type)
        {
            return new NullableEmitterStrategy(type.InnerType!);
        }

        /// <summary>
        /// Generates the complete source code for a type
        /// </summary>
        private string GenerateSourceCode(SerializableType type, IEmitterStrategy strategy)
        {
            string serializerCode = strategy.EmitSerializer(type);
            string deserializerCode = strategy.EmitDeserializer(type);

            // Handle generic types specially
            if (type.Type.IsGeneric)
            {
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

                // For generic types, use different template that preserves the constraints
                // Define the 'new' keyword based on whether we need to hide methods
                string writeNewKeyword = type.HasBaseWriteMethod ? "new " : "";
                string readNewKeyword = type.HasBaseReadMethod ? "new " : "";

                return $$"""
                    #nullable enable
                    // <auto-generated/>
                    #pragma warning disable CS0109 // Ignore warnings about unnecessary 'new' keyword
                    using System;
                    using System.Collections.Generic;
                    using System.Formats.Cbor;
                    using Chrysalis.Cbor.Types;
                    using Chrysalis.Cbor.Types.Custom;
                    using Chrysalis.Cbor.Serialization.Attributes;
                    

                    namespace {{type.Type.Namespace}};
                    
                    {{typeParamDeclarations}}

                    public partial {{GetTypeKeyword(type)}} {{type.Type.Name}}<{{typeParams}}>
                    {
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
                    }
                    #pragma warning restore CS0109
                    """;
            }
            else
            {
                // Standard non-generic template
                // Define the 'new' keyword based on whether we need to hide methods
                string writeNewKeyword = type.HasBaseWriteMethod ? "new " : "";
                string readNewKeyword = type.HasBaseReadMethod ? "new " : "";

                return $$"""
                    #nullable enable
                    // <auto-generated/>
                    #pragma warning disable CS0109 // Ignore warnings about unnecessary 'new' keyword
                    using System;
                    using System.Collections.Generic;
                    using System.Formats.Cbor;
                    using Chrysalis.Cbor.Types;
                    using Chrysalis.Cbor.Types.Custom;
                    using Chrysalis.Cbor.Serialization.Attributes;
                    

                    namespace {{type.Type.Namespace}};

                    public partial {{GetTypeKeyword(type)}} {{type.Type.Name}}
                    {
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
                    }
                    #pragma warning restore CS0109
                    """;
            }
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
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, !prop.Type.IsValueType));
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

            // Read map entries
            sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            sb.AppendLine("{");
            sb.AppendLine("    string key = reader.ReadTextString();");
            sb.AppendLine("    switch (key)");
            sb.AppendLine("    {");

            foreach (var prop in type.Properties)
            {
                sb.AppendLine($"        case \"{prop.Key}\":");
                sb.AppendLine($"            {GenericEmitterStrategy.GenerateReadCode(prop.Type.FullName, prop.Name, !prop.Type.IsValueType)}");
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

            foreach (var prop in orderedProperties)
            {
                sb.AppendLine($"// Write property: {prop.Name}");
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, !prop.Type.IsValueType));
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

            // Tag if needed
            if (type.Tag.HasValue)
            {
                sb.AppendLine($"writer.WriteTag((CborTag){type.Tag.Value});");
            }

            // Start the array
            sb.AppendLine("writer.WriteStartArray(null);");

            // Write constructor index first
            int constructorIndex = type.Constructor ?? 0;
            sb.AppendLine($"writer.WriteInt32({constructorIndex});");

            // Write properties in order
            var orderedProperties = type.Properties
                .OrderBy(p => p.Order ?? int.MaxValue)
                .ToList();

            foreach (var prop in orderedProperties)
            {
                sb.AppendLine($"// Write property: {prop.Name}");
                sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, !prop.Type.IsValueType));
            }

            // End the array
            sb.AppendLine("writer.WriteEndArray();");

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

            // Start reading the array
            sb.AppendLine("reader.ReadStartArray();");

            // Read constructor index
            int expectedIndex = type.Constructor ?? 0;
            sb.AppendLine($"int constructorIndex = reader.ReadInt32();");
            sb.AppendLine($"if (constructorIndex != {expectedIndex}) throw new Exception($\"Invalid constructor index: {{constructorIndex}}, expected: {expectedIndex}\");");

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

            // Use the CborTypeName property instead of GetType()
            sb.AppendLine("// Determine the concrete type from its type name");
            sb.AppendLine("switch (value.CborTypeName)");
            sb.AppendLine("{");

            foreach (var unionCase in type.UnionCases)
            {
                sb.AppendLine($"    case \"{unionCase.Name}\":");
                sb.AppendLine($"        {unionCase.FullName}.Write(writer, ({unionCase.FullName})value);");
                sb.AppendLine("        break;");
            }

            sb.AppendLine("    default:");
            sb.AppendLine("        throw new Exception($\"Unknown union type: {value.CborTypeName}\");");
            sb.AppendLine("}");

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();

            // Try each case until one succeeds
            sb.AppendLine("// Try each union case");
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("Exception lastException = null;");

            foreach (var unionCase in type.UnionCases)
            {
                sb.AppendLine($"// Try {unionCase.Name}");
                sb.AppendLine("try");
                sb.AppendLine("{");
                sb.AppendLine($"    var result = {unionCase.FullName}.Read(originalData, preserveRaw);");
                sb.AppendLine("    return result;");
                sb.AppendLine("}");
                sb.AppendLine("catch (Exception ex)");
                sb.AppendLine("{");
                sb.AppendLine("    lastException = ex;");
                sb.AppendLine("}");
            }

            sb.AppendLine("throw new Exception(\"Could not deserialize union type\", lastException);");

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
            sb.AppendLine(GenericEmitterStrategy.GenerateWriteCode($"value.{prop.Name}", prop.Type.FullName, !prop.Type.IsValueType));

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
}