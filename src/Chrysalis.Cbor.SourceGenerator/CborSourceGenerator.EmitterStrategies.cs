using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Reflection;
using System.Text;

namespace Chrysalis.Cbor.SourceGenerator;

public sealed partial class CborSourceGenerator
{
    /// <summary>
    /// Provides reusable code generation patterns for all serialization strategies
    /// </summary>
    private class GenericEmitterStrategy
    {
        /// <summary>
        /// Generates code to read a value of the specified type
        /// </summary>
        /// <param name="typeName">The type name</param>
        /// <param name="variableName">The variable name</param>
        /// <param name="isNullable">Whether the property is marked with [CborNullable]</param>
        public static string GenerateReadCode(string typeName, string variableName, bool isNullable = false, int? size = null)
        {
            // If the type itself is already nullable (like ulong?), don't add additional null check
            bool isAlreadyNullable = typeName.EndsWith("?") && !typeName.Contains("?>");

            // Handle null values for reference types and nullable types
            // Only handle as nullable if explicitly marked with [CborNullable]
            if (isNullable && !isAlreadyNullable)
            {
                return $$"""
                if (reader.PeekState() == CborReaderState.Null)
                {
                    reader.ReadNull();
                    {{variableName}} = default;
                }
                else
                {
                    {{InternalGenerateReadCode(typeName, variableName)}}
                }
                """;
            }

            return InternalGenerateReadCode(typeName, variableName);
        }

        /// <summary>
        /// Generates code to write a value of the specified type
        /// </summary>
        /// <param name="variableName">The variable name</param>
        /// <param name="typeName">The type name</param>
        /// <param name="isNullable">Whether the property is marked with [CborNullable]</param>
        public static string GenerateWriteCode(string variableName, string typeName, bool isNullable)
        {
            // Handle null values for nullable types
            // Only handle as nullable if explicitly marked with [CborNullable]
            if (isNullable)
            {
                return $$"""
                    if ({{variableName}} == null)
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        {{InternalGenerateWriteCode(variableName, typeName, isNullable)}}
                    }
                """;
            }

            return InternalGenerateWriteCode(variableName, typeName, isNullable);
        }

        private static string InternalGenerateReadCode(string typeName, string variableName)
        {
            // Check if we're dealing with a nullable value type (like int?)
            bool isNullableValueType = typeName.EndsWith("?") && !typeName.Contains("?>");
            string cleanTypeName = typeName.TrimEnd('?');

            // Handle generic type parameters (like T)
            if (ContainsGenericParameters(cleanTypeName) && !IsCollection(cleanTypeName) &&
                (cleanTypeName.Length == 1 || cleanTypeName == "TKey" || cleanTypeName == "TValue"))
            {
                return $$"""
                    // Read the encoded value for generic type parameter
                    var encodedValue_{{variableName}} = reader.ReadEncodedValue();
                    
                    // Deserialize using the generic helper
                    {{variableName}} = DeserializeGenericValue<{{cleanTypeName}}>(encodedValue_{{variableName}});
                """;
            }

            // Special handling for nullable primitive types like ulong?
            if (isNullableValueType && IsPrimitive(cleanTypeName))
            {
                string readMethod = cleanTypeName switch
                {
                    "System.Int32" or "int" => "reader.ReadInt32()",
                    "System.String" or "string" => "reader.ReadTextString()",
                    "System.Boolean" or "bool" => "reader.ReadBoolean()",
                    "System.UInt32" or "uint" => "reader.ReadUInt32()",
                    "System.Int64" or "long" => "reader.ReadInt64()",
                    "System.UInt64" or "ulong" => "reader.ReadUInt64()",
                    "System.Single" or "float" => "reader.ReadSingle()",
                    "System.Double" or "double" => "reader.ReadDouble()",
                    "System.Decimal" or "decimal" => "(decimal)reader.ReadDouble()",
                    "System.Byte[]" or "byte[]" => GenerateByteRead(variableName),
                    _ => throw new InvalidOperationException($"Unsupported primitive type: {typeName}")
                };

                if (cleanTypeName == "byte[]")
                {
                    return readMethod;
                }

                return $"{variableName} = {readMethod};";
            }
            else if (IsPrimitive(typeName))
            {
                return GeneratePrimitiveRead(typeName, variableName);
            }
            else if (IsCollection(typeName))
            {
                return GenerateCollectionRead(typeName, variableName);
            }
            else
            {
                // FIXED: Always add an explicit cast for complex types to handle inheritance scenarios
                // This ensures the returned object from Read() is properly cast to the expected type,
                // which is especially important when the Read method returns a base type but we need a derived type

                // Special handling for globally qualified or nested types
                // Ensure we use fully qualified type references to avoid namespace conflicts
                string fullyQualifiedType = cleanTypeName.Contains(".") ? cleanTypeName : $"global::{cleanTypeName}";

                string readCode = $$"""
                    // Read the encoded value as ReadOnlyMemory<byte>
                    var encodedValue_{{variableName}} = reader.ReadEncodedValue();
                    
                    // Deserialize using the type's Read method with explicit cast
                    {{variableName}} = ({{cleanTypeName}}){{fullyQualifiedType}}.Read(encodedValue_{{variableName}});
                    """;

                return readCode;
            }
        }

