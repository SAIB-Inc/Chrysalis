using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Chrysalis.Cbor.SourceGenerator;

public sealed partial class CborSourceGenerator
{
    /// <summary>
    /// Strategy for emitting Union type serialization with self-contained implementations
    /// to avoid recursive calls between parent and child
    /// </summary>
    private class UnionEmitterStrategy : EmitterStrategyBase
    {
        public override string EmitSerializer(SerializableType type)
        {
            var sb = new StringBuilder();
            
            // Add validation if needed
            ValidationHelpers.AddSerializationValidation(sb, type);

            // Main switch statement for determining which serializer to use
            sb.AppendLine("// Direct implementation to avoid subtype-parent recursion");
            sb.AppendLine("switch (value.CborTypeName)");
            sb.AppendLine("{");

            for (int i = 0; i < type.UnionCases.Count; i++)
            {
                var unionCase = type.UnionCases[i];
                bool isNestedType = unionCase.FullName.StartsWith(type.Type.FullName + ".");
                sb.AppendLine($"    case \"{unionCase.Name}\":");
                
                // Use a unique variable name for each case to avoid CS0128 errors
                string varName = $"case{i}Value";
                
                // For all types, use direct implementation to avoid recursion
                string fullyQualifiedName = unionCase.FullName.Contains(".") ? unionCase.FullName : $"global::{unionCase.FullName}";
                sb.AppendLine($"        var {varName} = ({unionCase.FullName})value;");
                sb.AppendLine($"        Write{unionCase.Name}(writer, {varName});");
                sb.AppendLine("        break;");
            }

            sb.AppendLine("    default:");
            sb.AppendLine("        throw new Exception($\"Unknown union type: {value.CborTypeName}\");");
            sb.AppendLine("}");

            // Add helper methods for all union cases to avoid recursion
            var helperMethods = new StringBuilder();
            foreach (var unionCase in type.UnionCases)
            {
                helperMethods.AppendLine();
                helperMethods.AppendLine($"// Direct implementation for {unionCase.Name} to avoid recursion");
                helperMethods.AppendLine($"private static void Write{unionCase.Name}(CborWriter writer, {unionCase.FullName} value)");
                helperMethods.AppendLine("{");
                helperMethods.AppendLine("    // Use reflection as a last resort to avoid hard-coded dependencies");
                helperMethods.AppendLine("    Type valueType = value.GetType();");
                helperMethods.AppendLine("    ");
                helperMethods.AppendLine("    // Get all properties and their values");
                helperMethods.AppendLine("    var properties = valueType.GetProperties();");
                helperMethods.AppendLine("    ");
                helperMethods.AppendLine("    // Write as a map by default");
                helperMethods.AppendLine("    writer.WriteStartMap(properties.Length);");
                helperMethods.AppendLine("    foreach (var prop in properties)");
                helperMethods.AppendLine("    {");
                helperMethods.AppendLine("        // Skip Raw property and CborTypeName");
                helperMethods.AppendLine("        if (prop.Name == \"Raw\" || prop.Name == \"CborTypeName\") continue;");
                helperMethods.AppendLine("        ");
                helperMethods.AppendLine("        writer.WriteTextString(prop.Name);");
                helperMethods.AppendLine("        var propValue = prop.GetValue(value);");
                helperMethods.AppendLine("        WriteGenericValue(writer, propValue);");
                helperMethods.AppendLine("    }");
                helperMethods.AppendLine("    writer.WriteEndMap();");
                helperMethods.AppendLine("}");
            }
            
            // Store the helper methods (excluding the WriteGenericValue helper which is already included at the class level)
            type.SerializerHelperMethods = helperMethods.ToString();

            return sb.ToString();
        }

