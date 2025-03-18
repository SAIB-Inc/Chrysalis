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
                // Check if this is a nested type with a dot in the name
                bool isNestedType = cleanTypeName.Contains(".");

                // Check if this is a nested type that ends with ".CborDefList" or similar
                bool isNestedClassType = cleanTypeName.Contains(".") &&
                                        (cleanTypeName.EndsWith(".CborDefList") ||
                                         cleanTypeName.Contains(".CborDefList<") ||
                                         cleanTypeName.Contains(">.CborDefList"));

                // Special handling for globally qualified or nested types
                string readCode = $$"""
            // Read the encoded value as ReadOnlyMemory<byte>
            var encodedValue = reader.ReadEncodedValue();
            
            // Deserialize using the type's Read method
            {{variableName}} = {{(isNestedClassType ? $"({cleanTypeName})" : "")}}{{cleanTypeName}}.Read(encodedValue);
            """;

                return readCode;
            }
        }

        private static string InternalGenerateWriteCode(string variableName, string typeName, bool isNullable, int? size = null)
        {
            // Check if we're dealing with a nullable type (like int?)
            bool isNullableValueType = typeName.EndsWith("?") && !typeName.Contains("?>");
            string cleanTypeName = typeName.TrimEnd('?');

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
                // Check if this is a global type (fully qualified)
                if (typeName.Contains("global::"))
                {
                    // Always use the type's static Write method for custom types
                    return $$"""
                    {{cleanTypeName}}.Write(writer, {{variableName}});
                    """;
                }
                else
                {
                    // Custom CBOR type with potential raw bytes
                    return $$"""
                    // If we have raw bytes, use them directly
                    if ({{variableName}}.Raw.HasValue)
                    {
                        writer.WriteEncodedValue({{variableName}}.Raw.Value.Span);
                    }
                    else
                    {
                        {{cleanTypeName}}.Write(writer, {{variableName}});
                    }
                    """;
                }
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
                bool elementIsNullable = !isPrimitiveElement || elementType == "string";

                return $$"""
                {{variableName}} = new {{typeName}}();
                reader.ReadStartArray();
                while (reader.PeekState() != CborReaderState.EndArray)
                {
                    {{elementType}} element = default;
                    {{GenerateReadCode(elementType, "element", elementIsNullable)}}
                    {{variableName}}.Add(element);
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

            var sb = new StringBuilder();

            sb.AppendLine($"{variableName} = new {typeName}();");
            sb.AppendLine("reader.ReadStartMap();");
            sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            sb.AppendLine("{");

            // Use a more specific variable name with property name to avoid conflicts
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
                sb.AppendLine("        var encodedKey = reader.ReadEncodedValue();");
                sb.AppendLine("        var keyReader = new CborReader(encodedKey);");
                sb.AppendLine("        var keyState = keyReader.PeekState();");
                sb.AppendLine("");
                sb.AppendLine("        if (keyState == CborReaderState.Null)");
                sb.AppendLine("        {");
                sb.AppendLine("            keyReader.ReadNull();");
                sb.AppendLine($"            dictKey_{variableName} = default;");
                sb.AppendLine("        }");
                sb.AppendLine("        else if (keyState == CborReaderState.UnsignedInteger)");
                sb.AppendLine("        {");
                sb.AppendLine("            var uintValue = keyReader.ReadUInt32();");
                sb.AppendLine("            if (typeof(T) == typeof(int))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)(int)uintValue;");
                sb.AppendLine("            else if (typeof(T) == typeof(uint))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)uintValue;");
                sb.AppendLine("            else if (typeof(T) == typeof(long))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)(long)uintValue;");
                sb.AppendLine("            else if (typeof(T) == typeof(ulong))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)(ulong)uintValue;");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize UInt32 to key type {{{keyType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("        else if (keyState == CborReaderState.TextString)");
                sb.AppendLine("        {");
                sb.AppendLine("            var strValue = keyReader.ReadTextString();");
                sb.AppendLine("            if (typeof(T) == typeof(string))");
                sb.AppendLine($"                dictKey_{variableName} = (T)(object)strValue;");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize string to key type {{{keyType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine($"            throw new InvalidOperationException($\"Cannot deserialize {{keyState}} to key type {{{keyType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            else
            {
                // Replace the code being generated for read key with new variable name
                string keyReadCode = GenerateReadCode(keyType, $"dictKey_{variableName}", false);
                // Replace any temporary variables inside to avoid conflicts
                keyReadCode = keyReadCode.Replace("key = ", $"dictKey_{variableName} = ");
                sb.AppendLine($"    {keyReadCode}");
            }

            // Use a more specific variable name with property name to avoid conflicts
            sb.AppendLine($"    {valueType} dictValue_{variableName} = default;");
            if (valueContainsGenericParams)
            {
                sb.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("    {");
                sb.AppendLine("        reader.ReadNull();");
                sb.AppendLine($"        dictValue_{variableName} = default;");
                sb.AppendLine("    }");
                sb.AppendLine("    else");
                sb.AppendLine("    {");
                sb.AppendLine("        var encodedValue = reader.ReadEncodedValue();");
                sb.AppendLine("        var valueReader = new CborReader(encodedValue);");
                sb.AppendLine("        var valueState = valueReader.PeekState();");
                sb.AppendLine("");
                sb.AppendLine("        if (valueState == CborReaderState.Null)");
                sb.AppendLine("        {");
                sb.AppendLine("            valueReader.ReadNull();");
                sb.AppendLine($"            dictValue_{variableName} = default;");
                sb.AppendLine("        }");
                sb.AppendLine("        else if (valueState == CborReaderState.UnsignedInteger)");
                sb.AppendLine("        {");
                sb.AppendLine("            var uintValue = valueReader.ReadUInt32();");
                sb.AppendLine("            if (typeof(T) == typeof(int))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)(int)uintValue;");
                sb.AppendLine("            else if (typeof(T) == typeof(uint))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)uintValue;");
                sb.AppendLine("            else if (typeof(T) == typeof(long))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)(long)uintValue;");
                sb.AppendLine("            else if (typeof(T) == typeof(ulong))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)(ulong)uintValue;");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize UInt32 to value type {{{valueType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("        else if (valueState == CborReaderState.TextString)");
                sb.AppendLine("        {");
                sb.AppendLine("            var strValue = valueReader.ReadTextString();");
                sb.AppendLine("            if (typeof(T) == typeof(string))");
                sb.AppendLine($"                dictValue_{variableName} = (T)(object)strValue;");
                sb.AppendLine("            else");
                sb.AppendLine($"                throw new InvalidOperationException($\"Cannot deserialize string to value type {{{valueType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine($"            throw new InvalidOperationException($\"Cannot deserialize {{valueState}} to value type {{{valueType}}}\");");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            else
            {
                // Replace the code being generated for read value with new variable name
                string valueReadCode = GenerateReadCode(valueType, $"dictValue_{variableName}", valueIsNullable);
                // Replace any temporary variables inside to avoid conflicts
                valueReadCode = valueReadCode.Replace("value = ", $"dictValue_{variableName} = ");
                sb.AppendLine($"    {valueReadCode}");
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

                return $$"""
            if ({{variableName}} == null)
            {
                writer.WriteNull();
                return;
            }
            
            writer.WriteStartMap({{variableName}}.Count);
            foreach (var kvp in {{variableName}})
            {
                {{(keyContainsGenericParams ? "WriteGenericValue(writer, kvp.Key);" :
                      GenerateWriteCode("kvp.Key", keyType, false))}}
                
                {{(valueContainsGenericParams ? "WriteGenericValue(writer, kvp.Value);" :
                      GenerateWriteCode("kvp.Value", valueType, valueIsNullable))}}
            }
            writer.WriteEndMap();
            """;
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