        private static string InternalGenerateWriteCode(string variableName, string typeName, bool isNullable, int? size = null)
        {
            // Check if we're dealing with a nullable type (like int?)
            bool isNullableValueType = typeName.EndsWith("?") && !typeName.Contains("?>");
            string cleanTypeName = typeName.TrimEnd('?');

            // Handle generic type parameters (like T)
            if (ContainsGenericParameters(cleanTypeName) && !IsCollection(cleanTypeName) &&
                (cleanTypeName.Length == 1 || cleanTypeName == "TKey" || cleanTypeName == "TValue"))
            {
                return $"WriteGenericValue(writer, {variableName});";
            }

            // Special handling for nullable primitive types like ulong?
            if (isNullableValueType && IsPrimitive(cleanTypeName))
            {
                if (cleanTypeName == "ulong" || cleanTypeName == "System.UInt64")
                {
                    return $$"""
                    if ({{variableName}}.HasValue)
                    {
                        writer.WriteUInt64({{variableName}}.Value);
                    }
                    else
                    {
                        writer.WriteNull();
                    }
                    """;
                }

                string writeMethod = cleanTypeName switch
                {
                    "System.Int32" or "int" => $"writer.WriteInt32({variableName}.Value);",
                    "System.String" or "string" => $"writer.WriteTextString({variableName});",
                    "System.Boolean" or "bool" => $"writer.WriteBoolean({variableName}.Value);",
                    "System.UInt32" or "uint" => $"writer.WriteUInt32({variableName}.Value);",
                    "System.Int64" or "long" => $"writer.WriteInt64({variableName}.Value);",
                    "System.UInt64" or "ulong" => $"writer.WriteUInt64({variableName}.Value);",
                    "System.Single" or "float" => $"writer.WriteSingle({variableName}.Value);",
                    "System.Double" or "double" => $"writer.WriteDouble({variableName}.Value);",
                    "System.Decimal" or "decimal" => $"writer.WriteDouble((double){variableName}.Value);",
                    "System.Byte[]" or "byte[]" => GenerateByteWrite(variableName, size),
                    "System.DateTime" => $"writer.WriteTextString({variableName}.Value.ToString(\"o\"));",
                    _ => throw new InvalidOperationException($"Unsupported primitive type: {typeName}")
                };

                return writeMethod;
            }
            else if (IsPrimitive(typeName))
            {
                return GeneratePrimitiveWrite(variableName, typeName);
            }
            else if (IsCollection(typeName))
            {
                return GenerateCollectionWrite(variableName, typeName, isNullable);
            }
            else
            {
                // Always ensure the type reference is fully qualified to avoid namespace conflicts
                string fullyQualifiedType = cleanTypeName.Contains(".")
                    ? cleanTypeName
                    : $"global::{cleanTypeName}";

                // Always use the type's static Write method for custom types
                return $$"""{{fullyQualifiedType}}.Write(writer, {{variableName}});""";
            }
        }

        /// <summary>
        /// Determines if a type is a primitive CBOR type
        /// </summary>
        public static bool IsPrimitive(string typeName)
        {
            // Remove any question mark for nullable types
            string cleanTypeName = typeName.TrimEnd('?');

            return cleanTypeName switch
            {
                "System.Int32" or "int" => true,
                "System.String" or "string" => true,
                "System.Boolean" or "bool" => true,
                "System.UInt32" or "uint" => true,
                "System.Int64" or "long" => true,
                "System.UInt64" or "ulong" => true,
                "System.Single" or "float" => true,
                "System.Double" or "double" => true,
                "System.Decimal" or "decimal" => true,
                "System.Byte[]" or "byte[]" => true,
                "System.DateTime" => true,
                _ => false
            };
        }

        /// <summary>
        /// Determines if a type is a collection type
        /// </summary>
        public static bool IsCollection(string typeName)
        {
            return IsListType(typeName) || IsDictionaryType(typeName);
        }

        /// <summary>
        /// Determines if a type is a list-like collection
        /// </summary>
        public static bool IsListType(string typeName)
        {
            return typeName.Contains("System.Collections.Generic.List<") ||
                   typeName.Contains("System.Collections.Generic.IList<") ||
                   typeName.Contains("System.Collections.Generic.ICollection<") ||
                   typeName.Contains("System.Collections.Generic.IEnumerable<");
        }

        /// <summary>
        /// Determines if a type is a dictionary-like collection
        /// </summary>
        public static bool IsDictionaryType(string typeName)
        {
            return typeName.Contains("System.Collections.Generic.Dictionary<") ||
                   typeName.Contains("System.Collections.Generic.IDictionary<");
        }

        /// <summary>
        /// Generates code to read a primitive value
        /// </summary>
        public static string GeneratePrimitiveRead(string typeName, string variableName)
        {
            // Get the clean type name without question mark
            string cleanTypeName = typeName.TrimEnd('?');

            string readMethod = cleanTypeName switch
            {
                "System.Int32" or "int" => "reader.ReadInt32()",
                "System.String" or "string" => "reader.ReadTextString()",
                "System.Boolean" or "bool" => "reader.ReadBoolean()",
                "System.UInt32" or "uint" => "reader.ReadUInt32()",
                "System.Int64" or "long" => "reader.ReadInt64()",
                "System.UInt64" or "ulong" => "reader.ReadUInt64()",
                "System.Single" or "float" => "reader.ReadSingle()",
                "System.Double" or "double" => "reader.ReadDouble()",
                "System.Decimal" or "decimal" => "(decimal)reader.ReadDouble()",
                "System.Byte[]" or "byte[]" => GenerateByteRead(variableName),
                "System.DateTime" => "DateTime.Parse(reader.ReadTextString())",
                _ => throw new InvalidOperationException($"Unsupported primitive type: {typeName}")
            };

            if (cleanTypeName == "byte[]")
            {
                return readMethod;
            }

            return $"{variableName} = {readMethod};";
        }

        /// <summary>
        /// Generates code to write a primitive value
        /// </summary>
        public static string GeneratePrimitiveWrite(string variableName, string typeName, int? size = null)
        {
            // Get the clean type name without question mark
            string cleanTypeName = typeName.TrimEnd('?');

            return cleanTypeName switch
            {
                "System.Int32" or "int" => $"writer.WriteInt32({variableName});",
                "System.String" or "string" => $"writer.WriteTextString({variableName});",
                "System.Boolean" or "bool" => $"writer.WriteBoolean({variableName});",
                "System.UInt32" or "uint" => $"writer.WriteUInt32({variableName});",
                "System.Int64" or "long" => $"writer.WriteInt64({variableName});",
                "System.UInt64" or "ulong" => $"writer.WriteUInt64({variableName});",
                "System.Single" or "float" => $"writer.WriteSingle({variableName});",
                "System.Double" or "double" => $"writer.WriteDouble({variableName});",
                "System.Decimal" or "decimal" => $"writer.WriteDouble((double){variableName});",
                "System.Byte[]" or "byte[]" => GenerateByteWrite(variableName, size),
                "System.DateTime" => $"writer.WriteTextString({variableName}.ToString(\"o\"));",
                _ => throw new InvalidOperationException($"Unsupported primitive type: {typeName}")
            };
        }

        /// <summary>
        /// Extracts the element type from a collection type name
        /// </summary>
        public static string ExtractElementType(string typeName)
        {
            int start = typeName.IndexOf('<') + 1;
            int end = typeName.LastIndexOf('>');

            if (IsDictionaryType(typeName))
            {
                // For Dictionary<TKey, TValue>, return TValue
                string content = typeName.Substring(start, end - start);
                int commaIndex = content.IndexOf(',');
                return content.Substring(commaIndex + 1).Trim();
            }
            else
            {
                // For List<T>, return T
                return typeName.Substring(start, end - start);
            }
        }