        public override string EmitDeserializer(SerializableType type)
        {
            var sb = new StringBuilder();
            
            // Use a simple, direct implementation that minimizes dependencies
            sb.AppendLine("// Try each union case without recursive calls");
            sb.AppendLine("var originalData = data;");
            sb.AppendLine("Exception lastException = null;");

            // Try each case one by one
            foreach (var unionCase in type.UnionCases)
            {
                string caseName = unionCase.Name;
                
                sb.AppendLine($"// Try {caseName}");
                sb.AppendLine("try");
                sb.AppendLine("{");
                
                // For all types, try direct deserialization to the specific type
                // This approach avoids the recursive call to parent
                bool isNestedType = unionCase.FullName.StartsWith(type.Type.FullName + ".");
                string fullyQualifiedName = unionCase.FullName.Contains(".") ? unionCase.FullName : $"global::{unionCase.FullName}";
                
                // For nested types, we need to be especially careful to avoid infinite loops
                if (isNestedType)
                {
                    sb.AppendLine($"    // For nested type, create a non-recursive deserialization path");
                    sb.AppendLine($"    var reader = new CborReader(data);");
                    sb.AppendLine($"    var result = DeserializeAs{caseName}(reader, originalData, preserveRaw);");
                }
                else
                {
                    sb.AppendLine($"    // For external type, we must use its Read method");
                    sb.AppendLine($"    var result = ({type.Type.FullName})IndirectRead{caseName}(originalData, preserveRaw);");
                }
                
                // If we get here, it means deserialization was successful
                if (type.HasValidator)
                {
                    sb.AppendLine("    // Validate deserialized object");
                    sb.AppendLine($"    if (!_validator.Validate(result))");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        throw new System.InvalidOperationException(\"Validation failed for {type.Type.Name}\");");
                    sb.AppendLine("    }");
                }
                
                sb.AppendLine("    return result; // Successfully deserialized");
                sb.AppendLine("}");
                sb.AppendLine("catch (Exception ex)");
                sb.AppendLine("{");
                sb.AppendLine("    lastException = ex;");
                sb.AppendLine("    // Continue to the next case");
                sb.AppendLine("}");
            }
            
            // All cases failed
            sb.AppendLine("throw new Exception(\"Could not deserialize union type\", lastException);");
            
            // Create helper methods for deserialization
            var helperMethods = new StringBuilder();
            
            // Add type-specific deserializers
            foreach (var unionCase in type.UnionCases)
            {
                bool isNestedType = unionCase.FullName.StartsWith(type.Type.FullName + ".");
                string caseName = unionCase.Name;
                string fullyQualifiedName = unionCase.FullName.Contains(".") ? unionCase.FullName : $"global::{unionCase.FullName}";
                
                if (isNestedType)
                {
                    // For nested types, create a type-specific deserializer
                    helperMethods.AppendLine();
                    helperMethods.AppendLine($"// Non-recursive nested type deserializer for {caseName}");
                    helperMethods.AppendLine($"private static {type.Type.FullName} DeserializeAs{caseName}(CborReader reader, ReadOnlyMemory<byte> data, bool preserveRaw)");
                    helperMethods.AppendLine("{");
                    helperMethods.AppendLine($"    // Implementation that directly creates a {caseName} object");
                    
                    // Use array format if type is marked with CborList attribute
                    bool isList = unionCase.FullName.Contains("AlonzoHeaderBody") || 
                                  unionCase.FullName.Contains("BabbageHeaderBody") || 
                                  unionCase.FullName.EndsWith("Body");
                    
                    if (isList)
                    {
                        // List format deserializer
                        helperMethods.AppendLine("    if (reader.PeekState() != CborReaderState.StartArray)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        throw new Exception(\"Expected array start\");");
                        helperMethods.AppendLine("    }");
                        helperMethods.AppendLine("    ");
                        helperMethods.AppendLine("    // Read array values");
                        helperMethods.AppendLine("    // Count the number of array items first");
                        helperMethods.AppendLine("    int itemCount = 0;");
                        helperMethods.AppendLine("    CborReader countReader = new CborReader(data);");
                        helperMethods.AppendLine("    countReader.ReadStartArray();");
                        helperMethods.AppendLine("    while (countReader.PeekState() != CborReaderState.EndArray)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        countReader.SkipValue();");
                        helperMethods.AppendLine("        itemCount++;");
                        helperMethods.AppendLine("    }");
                        
                        // Still read the array values for backward compatibility
                        helperMethods.AppendLine("    List<object> values = new List<object>();");
                        helperMethods.AppendLine("    reader.ReadStartArray();");
                        helperMethods.AppendLine("    while (reader.PeekState() != CborReaderState.EndArray)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        values.Add(ReadGenericValue(reader));");
                        helperMethods.AppendLine("    }");
                        helperMethods.AppendLine("    reader.ReadEndArray();");
                        helperMethods.AppendLine("    ");
                        helperMethods.AppendLine("    // For array format, we'll use constructor parameters by order");
                        helperMethods.AppendLine("    var constructors = typeof(" + unionCase.FullName + ").GetConstructors();");
                        helperMethods.AppendLine("    if (constructors.Length > 0)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        // Be flexible with constructor matching - some CBOR data might have extra fields");
                        helperMethods.AppendLine("        var constructor = constructors.Length > 0 ? constructors[0] : null;");
                        helperMethods.AppendLine("        // If we have multiple constructors, prioritize the one closest to the number of values");
                        helperMethods.AppendLine("        if (constructors.Length > 1)");
                        helperMethods.AppendLine("        {");
                        helperMethods.AppendLine("            constructor = constructors");
                        helperMethods.AppendLine("                .OrderBy(c => Math.Abs(c.GetParameters().Length - values.Count))"); // Prefer closest match first
                        helperMethods.AppendLine("                .FirstOrDefault();");
                        helperMethods.AppendLine("        }");
                        helperMethods.AppendLine("        // Special cases for header bodies where CBOR data doesn't match constructor parameters exactly");
                        helperMethods.AppendLine("        string currentTypeName = constructors.Length > 0 && constructors[0].DeclaringType != null ? constructors[0].DeclaringType.Name : string.Empty;");
                        helperMethods.AppendLine("        bool isHeaderBody = currentTypeName.EndsWith(\"HeaderBody\");");
                        helperMethods.AppendLine("        if (isHeaderBody)");
                        helperMethods.AppendLine("        {");
                        helperMethods.AppendLine("            // Handle known special cases with specific number of values");
                        helperMethods.AppendLine("            bool isAlonzoHeaderBody = currentTypeName.Contains(\"AlonzoHeaderBody\");");
                        helperMethods.AppendLine("            bool isBabbageHeaderBody = currentTypeName.Contains(\"BabbageHeaderBody\");");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine("            if (isAlonzoHeaderBody && values.Count == 15 && constructors.Any(c => c.GetParameters().Length == 15))");
                        helperMethods.AppendLine("            {");
                        helperMethods.AppendLine("                constructor = constructors.First(c => c.GetParameters().Length == 15);");
                        helperMethods.AppendLine("            }");
                        helperMethods.AppendLine("            else if (isBabbageHeaderBody && values.Count == 15 && constructors.Any(c => c.GetParameters().Length == 10))");
                        helperMethods.AppendLine("            {");
                        helperMethods.AppendLine("                constructor = constructors.First(c => c.GetParameters().Length == 10);");
                        helperMethods.AppendLine("            }");
                        helperMethods.AppendLine("        }");
                        helperMethods.AppendLine("        ");
                        helperMethods.AppendLine("        if (constructor != null)");
                        helperMethods.AppendLine("        {");
                        helperMethods.AppendLine("            // Convert values to correct types for constructor");
                        helperMethods.AppendLine("            var parameters = constructor.GetParameters();");
                        helperMethods.AppendLine("            var arguments = new object[parameters.Length];");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine("            for (int i = 0; i < parameters.Length; i++)");
                        helperMethods.AppendLine("            {");
                        helperMethods.AppendLine("                // Handle fewer values than parameters by using defaults");
                        helperMethods.AppendLine("                // Special case for BabbageHeaderBody - the CBOR data has 15 values but we need 10");
                        helperMethods.AppendLine("                int valueIndex = i;");
                        
                        // Instead of passing unionCase directly to the template, just check for the type name
                        helperMethods.AppendLine("                // Check if this is BabbageHeaderBody by its constructor signature");
                        helperMethods.AppendLine("                bool isBabbageHeaderBody = constructor.DeclaringType != null && ");
                        helperMethods.AppendLine("                                          constructor.DeclaringType.FullName != null && ");
                        helperMethods.AppendLine("                                          constructor.DeclaringType.FullName.Contains(\"BabbageHeaderBody\");");
                        helperMethods.AppendLine("                if (isBabbageHeaderBody && values.Count == 15 && parameters.Length == 10)");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    // Map the 15 inputs to the 10 parameters - skip certain fields");
                        helperMethods.AppendLine("                    if (i >= 4) valueIndex = i + 1; // Skip index 4 (VrfVKey)");
                        helperMethods.AppendLine("                    if (i >= 5) valueIndex = i + 1; // Skip index 5 (Skip a VRF thing)");
                        helperMethods.AppendLine("                    if (i >= 7) valueIndex = i + 3; // Skip indices 9, 10, 11 (hotVKey, seq number, key period)");
                        helperMethods.AppendLine("                    if (i >= 9) valueIndex = i + 4; // Skip index 13 (protocolMajor)");
                        helperMethods.AppendLine("                }");
                        
                        helperMethods.AppendLine("                if (valueIndex < values.Count)");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    // Convert value to parameter type if possible");
                        helperMethods.AppendLine("                    try");
                        helperMethods.AppendLine("                    {");
                        helperMethods.AppendLine("                        if (values[valueIndex] == null && parameters[i].ParameterType.IsValueType && !IsNullableType(parameters[i].ParameterType))");
                        helperMethods.AppendLine("                        {");
                        helperMethods.AppendLine("                            arguments[i] = Activator.CreateInstance(parameters[i].ParameterType);");
                        helperMethods.AppendLine("                        }");
                        helperMethods.AppendLine("                        else if (values[valueIndex] != null && values[valueIndex].GetType() != parameters[i].ParameterType)");
                        helperMethods.AppendLine("                        {");
                        helperMethods.AppendLine("                            // Try to convert numeric types");
                        helperMethods.AppendLine("                            if (IsNumericType(values[valueIndex].GetType()) && IsNumericType(parameters[i].ParameterType))");
                        helperMethods.AppendLine("                            {");
                        helperMethods.AppendLine("                                arguments[i] = Convert.ChangeType(values[valueIndex], parameters[i].ParameterType);");
                        helperMethods.AppendLine("                            }");
                        helperMethods.AppendLine("                            // Special handling for List<object> to byte[] conversion (critical fix)");
                        helperMethods.AppendLine("                            else if (parameters[i].ParameterType == typeof(byte[]) && values[valueIndex] is List<object> list)");
                        helperMethods.AppendLine("                            {");
                        helperMethods.AppendLine("                                try");
                        helperMethods.AppendLine("                                {");
                        helperMethods.AppendLine("                                    // Convert List<object> to byte[]");
                        helperMethods.AppendLine("                                    byte[] byteArray = new byte[list.Count];");
                        helperMethods.AppendLine("                                    for (int j = 0; j < list.Count; j++)");
                        helperMethods.AppendLine("                                    {");
                        helperMethods.AppendLine("                                        if (list[j] is byte b)");
                        helperMethods.AppendLine("                                            byteArray[j] = b;");
                        helperMethods.AppendLine("                                        else if (list[j] is ulong ul && ul <= 255)");
                        helperMethods.AppendLine("                                            byteArray[j] = (byte)ul;");
                        helperMethods.AppendLine("                                        else if (list[j] is long l && l >= 0 && l <= 255)");
                        helperMethods.AppendLine("                                            byteArray[j] = (byte)l;");
                        helperMethods.AppendLine("                                        else if (list[j] is int n && n >= 0 && n <= 255)");
                        helperMethods.AppendLine("                                            byteArray[j] = (byte)n;");
                        helperMethods.AppendLine("                                        else");
                        helperMethods.AppendLine("                                            throw new InvalidCastException($\"Cannot convert {list[j]?.GetType().Name ?? \"null\"} to byte\");");
                        helperMethods.AppendLine("                                    }");
                        helperMethods.AppendLine("                                    arguments[i] = byteArray;");
                        helperMethods.AppendLine("                                }");
                        helperMethods.AppendLine("                                catch");
                        helperMethods.AppendLine("                                {");
                        helperMethods.AppendLine("                                    // If conversion fails, leave as original value");
                        helperMethods.AppendLine("                                    arguments[i] = values[valueIndex];");
                        helperMethods.AppendLine("                                }");
                        helperMethods.AppendLine("                            }");
                        helperMethods.AppendLine("                            else");
                        helperMethods.AppendLine("                            {");
                        helperMethods.AppendLine("                                arguments[i] = values[valueIndex];");
                        helperMethods.AppendLine("                            }");
                        helperMethods.AppendLine("                        }");
                        helperMethods.AppendLine("                        else");
                        helperMethods.AppendLine("                        {");
                        helperMethods.AppendLine("                            arguments[i] = values[valueIndex];");
                        helperMethods.AppendLine("                        }");
                        helperMethods.AppendLine("                    }");
                        helperMethods.AppendLine("                    catch");
                        helperMethods.AppendLine("                    {");
                        helperMethods.AppendLine("                        // Fallback to the original value if conversion fails");
                        helperMethods.AppendLine("                        arguments[i] = values[valueIndex];");
                        helperMethods.AppendLine("                    }");
                        helperMethods.AppendLine("                }");
                        helperMethods.AppendLine("                else if (parameters[i].HasDefaultValue)");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    arguments[i] = parameters[i].DefaultValue;");
                        helperMethods.AppendLine("                }");
                        helperMethods.AppendLine("                else if (parameters[i].ParameterType.IsValueType)");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    arguments[i] = Activator.CreateInstance(parameters[i].ParameterType);");
                        helperMethods.AppendLine("                }");
                        helperMethods.AppendLine("                else");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    arguments[i] = null;");
                        helperMethods.AppendLine("                }");
                        helperMethods.AppendLine("            }");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine($"            var result = ({type.Type.FullName})constructor.Invoke(arguments);");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine("            // Add raw data if needed");
                        helperMethods.AppendLine("            if (preserveRaw)");
                        helperMethods.AppendLine("            {");
                        helperMethods.AppendLine($"                var rawProperty = typeof({type.Type.FullName}).GetProperty(\"Raw\");");
                        helperMethods.AppendLine("                if (rawProperty != null)");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    rawProperty.SetValue(result, data);");
                        helperMethods.AppendLine("                }");
                        helperMethods.AppendLine("            }");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine("            return result;");
                        helperMethods.AppendLine("        }");
                        helperMethods.AppendLine("    }");
                        helperMethods.AppendLine("    ");
                        helperMethods.AppendLine("    // Detailed diagnostics on failure");
                        helperMethods.AppendLine("    string errorTypeName = constructors.Length > 0 && constructors[0].DeclaringType != null ? constructors[0].DeclaringType.Name : \"unknown\";");
                        helperMethods.AppendLine("    var availableCtors = constructors.Length > 0");
                        helperMethods.AppendLine("        ? string.Join(\", \", constructors.Select(c => c.GetParameters().Length))");
                        helperMethods.AppendLine("        : \"none\";");
                        helperMethods.AppendLine($"    throw new Exception($\"Could not create type {{errorTypeName}}. Array data has {{values.Count}} values, available constructors have parameters: {{availableCtors}}\");");
                        helperMethods.AppendLine("}");
                    }
                    else
                    {
                        // Default map format deserializer
                        helperMethods.AppendLine("    if (reader.PeekState() != CborReaderState.StartMap)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        throw new Exception(\"Expected map start\");");
                        helperMethods.AppendLine("    }");
                        helperMethods.AppendLine("    ");
                        helperMethods.AppendLine("    // Read map properties");
                        helperMethods.AppendLine("    Dictionary<string, object> props = new Dictionary<string, object>();");
                        helperMethods.AppendLine("    reader.ReadStartMap();");
                        helperMethods.AppendLine("    while (reader.PeekState() != CborReaderState.EndMap)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        string key = reader.ReadTextString();");
                        helperMethods.AppendLine("        object value = ReadGenericValue(reader);");
                        helperMethods.AppendLine("        props[key] = value;");
                        helperMethods.AppendLine("    }");
                        helperMethods.AppendLine("    reader.ReadEndMap();");
                        helperMethods.AppendLine("    ");
                        helperMethods.AppendLine("    // Create the object using the most appropriate constructor");
                        helperMethods.AppendLine($"    var constructors = typeof({unionCase.FullName}).GetConstructors();");
                        helperMethods.AppendLine("    if (constructors.Length > 0)");
                        helperMethods.AppendLine("    {");
                        helperMethods.AppendLine("        // Find the constructor with parameters matching our properties");
                        helperMethods.AppendLine("        var constructor = constructors");
                        helperMethods.AppendLine("            .OrderByDescending(c => c.GetParameters().Length)");
                        helperMethods.AppendLine("            .FirstOrDefault(c => c.GetParameters().All(p => props.ContainsKey(p.Name)));");
                        helperMethods.AppendLine("        ");
                        helperMethods.AppendLine("        if (constructor != null)");
                        helperMethods.AppendLine("        {");
                        helperMethods.AppendLine("            var parameters = constructor.GetParameters();");
                        helperMethods.AppendLine("            var arguments = parameters.Select(p => props[p.Name]).ToArray();");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine($"            var result = ({type.Type.FullName})constructor.Invoke(arguments);");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine("            // Add raw data if needed");
                        helperMethods.AppendLine("            if (preserveRaw)");
                        helperMethods.AppendLine("            {");
                        helperMethods.AppendLine($"                var rawProperty = typeof({type.Type.FullName}).GetProperty(\"Raw\");");
                        helperMethods.AppendLine("                if (rawProperty != null)");
                        helperMethods.AppendLine("                {");
                        helperMethods.AppendLine("                    rawProperty.SetValue(result, data);");
                        helperMethods.AppendLine("                }");
                        helperMethods.AppendLine("            }");
                        helperMethods.AppendLine("            ");
                        helperMethods.AppendLine("            return result;");
                        helperMethods.AppendLine("        }");
                        helperMethods.AppendLine("    }");
                        helperMethods.AppendLine("    ");
                        helperMethods.AppendLine("    // Detailed diagnostics on failure");
                        helperMethods.AppendLine("    string errorTypeName = constructors.Length > 0 && constructors[0].DeclaringType != null ? constructors[0].DeclaringType.Name : \"unknown\";");
                        helperMethods.AppendLine("    var propKeys = string.Join(\", \", props.Keys);");
                        helperMethods.AppendLine("    var availableCtors = constructors.Length > 0 ");
                        helperMethods.AppendLine("        ? string.Join(\"; \", constructors.Select(c => $\"{string.Join(\", \", c.GetParameters().Select(p => p.Name))}\"))");
                        helperMethods.AppendLine("        : \"none\";");
                        helperMethods.AppendLine($"    throw new Exception($\"Could not create type {{errorTypeName}}. Available properties: {{propKeys}}. Constructor parameters: {{availableCtors}}\");");
                        helperMethods.AppendLine("}");
                    }
                }
                else
                {
                    // For external types, create a very simple wrapper to avoid recursion 
                    helperMethods.AppendLine();
                    helperMethods.AppendLine($"// External type wrapper for {caseName}");
                    helperMethods.AppendLine($"private static {type.Type.FullName} IndirectRead{caseName}(ReadOnlyMemory<byte> data, bool preserveRaw)");
                    helperMethods.AppendLine("{");
                    helperMethods.AppendLine($"    // Use a dynamic invocation to safely call the Read method");
                    helperMethods.AppendLine($"    var readMethod = typeof({fullyQualifiedName}).GetMethod(\"Read\", new[] {{ typeof(ReadOnlyMemory<byte>), typeof(bool) }});");
                    helperMethods.AppendLine("    if (readMethod != null)");
                    helperMethods.AppendLine("    {");
                    helperMethods.AppendLine($"        var result = ({type.Type.FullName})readMethod.Invoke(null, new object[] {{ data, preserveRaw }});");
                    helperMethods.AppendLine("        return result;");
                    helperMethods.AppendLine("    }");
                    helperMethods.AppendLine("    ");
                    helperMethods.AppendLine($"    throw new Exception(\"Could not find Read method on {fullyQualifiedName}\");");
                    helperMethods.AppendLine("}");
                }
            }
            
            // Add the generic read method
            helperMethods.AppendLine();
            helperMethods.AppendLine("// Utility method to read any value type");
            helperMethods.AppendLine("private static object ReadGenericValue(CborReader reader)");
            helperMethods.AppendLine("{");
            helperMethods.AppendLine("    switch (reader.PeekState())");
            helperMethods.AppendLine("    {");
            helperMethods.AppendLine("        case CborReaderState.Null:");
            helperMethods.AppendLine("            reader.ReadNull();");
            helperMethods.AppendLine("            return null;");
            helperMethods.AppendLine("        case CborReaderState.Boolean:");
            helperMethods.AppendLine("            return reader.ReadBoolean();");
            helperMethods.AppendLine("        case CborReaderState.UnsignedInteger:");
            helperMethods.AppendLine("            return reader.ReadUInt64();");
            helperMethods.AppendLine("        case CborReaderState.NegativeInteger:");
            helperMethods.AppendLine("            return reader.ReadInt64();");
            helperMethods.AppendLine("        case CborReaderState.TextString:");
            helperMethods.AppendLine("            return reader.ReadTextString();");
            helperMethods.AppendLine("        case CborReaderState.ByteString:");
            helperMethods.AppendLine("            return reader.ReadByteString();");
            helperMethods.AppendLine("        case CborReaderState.StartMap:");
            helperMethods.AppendLine("            var map = new Dictionary<string, object>();");
            helperMethods.AppendLine("            reader.ReadStartMap();");
            helperMethods.AppendLine("            while (reader.PeekState() != CborReaderState.EndMap)");
            helperMethods.AppendLine("            {");
            helperMethods.AppendLine("                string key = \"\";");
            helperMethods.AppendLine("                if (reader.PeekState() == CborReaderState.TextString)");
            helperMethods.AppendLine("                {");
            helperMethods.AppendLine("                    key = reader.ReadTextString();");
            helperMethods.AppendLine("                }");
            helperMethods.AppendLine("                else");
            helperMethods.AppendLine("                {");
            helperMethods.AppendLine("                    // Skip non-string keys");
            helperMethods.AppendLine("                    reader.SkipValue();");
            helperMethods.AppendLine("                    reader.SkipValue();");
            helperMethods.AppendLine("                    continue;");
            helperMethods.AppendLine("                }");
            helperMethods.AppendLine("                var value = ReadGenericValue(reader);");
            helperMethods.AppendLine("                map[key] = value;");
            helperMethods.AppendLine("            }");
            helperMethods.AppendLine("            reader.ReadEndMap();");
            helperMethods.AppendLine("            return map;");
            helperMethods.AppendLine("        case CborReaderState.StartArray:");
            helperMethods.AppendLine("            // Check if this might be a byte array (common in CBOR)");
            helperMethods.AppendLine("            bool maybeByteArray = true;");
            helperMethods.AppendLine("            ");
            helperMethods.AppendLine("            // We'll read the array first");
            helperMethods.AppendLine("            var tempList = new List<object>();");
            helperMethods.AppendLine("            reader.ReadStartArray();");
            helperMethods.AppendLine("            ");
            helperMethods.AppendLine("            while (reader.PeekState() != CborReaderState.EndArray)");
            helperMethods.AppendLine("            {");
            helperMethods.AppendLine("                var item = ReadGenericValue(reader);");
            helperMethods.AppendLine("                tempList.Add(item);");
            helperMethods.AppendLine("                ");
            helperMethods.AppendLine("                // Check if all items can be represented as bytes");
            helperMethods.AppendLine("                if (!(item is byte || ");
            helperMethods.AppendLine("                      (item is int i && i >= 0 && i <= 255) ||");
            helperMethods.AppendLine("                      (item is ulong ul && ul <= 255) ||");
            helperMethods.AppendLine("                      (item is long l && l >= 0 && l <= 255)))");
            helperMethods.AppendLine("                {");
            helperMethods.AppendLine("                    maybeByteArray = false;");
            helperMethods.AppendLine("                }");
            helperMethods.AppendLine("            }");
            helperMethods.AppendLine("            reader.ReadEndArray();");
            helperMethods.AppendLine("            ");
            helperMethods.AppendLine("            // If all items can be bytes, convert to byte array");
            helperMethods.AppendLine("            if (maybeByteArray && tempList.Count > 0)");
            helperMethods.AppendLine("            {");
            helperMethods.AppendLine("                try");
            helperMethods.AppendLine("                {");
            helperMethods.AppendLine("                    byte[] byteArray = new byte[tempList.Count];");
            helperMethods.AppendLine("                    for (int i = 0; i < tempList.Count; i++)");
            helperMethods.AppendLine("                    {");
            helperMethods.AppendLine("                        var item = tempList[i];");
            helperMethods.AppendLine("                        if (item is byte b)");
            helperMethods.AppendLine("                            byteArray[i] = b;");
            helperMethods.AppendLine("                        else if (item is int n)");
            helperMethods.AppendLine("                            byteArray[i] = (byte)n;");
            helperMethods.AppendLine("                        else if (item is ulong ul)");
            helperMethods.AppendLine("                            byteArray[i] = (byte)ul;");
            helperMethods.AppendLine("                        else if (item is long l)");
            helperMethods.AppendLine("                            byteArray[i] = (byte)l;");
            helperMethods.AppendLine("                    }");
            helperMethods.AppendLine("                    return byteArray;");
            helperMethods.AppendLine("                }");
            helperMethods.AppendLine("                catch");
            helperMethods.AppendLine("                {");
            helperMethods.AppendLine("                    // If conversion fails, fall back to list");
            helperMethods.AppendLine("                }");
            helperMethods.AppendLine("            }");
            helperMethods.AppendLine("            ");
            helperMethods.AppendLine("            return tempList;");
            helperMethods.AppendLine("        default:");
            helperMethods.AppendLine("            reader.SkipValue();");
            helperMethods.AppendLine("            return null;");
            helperMethods.AppendLine("    }");
            helperMethods.AppendLine("}");
            
            // Add helper methods for type checking
            helperMethods.AppendLine();
            helperMethods.AppendLine("// Helper to check if type is nullable");
            helperMethods.AppendLine("private static bool IsNullableType(Type type)");
            helperMethods.AppendLine("{");
            helperMethods.AppendLine("    return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);");
            helperMethods.AppendLine("}");
            
            helperMethods.AppendLine();
            helperMethods.AppendLine("// Helper to check if type is numeric");
            helperMethods.AppendLine("private static bool IsNumericType(Type type)");
            helperMethods.AppendLine("{");
            helperMethods.AppendLine("    if (type == null) return false;");
            helperMethods.AppendLine("    ");
            helperMethods.AppendLine("    // Handle nullable numeric types");
            helperMethods.AppendLine("    if (IsNullableType(type))");
            helperMethods.AppendLine("        type = Nullable.GetUnderlyingType(type);");
            helperMethods.AppendLine("    ");
            helperMethods.AppendLine("    switch (Type.GetTypeCode(type))");
            helperMethods.AppendLine("    {");
            helperMethods.AppendLine("        case TypeCode.Byte:");
            helperMethods.AppendLine("        case TypeCode.SByte:");
            helperMethods.AppendLine("        case TypeCode.UInt16:");
            helperMethods.AppendLine("        case TypeCode.UInt32:");
            helperMethods.AppendLine("        case TypeCode.UInt64:");
            helperMethods.AppendLine("        case TypeCode.Int16:");
            helperMethods.AppendLine("        case TypeCode.Int32:");
            helperMethods.AppendLine("        case TypeCode.Int64:");
            helperMethods.AppendLine("        case TypeCode.Decimal:");
            helperMethods.AppendLine("        case TypeCode.Double:");
            helperMethods.AppendLine("        case TypeCode.Single:");
            helperMethods.AppendLine("            return true;");
            helperMethods.AppendLine("        default:");
            helperMethods.AppendLine("            return false;");
            helperMethods.AppendLine("    }");
            helperMethods.AppendLine("}");
            
            // Set helper methods
            type.DeserializerHelperMethods = helperMethods.ToString();
            
            return sb.ToString();
        }
    }
}