        /// <summary>
        /// Generates code to read a byte[] value
        /// </summary>
        public static string GenerateByteRead(string variableName)
        {
            return $$"""
            switch (reader.PeekState())
            {
                case CborReaderState.StartIndefiniteLengthByteString:
                    using (var stream = new MemoryStream())
                    {
                        reader.ReadStartIndefiniteLengthByteString();
                        while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)
                        {
                            byte[] chunk = reader.ReadByteString();
                            stream.Write(chunk, 0, chunk.Length);
                        }
                        reader.ReadEndIndefiniteLengthByteString();
                        {{variableName}} = stream.ToArray();
                    }
                    break;
                default:
                    {{variableName}} = reader.ReadByteString();
                    break;
            }
            """;
        }

        /// <summary>
        /// Generates code to write a byte[] value
        /// </summary>
        public static string GenerateByteWrite(string variableName, int? size = null)
        {
            if (!size.HasValue)
            {
                return $$"""
                writer.WriteByteString({{variableName}});
                """;
            }

            return $$"""
            writer.WriteStartIndefiniteLengthByteString();
            
            int chunkSize = {{size.Value}};
            
            for (int i = 0; i < {{variableName}}.Length; i += chunkSize)
            {
                int remainingBytes = Math.Min(chunkSize, {{variableName}}.Length - i);
                writer.WriteByteString({{variableName}}.AsSpan(i, remainingBytes));
            }
            
            writer.WriteEndIndefiniteLengthByteString();
            """;
        }

        /// <summary>
        /// Extracts the key type from a dictionary type name
        /// </summary>
        public static string ExtractKeyType(string typeName)
        {
            if (!IsDictionaryType(typeName))
                throw new InvalidOperationException($"Type {typeName} is not a dictionary type");

            int start = typeName.IndexOf('<') + 1;
            int end = typeName.LastIndexOf('>');
            string content = typeName.Substring(start, end - start);
            int commaIndex = content.IndexOf(',');
            return content.Substring(0, commaIndex).Trim();
        }

        /// <summary>
        /// Determines if a type contains any generic parameters
        /// </summary>
        public static bool ContainsGenericParameters(string typeName)
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
        /// Determines if a type is potentially nullable (either reference type or nullable value type)
        /// </summary>
        private static bool IsNullableType(string typeName)
        {
            // All type parameters are treated as potentially nullable
            if (typeName == "T" || typeName.Contains("<T>"))
                return true;

            // Explicit nullable value types
            if (typeName.EndsWith("?") && !typeName.Contains("?>"))
                return true;

            // Primitive value types are not nullable unless explicitly marked
            return !IsPrimitiveValueType(typeName);
        }

        /// <summary>
        /// Determines if a type is a primitive value type (not nullable by default)
        /// </summary>
        private static bool IsPrimitiveValueType(string typeName)
        {
            return typeName switch
            {
                "int" or "System.Int32" => true,
                "long" or "System.Int64" => true,
                "float" or "System.Single" => true,
                "double" or "System.Double" => true,
                "decimal" or "System.Decimal" => true,
                "bool" or "System.Boolean" => true,
                "char" or "System.Char" => true,
                "byte" or "System.Byte" => true,
                "sbyte" or "System.SByte" => true,
                "short" or "System.Int16" => true,
                "ushort" or "System.UInt16" => true,
                "uint" or "System.UInt32" => true,
                "ulong" or "System.UInt64" => true,
                _ => false
            };
        }

        /// <summary>
        /// Generates code to read a collection of generic elements
        /// </summary>
        public static string GenerateGenericCollectionReadCode(string collectionVarName, string elementTypeName, string resultCollectionType)
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
            if (ContainsGenericParameters(elementTypeName))
            {
                // For generic parameters, use type-aware deserialization
                sb.AppendLine("        // Handling generic type parameter");
                sb.AppendLine("        if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("        {");
                sb.AppendLine("            reader.ReadNull();");
                sb.AppendLine($"            {collectionVarName}.Add(default);");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine("            var encodedValue = reader.ReadEncodedValue();");
                sb.AppendLine("            var elementReader = new CborReader(encodedValue);");
                sb.AppendLine($"            {elementTypeName} deserializedValue = default;");
                sb.AppendLine("");
                sb.AppendLine("            switch (elementReader.PeekState())");
                sb.AppendLine("            {");
                sb.AppendLine("                case CborReaderState.Null:");
                sb.AppendLine("                    elementReader.ReadNull();");
                sb.AppendLine("                    deserializedValue = default;");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.UnsignedInteger:");
                sb.AppendLine("                    var uintValue = elementReader.ReadUInt64();");
                sb.AppendLine("                    if (typeof(T) == typeof(int))");
                sb.AppendLine("                        deserializedValue = (T)(object)(int)uintValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(uint))");
                sb.AppendLine("                        deserializedValue = (T)(object)(uint)uintValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(long))");
                sb.AppendLine("                        deserializedValue = (T)(object)(long)uintValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(ulong))");
                sb.AppendLine("                        deserializedValue = (T)(object)uintValue;");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert UInt64 to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.NegativeInteger:");
                sb.AppendLine("                    var intValue = elementReader.ReadInt64();");
                sb.AppendLine("                    if (typeof(T) == typeof(int))");
                sb.AppendLine("                        deserializedValue = (T)(object)(int)intValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(long))");
                sb.AppendLine("                        deserializedValue = (T)(object)intValue;");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert Int64 to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.TextString:");
                sb.AppendLine("                    var strValue = elementReader.ReadTextString();");
                sb.AppendLine("                    if (typeof(T) == typeof(string))");
                sb.AppendLine("                        deserializedValue = (T)(object)strValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(Guid))");
                sb.AppendLine("                        deserializedValue = (T)(object)Guid.Parse(strValue);");
                sb.AppendLine("                    else if (typeof(T) == typeof(DateTime))");
                sb.AppendLine("                        deserializedValue = (T)(object)DateTime.Parse(strValue);");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert string to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.ByteString:");
                sb.AppendLine("                    var byteValue = elementReader.ReadByteString();");
                sb.AppendLine("                    if (typeof(T) == typeof(byte[]))");
                sb.AppendLine("                        deserializedValue = (T)(object)byteValue;");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert byte[] to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.Boolean:");
                sb.AppendLine("                    var boolValue = elementReader.ReadBoolean();");
                sb.AppendLine("                    if (typeof(T) == typeof(bool))");
                sb.AppendLine("                        deserializedValue = (T)(object)boolValue;");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert bool to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.SinglePrecisionFloat:");
                sb.AppendLine("                    var floatValue = elementReader.ReadSingle();");
                sb.AppendLine("                    if (typeof(T) == typeof(float))");
                sb.AppendLine("                        deserializedValue = (T)(object)floatValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(double))");
                sb.AppendLine("                        deserializedValue = (T)(object)(double)floatValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(decimal))");
                sb.AppendLine("                        deserializedValue = (T)(object)(decimal)floatValue;");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert float to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                case CborReaderState.DoublePrecisionFloat:");
                sb.AppendLine("                    var doubleValue = elementReader.ReadDouble();");
                sb.AppendLine("                    if (typeof(T) == typeof(float))");
                sb.AppendLine("                        deserializedValue = (T)(object)(float)doubleValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(double))");
                sb.AppendLine("                        deserializedValue = (T)(object)doubleValue;");
                sb.AppendLine("                    else if (typeof(T) == typeof(decimal))");
                sb.AppendLine("                        deserializedValue = (T)(object)(decimal)doubleValue;");
                sb.AppendLine("                    else");
                sb.AppendLine($"                        throw new InvalidOperationException($\"Cannot convert double to {{typeof(T).Name}}\");");
                sb.AppendLine("                    break;");
                sb.AppendLine("                default:");
                sb.AppendLine($"                    throw new InvalidOperationException($\"Cannot deserialize {{elementReader.PeekState()}} to type {{typeof(T).Name}}\");");
                sb.AppendLine("            }");
                sb.AppendLine("");
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
                sb.AppendLine("            element = default;");
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
        /// Generates code to read a collection
        /// </summary>
        public static string GenerateCollectionRead(string typeName, string variableName)
        {
            if (IsListType(typeName))
            {
                return GenerateListRead(typeName, variableName);
            }
            else if (IsDictionaryType(typeName))
            {
                return GenerateDictionaryRead(typeName, variableName);
            }

            throw new InvalidOperationException($"Unsupported collection type: {typeName}");
        }

        /// <summary>
        /// Generates code to read a list-like collection
        /// </summary>
        private static string GenerateListRead(string typeName, string variableName)
        {
            string elementType = ExtractElementType(typeName);
            bool containsGenericParameters = ContainsGenericParameters(elementType);

            if (containsGenericParameters)
            {
                return GenerateGenericCollectionReadCode(variableName, elementType, typeName);
            }
            else
            {
                // For concrete types
                bool isPrimitiveElement = IsPrimitive(elementType);

                return $$"""
                    {{variableName}} = new {{typeName}}();
                    reader.ReadStartArray();
                    while (reader.PeekState() != CborReaderState.EndArray)
                    {
                        {{elementType}} element_{{variableName}} = default;
                        if (reader.PeekState() == CborReaderState.Null)
                        {
                            reader.ReadNull();
                            element_{{variableName}} = default;
                        }
                        else
                        {
                            // Read the encoded value as ReadOnlyMemory<byte>
                            var encodedValue_{{variableName}}_element = reader.ReadEncodedValue();
                            
                            // Deserialize using the type's Read method
                            element_{{variableName}} = {{elementType}}.Read(encodedValue_{{variableName}}_element);
                        }
                        {{variableName}}.Add(element_{{variableName}});
                    }
                    reader.ReadEndArray();
            """;
            }
        }

        /// <summary>
        /// Generates code to read a dictionary-like collection
        /// </summary>
        private static string GenerateDictionaryRead(string typeName, string variableName)
        {
            string keyType = ExtractKeyType(typeName);
            string valueType = ExtractElementType(typeName);
            bool keyContainsGenericParams = ContainsGenericParameters(keyType);
            bool valueContainsGenericParams = ContainsGenericParameters(valueType);
            bool valueIsNullable = !IsPrimitive(valueType) || valueType == "string";
            bool valueIsDictionary = IsDictionaryType(valueType);

            var sb = new StringBuilder();

            sb.AppendLine($"{variableName} = new {typeName}();");
            sb.AppendLine("reader.ReadStartMap();");
            sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            sb.AppendLine("{");

            // Process key reading (unchanged code)
            sb.AppendLine($"    {keyType} dictKey_{variableName} = default;");
            if (keyContainsGenericParams)
            {
                sb.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("    {");
                sb.AppendLine("        reader.ReadNull();");
                sb.AppendLine($"        dictKey_{variableName} = default;");
                sb.AppendLine("    }");
                sb.AppendLine("    else");
                sb.AppendLine("    {");
                sb.AppendLine($"        var encodedKey_{variableName} = reader.ReadEncodedValue();");
                sb.AppendLine($"        var keyReader_{variableName} = new CborReader(encodedKey_{variableName});");
                sb.AppendLine($"        var keyState_{variableName} = keyReader_{variableName}.PeekState();");
                sb.AppendLine("");
                sb.AppendLine($"        if (keyState_{variableName} == CborReaderState.Null)");
                sb.AppendLine("        {");
                sb.AppendLine($"            keyReader_{variableName}.ReadNull();");
                sb.AppendLine($"            dictKey_{variableName} = default;");
                sb.AppendLine("        }");
                sb.AppendLine($"        else if (keyState_{variableName} == CborReaderState.UnsignedInteger)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var uintValue_{variableName} = keyReader_{variableName}.ReadUInt32();");
                sb.AppendLine("            if (typeof(T) == typeof(int))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)(int)uintValue_{variableName};");
                sb.AppendLine("            else if (typeof(T) == typeof(uint))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)uintValue_{variableName};");
                sb.AppendLine("            else if (typeof(T) == typeof(long))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)(long)uintValue_{variableName};");
                sb.AppendLine("            else if (typeof(T) == typeof(ulong))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)(ulong)uintValue_{variableName};");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize UInt32 to key type {{{keyType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine($"        else if (keyState_{variableName} == CborReaderState.TextString)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var strValue_{variableName} = keyReader_{variableName}.ReadTextString();");
                sb.AppendLine("            if (typeof(T) == typeof(string))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)strValue_{variableName};");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize string to key type {{{keyType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine($"            throw new InvalidOperationException($\"Cannot deserialize {{keyState_{variableName}}} to key type {{{keyType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            else
            {
                if (keyType == "byte[]" || keyType == "System.Byte[]")
                {
                    sb.AppendLine($"    switch (reader.PeekState())");
                    sb.AppendLine("    {");
                    sb.AppendLine("        case CborReaderState.StartIndefiniteLengthByteString:");
                    sb.AppendLine("            using (var stream = new MemoryStream())");
                    sb.AppendLine("            {");
                    sb.AppendLine("                reader.ReadStartIndefiniteLengthByteString();");
                    sb.AppendLine("                while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    byte[] chunk = reader.ReadByteString();");
                    sb.AppendLine("                    stream.Write(chunk, 0, chunk.Length);");
                    sb.AppendLine("                }");
                    sb.AppendLine("                reader.ReadEndIndefiniteLengthByteString();");
                    sb.AppendLine($"                dictKey_{variableName} = stream.ToArray();");
                    sb.AppendLine("            }");
                    sb.AppendLine("            break;");
                    sb.AppendLine("        default:");
                    sb.AppendLine($"            dictKey_{variableName} = reader.ReadByteString();");
                    sb.AppendLine("            break;");
                    sb.AppendLine("    }");
                }
                else if (IsPrimitive(keyType))
                {
                    string readMethod = keyType switch
                    {
                        "System.Int32" or "int" => $"dictKey_{variableName} = reader.ReadInt32();",
                        "System.String" or "string" => $"dictKey_{variableName} = reader.ReadTextString();",
                        "System.Boolean" or "bool" => $"dictKey_{variableName} = reader.ReadBoolean();",
                        "System.UInt32" or "uint" => $"dictKey_{variableName} = reader.ReadUInt32();",
                        "System.Int64" or "long" => $"dictKey_{variableName} = reader.ReadInt64();",
                        "System.UInt64" or "ulong" => $"dictKey_{variableName} = reader.ReadUInt64();",
                        "System.Single" or "float" => $"dictKey_{variableName} = reader.ReadSingle();",
                        "System.Double" or "double" => $"dictKey_{variableName} = reader.ReadDouble();",
                        "System.Decimal" or "decimal" => $"dictKey_{variableName} = (decimal)reader.ReadDouble();",
                        "System.DateTime" => $"dictKey_{variableName} = DateTime.Parse(reader.ReadTextString());",
                        _ => $"dictKey_{variableName} = reader.ReadTextString();" // Default fallback
                    };
                    sb.AppendLine($"    {readMethod}");
                }
                else
                {
                    // Complex object key
                    sb.AppendLine("    // Read the encoded value as ReadOnlyMemory<byte>");
                    sb.AppendLine($"    var encodedKey_{variableName} = reader.ReadEncodedValue();");
                    sb.AppendLine();
                    sb.AppendLine("    // Deserialize using the type's Read method");
                    sb.AppendLine($"    dictKey_{variableName} = {keyType}.Read(encodedKey_{variableName});");
                }
            }

            // Handle the value - check if it's a dictionary
            sb.AppendLine($"    {valueType} dictValue_{variableName} = default;");

            if (valueIsDictionary)
            {
                // Special handling for nested dictionaries
                string nestedKeyType = ExtractKeyType(valueType);
                string nestedValueType = ExtractElementType(valueType);

                sb.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("    {");
                sb.AppendLine("        reader.ReadNull();");
                sb.AppendLine($"        dictValue_{variableName} = default;");
                sb.AppendLine("    }");
                sb.AppendLine("    else");
                sb.AppendLine("    {");
                sb.AppendLine($"        dictValue_{variableName} = new {valueType}();");
                sb.AppendLine("        reader.ReadStartMap();");
                sb.AppendLine("        while (reader.PeekState() != CborReaderState.EndMap)");
                sb.AppendLine("        {");

                // Generate code for the nested key
                if (nestedKeyType == "byte[]" || nestedKeyType == "System.Byte[]")
                {
                    sb.AppendLine($"            {nestedKeyType} nestedKey_{variableName} = default;");
                    sb.AppendLine($"            switch (reader.PeekState())");
                    sb.AppendLine("            {");
                    sb.AppendLine("                case CborReaderState.StartIndefiniteLengthByteString:");
                    sb.AppendLine("                    using (var stream = new MemoryStream())");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        reader.ReadStartIndefiniteLengthByteString();");
                    sb.AppendLine("                        while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                    sb.AppendLine("                        {");
                    sb.AppendLine("                            byte[] chunk = reader.ReadByteString();");
                    sb.AppendLine("                            stream.Write(chunk, 0, chunk.Length);");
                    sb.AppendLine("                        }");
                    sb.AppendLine("                        reader.ReadEndIndefiniteLengthByteString();");
                    sb.AppendLine($"                        nestedKey_{variableName} = stream.ToArray();");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    break;");
                    sb.AppendLine("                default:");
                    sb.AppendLine($"                    nestedKey_{variableName} = reader.ReadByteString();");
                    sb.AppendLine("                    break;");
                    sb.AppendLine("            }");
                }
                else if (IsPrimitive(nestedKeyType))
                {
                    sb.AppendLine($"            {nestedKeyType} nestedKey_{variableName} = default;");
                    string readMethod = nestedKeyType switch
                    {
                        "System.Int32" or "int" => $"nestedKey_{variableName} = reader.ReadInt32();",
                        "System.String" or "string" => $"nestedKey_{variableName} = reader.ReadTextString();",
                        "System.Boolean" or "bool" => $"nestedKey_{variableName} = reader.ReadBoolean();",
                        "System.UInt32" or "uint" => $"nestedKey_{variableName} = reader.ReadUInt32();",
                        "System.Int64" or "long" => $"nestedKey_{variableName} = reader.ReadInt64();",
                        "System.UInt64" or "ulong" => $"nestedKey_{variableName} = reader.ReadUInt64();",
                        "System.Single" or "float" => $"nestedKey_{variableName} = reader.ReadSingle();",
                        "System.Double" or "double" => $"nestedKey_{variableName} = reader.ReadDouble();",
                        "System.Decimal" or "decimal" => $"nestedKey_{variableName} = (decimal)reader.ReadDouble();",
                        "System.DateTime" => $"nestedKey_{variableName} = DateTime.Parse(reader.ReadTextString());",
                        _ => $"nestedKey_{variableName} = reader.ReadTextString();" // Default fallback
                    };
                    sb.AppendLine($"            {readMethod}");
                }
                else
                {
                    // Complex object key
                    sb.AppendLine($"            {nestedKeyType} nestedKey_{variableName} = default;");
                    sb.AppendLine("            // Read the encoded value as ReadOnlyMemory<byte>");
                    sb.AppendLine($"            var encodedNestedKey_{variableName} = reader.ReadEncodedValue();");
                    sb.AppendLine("");
                    sb.AppendLine("            // Deserialize using the type's Read method");
                    sb.AppendLine($"            nestedKey_{variableName} = {nestedKeyType}.Read(encodedNestedKey_{variableName});");
                }

                // Generate code for the nested value
                bool nestedValueIsNullable = !IsPrimitive(nestedValueType) || nestedValueType == "string";

                sb.AppendLine($"            {nestedValueType} nestedValue_{variableName} = default;");
                if (nestedValueIsNullable)
                {
                    sb.AppendLine("            if (reader.PeekState() == CborReaderState.Null)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                reader.ReadNull();");
                    sb.AppendLine($"                nestedValue_{variableName} = default;");
                    sb.AppendLine("            }");
                    sb.AppendLine("            else");
                    sb.AppendLine("            {");

                    if (IsPrimitive(nestedValueType))
                    {
                        string readMethod = nestedValueType switch
                        {
                            "System.Int32" or "int" => $"nestedValue_{variableName} = reader.ReadInt32();",
                            "System.String" or "string" => $"nestedValue_{variableName} = reader.ReadTextString();",
                            "System.Boolean" or "bool" => $"nestedValue_{variableName} = reader.ReadBoolean();",
                            "System.UInt32" or "uint" => $"nestedValue_{variableName} = reader.ReadUInt32();",
                            "System.Int64" or "long" => $"nestedValue_{variableName} = reader.ReadInt64();",
                            "System.UInt64" or "ulong" => $"nestedValue_{variableName} = reader.ReadUInt64();",
                            "System.Single" or "float" => $"nestedValue_{variableName} = reader.ReadSingle();",
                            "System.Double" or "double" => $"nestedValue_{variableName} = reader.ReadDouble();",
                            "System.Decimal" or "decimal" => $"nestedValue_{variableName} = (decimal)reader.ReadDouble();",
                            "System.DateTime" => $"nestedValue_{variableName} = DateTime.Parse(reader.ReadTextString());",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(readMethod))
                        {
                            sb.AppendLine($"                {readMethod}");
                        }
                        else if (nestedValueType == "byte[]" || nestedValueType == "System.Byte[]")
                        {
                            sb.AppendLine($"                switch (reader.PeekState())");
                            sb.AppendLine("                {");
                            sb.AppendLine("                    case CborReaderState.StartIndefiniteLengthByteString:");
                            sb.AppendLine("                        using (var stream = new MemoryStream())");
                            sb.AppendLine("                        {");
                            sb.AppendLine("                            reader.ReadStartIndefiniteLengthByteString();");
                            sb.AppendLine("                            while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                            sb.AppendLine("                            {");
                            sb.AppendLine("                                byte[] chunk = reader.ReadByteString();");
                            sb.AppendLine("                                stream.Write(chunk, 0, chunk.Length);");
                            sb.AppendLine("                            }");
                            sb.AppendLine("                            reader.ReadEndIndefiniteLengthByteString();");
                            sb.AppendLine($"                            nestedValue_{variableName} = stream.ToArray();");
                            sb.AppendLine("                        }");
                            sb.AppendLine("                        break;");
                            sb.AppendLine("                    default:");
                            sb.AppendLine($"                        nestedValue_{variableName} = reader.ReadByteString();");
                            sb.AppendLine("                        break;");
                            sb.AppendLine("                }");
                        }
                        else
                        {
                            // Fallback for other types
                            sb.AppendLine($"                // Read the encoded value as ReadOnlyMemory<byte>");
                            sb.AppendLine($"                var encodedNestedValue_{variableName} = reader.ReadEncodedValue();");
                            sb.AppendLine("");
                            sb.AppendLine($"                // Deserialize using the type's Read method");
                            sb.AppendLine($"                nestedValue_{variableName} = {nestedValueType}.Read(encodedNestedValue_{variableName});");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"                // Read the encoded value as ReadOnlyMemory<byte>");
                        sb.AppendLine($"                var encodedNestedValue_{variableName} = reader.ReadEncodedValue();");
                        sb.AppendLine("");
                        sb.AppendLine($"                // Deserialize using the type's Read method");
                        sb.AppendLine($"                nestedValue_{variableName} = {nestedValueType}.Read(encodedNestedValue_{variableName});");
                    }

                    sb.AppendLine("            }");
                }
                else
                {
                    // Non-nullable value types
                    if (IsPrimitive(nestedValueType))
                    {
                        string readMethod = nestedValueType switch
                        {
                            "System.Int32" or "int" => $"nestedValue_{variableName} = reader.ReadInt32();",
                            "System.Boolean" or "bool" => $"nestedValue_{variableName} = reader.ReadBoolean();",
                            "System.UInt32" or "uint" => $"nestedValue_{variableName} = reader.ReadUInt32();",
                            "System.Int64" or "long" => $"nestedValue_{variableName} = reader.ReadInt64();",
                            "System.UInt64" or "ulong" => $"nestedValue_{variableName} = reader.ReadUInt64();",
                            "System.Single" or "float" => $"nestedValue_{variableName} = reader.ReadSingle();",
                            "System.Double" or "double" => $"nestedValue_{variableName} = reader.ReadDouble();",
                            "System.Decimal" or "decimal" => $"nestedValue_{variableName} = (decimal)reader.ReadDouble();",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(readMethod))
                        {
                            sb.AppendLine($"            {readMethod}");
                        }
                        else
                        {
                            // Complex object value
                            sb.AppendLine($"            // Read the encoded value as ReadOnlyMemory<byte>");
                            sb.AppendLine($"            var encodedNestedValue_{variableName} = reader.ReadEncodedValue();");
                            sb.AppendLine("");
                            sb.AppendLine($"            // Deserialize using the type's Read method");
                            sb.AppendLine($"            nestedValue_{variableName} = {nestedValueType}.Read(encodedNestedValue_{variableName});");
                        }
                    }
                    else
                    {
                        // Complex object value
                        sb.AppendLine($"            // Read the encoded value as ReadOnlyMemory<byte>");
                        sb.AppendLine($"            var encodedNestedValue_{variableName} = reader.ReadEncodedValue();");
                        sb.AppendLine("");
                        sb.AppendLine($"            // Deserialize using the type's Read method");
                        sb.AppendLine($"            nestedValue_{variableName} = {nestedValueType}.Read(encodedNestedValue_{variableName});");
                    }
                }

                // Add to nested dictionary
                sb.AppendLine($"            dictValue_{variableName}[nestedKey_{variableName}] = nestedValue_{variableName};");
                sb.AppendLine("        }");
                sb.AppendLine("        reader.ReadEndMap();");
                sb.AppendLine("    }");
            }
            else if (valueContainsGenericParams)
            {
                sb.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("    {");
                sb.AppendLine("        reader.ReadNull();");
                sb.AppendLine($"        dictValue_{variableName} = default;");
                sb.AppendLine("    }");
                sb.AppendLine("    else");
                sb.AppendLine("    {");
                sb.AppendLine($"        var encodedValue_{variableName} = reader.ReadEncodedValue();");
                sb.AppendLine($"        var valueReader_{variableName} = new CborReader(encodedValue_{variableName});");
                sb.AppendLine($"        var valueState_{variableName} = valueReader_{variableName}.PeekState();");
                sb.AppendLine("");
                sb.AppendLine($"        if (valueState_{variableName} == CborReaderState.Null)");
                sb.AppendLine("        {");
                sb.AppendLine($"            valueReader_{variableName}.ReadNull();");
                sb.AppendLine($"            dictValue_{variableName} = default;");
                sb.AppendLine("        }");
                sb.AppendLine($"        else if (valueState_{variableName} == CborReaderState.UnsignedInteger)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var uintValue_{variableName} = valueReader_{variableName}.ReadUInt32();");
                sb.AppendLine("            if (typeof(T) == typeof(int))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)(int)uintValue_{variableName};");
                sb.AppendLine("            else if (typeof(T) == typeof(uint))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)uintValue_{variableName};");
                sb.AppendLine("            else if (typeof(T) == typeof(long))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)(long)uintValue_{variableName};");
                sb.AppendLine("            else if (typeof(T) == typeof(ulong))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)(ulong)uintValue_{variableName};");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize UInt32 to value type {{{valueType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine($"        else if (valueState_{variableName} == CborReaderState.TextString)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var strValue_{variableName} = valueReader_{variableName}.ReadTextString();");
                sb.AppendLine("            if (typeof(T) == typeof(string))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)strValue_{variableName};");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize string to value type {{{valueType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine($"            throw new InvalidOperationException($\"Cannot deserialize {{valueState_{variableName}}} to value type {{{valueType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            else
            {
                if (valueIsNullable)
                {
                    sb.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        reader.ReadNull();");
                    sb.AppendLine($"        dictValue_{variableName} = default;");
                    sb.AppendLine("    }");
                    sb.AppendLine("    else");
                    sb.AppendLine("    {");

                    if (IsPrimitive(valueType))
                    {
                        string readMethod = valueType switch
                        {
                            "System.Int32" or "int" => $"dictValue_{variableName} = reader.ReadInt32();",
                            "System.String" or "string" => $"dictValue_{variableName} = reader.ReadTextString();",
                            "System.Boolean" or "bool" => $"dictValue_{variableName} = reader.ReadBoolean();",
                            "System.UInt32" or "uint" => $"dictValue_{variableName} = reader.ReadUInt32();",
                            "System.Int64" or "long" => $"dictValue_{variableName} = reader.ReadInt64();",
                            "System.UInt64" or "ulong" => $"dictValue_{variableName} = reader.ReadUInt64();",
                            "System.Single" or "float" => $"dictValue_{variableName} = reader.ReadSingle();",
                            "System.Double" or "double" => $"dictValue_{variableName} = reader.ReadDouble();",
                            "System.Decimal" or "decimal" => $"dictValue_{variableName} = (decimal)reader.ReadDouble();",
                            "System.DateTime" => $"dictValue_{variableName} = DateTime.Parse(reader.ReadTextString());",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(readMethod))
                        {
                            sb.AppendLine($"        {readMethod}");
                        }
                        else if (valueType == "byte[]" || valueType == "System.Byte[]")
                        {
                            sb.AppendLine($"        switch (reader.PeekState())");
                            sb.AppendLine("        {");
                            sb.AppendLine("            case CborReaderState.StartIndefiniteLengthByteString:");
                            sb.AppendLine("                using (var stream = new MemoryStream())");
                            sb.AppendLine("                {");
                            sb.AppendLine("                    reader.ReadStartIndefiniteLengthByteString();");
                            sb.AppendLine("                    while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                            sb.AppendLine("                    {");
                            sb.AppendLine("                        byte[] chunk = reader.ReadByteString();");
                            sb.AppendLine("                        stream.Write(chunk, 0, chunk.Length);");
                            sb.AppendLine("                    }");
                            sb.AppendLine("                    reader.ReadEndIndefiniteLengthByteString();");
                            sb.AppendLine($"                    dictValue_{variableName} = stream.ToArray();");
                            sb.AppendLine("                }");
                            sb.AppendLine("                break;");
                            sb.AppendLine("            default:");
                            sb.AppendLine($"                dictValue_{variableName} = reader.ReadByteString();");
                            sb.AppendLine("                break;");
                            sb.AppendLine("        }");
                        }
                        else
                        {
                            // Fallback for other types
                            sb.AppendLine($"        // Read the encoded value as ReadOnlyMemory<byte>");
                            sb.AppendLine($"        var encodedValue_{variableName}_value = reader.ReadEncodedValue();");
                            sb.AppendLine("");
                            sb.AppendLine($"        // Deserialize using the type's Read method");
                            sb.AppendLine($"        dictValue_{variableName} = {valueType}.Read(encodedValue_{variableName}_value);");
                        }
                    }
                    else
                    {
                        // Complex object value with explicit cast
                        sb.AppendLine($"        // Read the encoded value as ReadOnlyMemory<byte>");
                        sb.AppendLine($"        var encodedValue_{variableName}_value = reader.ReadEncodedValue();");
                        sb.AppendLine("");

                        // Add explicit cast to ensure we get the right derived type
                        if (valueType.Contains("."))
                        {
                            // It's likely a specific derived type like TokenBundle.TokenBundleMint
                            // Need to add a cast to the specific type
                            sb.AppendLine($"        // Deserialize using the type's Read method with explicit cast to derived type");
                            sb.AppendLine($"        dictValue_{variableName} = ({valueType}){valueType}.Read(encodedValue_{variableName}_value);");
                        }
                        else
                        {
                            sb.AppendLine($"        // Deserialize using the type's Read method");
                            sb.AppendLine($"        dictValue_{variableName} = {valueType}.Read(encodedValue_{variableName}_value);");
                        }
                    }

                    sb.AppendLine("    }");
                }
                else
                {
                    // Non-nullable value types
                    if (IsPrimitive(valueType))
                    {
                        string readMethod = valueType switch
                        {
                            "System.Int32" or "int" => $"dictValue_{variableName} = reader.ReadInt32();",
                            "System.Boolean" or "bool" => $"dictValue_{variableName} = reader.ReadBoolean();",
                            "System.UInt32" or "uint" => $"dictValue_{variableName} = reader.ReadUInt32();",
                            "System.Int64" or "long" => $"dictValue_{variableName} = reader.ReadInt64();",
                            "System.UInt64" or "ulong" => $"dictValue_{variableName} = reader.ReadUInt64();",
                            "System.Single" or "float" => $"dictValue_{variableName} = reader.ReadSingle();",
                            "System.Double" or "double" => $"dictValue_{variableName} = reader.ReadDouble();",
                            "System.Decimal" or "decimal" => $"dictValue_{variableName} = (decimal)reader.ReadDouble();",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(readMethod))
                        {
                            sb.AppendLine($"    {readMethod}");
                        }
                        else
                        {
                            // Complex object value
                            sb.AppendLine($"    // Read the encoded value as ReadOnlyMemory<byte>");
                            sb.AppendLine($"    var encodedValue_{variableName}_value = reader.ReadEncodedValue();");
                            sb.AppendLine("");
                            sb.AppendLine($"    // Deserialize using the type's Read method");
                            sb.AppendLine($"    dictValue_{variableName} = {valueType}.Read(encodedValue_{variableName}_value);");
                        }
                    }
                    else
                    {
                        // Complex object value with explicit cast
                        sb.AppendLine($"    // Read the encoded value as ReadOnlyMemory<byte>");
                        sb.AppendLine($"    var encodedValue_{variableName}_value = reader.ReadEncodedValue();");
                        sb.AppendLine("");

                        // Add explicit cast for derived types
                        if (valueType.Contains("."))
                        {
                            sb.AppendLine($"    // Deserialize using the type's Read method with explicit cast to derived type");
                            sb.AppendLine($"    dictValue_{variableName} = ({valueType}){valueType}.Read(encodedValue_{variableName}_value);");
                        }
                        else
                        {
                            sb.AppendLine($"    // Deserialize using the type's Read method");
                            sb.AppendLine($"    dictValue_{variableName} = {valueType}.Read(encodedValue_{variableName}_value);");
                        }
                    }
                }
            }

            sb.AppendLine($"    {variableName}[dictKey_{variableName}] = dictValue_{variableName};");
            sb.AppendLine("}");
            sb.AppendLine("reader.ReadEndMap();");

            return sb.ToString();
        }

        /// <summary>
        /// Generates code to write a collection
        /// </summary>
        public static string GenerateCollectionWrite(string variableName, string typeName, bool isNullable)
        {
            if (IsListType(typeName))
            {
                string elementType = ExtractElementType(typeName);
                bool containsGenericParams = ContainsGenericParameters(elementType);

                return $$"""
                    if ({{variableName}} == null)
                    {
                        writer.WriteNull();
                        return;
                    }
                    
                    writer.WriteStartArray({{variableName}}.Count);
                    foreach (var item in {{variableName}})
                    {
                        {{(containsGenericParams ? "WriteGenericValue(writer, item);" :
                            GenerateWriteCode("item", elementType, IsNullableType(elementType)))}}
                    }
                    writer.WriteEndArray();
                """;
            }
            else if (IsDictionaryType(typeName))
            {
                string keyType = ExtractKeyType(typeName);
                string valueType = ExtractElementType(typeName);
                bool keyContainsGenericParams = ContainsGenericParameters(keyType);
                bool valueContainsGenericParams = ContainsGenericParameters(valueType);
                bool valueIsNullable = IsNullableType(valueType);
                bool valueIsDictionary = IsDictionaryType(valueType);

                // Create a sanitized variable name by replacing dots with underscores
                string safeVarName = variableName.Replace(".", "_");

                var codeBuilder = new StringBuilder();

                codeBuilder.AppendLine($"if ({variableName} == null)");
                codeBuilder.AppendLine("{");
                codeBuilder.AppendLine("    writer.WriteNull();");
                codeBuilder.AppendLine("    return;");
                codeBuilder.AppendLine("}");
                codeBuilder.AppendLine();
                codeBuilder.AppendLine($"writer.WriteStartMap({variableName}.Count);");
                codeBuilder.AppendLine($"foreach (var kvp_{safeVarName} in {variableName})");
                codeBuilder.AppendLine("{");

                // Handle key
                if (keyContainsGenericParams)
                {
                    codeBuilder.AppendLine($"    WriteGenericValue(writer, kvp_{safeVarName}.Key);");
                }
                else
                {
                    codeBuilder.AppendLine($"    {GenerateWriteCode($"kvp_{safeVarName}.Key", keyType, false)}");
                }

                // Handle value - special case for nested dictionaries
                if (valueIsDictionary)
                {
                    string nestedKeyType = ExtractKeyType(valueType);
                    string nestedValueType = ExtractElementType(valueType);
                    bool nestedValueIsNullable = IsNullableType(nestedValueType);

                    codeBuilder.AppendLine($"    if (kvp_{safeVarName}.Value == null)");
                    codeBuilder.AppendLine("    {");
                    codeBuilder.AppendLine("        writer.WriteNull();");
                    codeBuilder.AppendLine("    }");
                    codeBuilder.AppendLine("    else");
                    codeBuilder.AppendLine("    {");
                    codeBuilder.AppendLine($"        writer.WriteStartMap(kvp_{safeVarName}.Value.Count);");
                    codeBuilder.AppendLine($"        foreach (var nested_kvp_{safeVarName} in kvp_{safeVarName}.Value)");
                    codeBuilder.AppendLine("        {");

                    // Nested key
                    if (ContainsGenericParameters(nestedKeyType))
                    {
                        codeBuilder.AppendLine($"            WriteGenericValue(writer, nested_kvp_{safeVarName}.Key);");
                    }
                    else
                    {
                        codeBuilder.AppendLine($"            {GenerateWriteCode($"nested_kvp_{safeVarName}.Key", nestedKeyType, false)}");
                    }

                    // Nested value
                    if (ContainsGenericParameters(nestedValueType))
                    {
                        codeBuilder.AppendLine($"            WriteGenericValue(writer, nested_kvp_{safeVarName}.Value);");
                    }
                    else
                    {
                        codeBuilder.AppendLine($"            {GenerateWriteCode($"nested_kvp_{safeVarName}.Value", nestedValueType, nestedValueIsNullable)}");
                    }

                    codeBuilder.AppendLine("        }");
                    codeBuilder.AppendLine("        writer.WriteEndMap();");
                    codeBuilder.AppendLine("    }");
                }
                else if (valueContainsGenericParams)
                {
                    codeBuilder.AppendLine($"    WriteGenericValue(writer, kvp_{safeVarName}.Value);");
                }
                else
                {
                    codeBuilder.AppendLine($"    {GenerateWriteCode($"kvp_{safeVarName}.Value", valueType, valueIsNullable)}");
                }

                codeBuilder.AppendLine("}");
                codeBuilder.AppendLine("writer.WriteEndMap();");

                return codeBuilder.ToString();
            }

            throw new InvalidOperationException($"Unsupported collection type: {typeName}");
        }

        /// <summary>
        /// Generates the WriteGenericValue method for serializing generic values
        /// </summary>
        public static string GenerateWriteGenericValueMethod()
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
    }
}