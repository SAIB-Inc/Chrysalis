using System.Formats.Cbor;

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
                        {{variableName}} = null;
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
                // Special handling for globally qualified types
                if (typeName.Contains("global::"))
                {
                    // Custom CBOR type - read the encoded value and pass it to the Read method
                    return $$"""
                        // Read the encoded value as ReadOnlyMemory<byte>
                        var encodedValue = reader.ReadEncodedValue();
                        
                        // Deserialize using the type's Read method
                        {{variableName}} = {{cleanTypeName}}.Read(encodedValue);
                        """;
                }
                else
                {
                    // Custom CBOR type - read the encoded value and pass it to the Read method  
                    return $$"""
                        // Read the encoded value as ReadOnlyMemory<byte>
                        var encodedValue = reader.ReadEncodedValue();
                        
                        // Deserialize using the type's Read method
                        {{variableName}} = {{cleanTypeName}}.Read(encodedValue);
                        """;
                }
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
                if (reader.PeekState() != CborReaderState.StartIndefiniteLengthByteString)
                {
                    {{variableName}} = reader.ReadByteString();
                } else 
                {
                    // Handle indefinite length
                    using MemoryStream stream = new();
                    reader.ReadStartIndefiniteLengthByteString();

                    while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)
                    {
                        byte[] chunk = reader.ReadByteString();
                        stream.Write(chunk);
                    }

                    reader.ReadEndIndefiniteLengthByteString();
                    {{variableName}} = stream.ToArray();
                }
            """;
        }

        /// <summary>
        /// Generates code to read a byte[] value
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
                    int remainingBytes = Math.Min(chunkSize, data.Length - i);
                    writer.WriteByteString(data.AsSpan(i, remainingBytes));
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
        /// Generates code to read a collection value
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
        /// Generates code to write a collection with special handling for generic elements
        /// </summary>
        public static string GenerateCollectionWrite(string variableName, string typeName, bool isNullable)
        {
            if (IsListType(typeName))
            {
                string elementType = ExtractElementType(typeName);

                // Special handling for generic element types
                if (elementType == "T" || elementType.Contains("<T>") || elementType.EndsWith("<T"))
                {
                    return $$"""
                        if ({{variableName}} == null)
                        {
                            writer.WriteNull();
                            return;
                        }
                        
                        {{GenerateGenericCollectionWriteCode(variableName, elementType)}}
                    """;
                }
                else
                {
                    // Standard collection handling for non-generic element types
                    return $$"""
                    if ({{variableName}} == null)
                    {
                        writer.WriteNull();
                        return;
                    }
                    
                    writer.WriteStartArray({{variableName}}.Count);
                    foreach (var item in {{variableName}})
                    {
                        {{GenerateWriteCode("item", elementType, IsNullableType(elementType))}}
                    }
                    writer.WriteEndArray();
                """;
                }
            }
            else if (IsDictionaryType(typeName))
            {
                string keyType = ExtractKeyType(typeName);
                string valueType = ExtractElementType(typeName);
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
                        {{GenerateWriteCode("kvp.Key", keyType, false)}}
                        {{GenerateWriteCode("kvp.Value", valueType, valueIsNullable)}}
                    }
                    writer.WriteEndMap();
                 """;
            }

            throw new InvalidOperationException($"Unsupported collection type: {typeName}");
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
                // Add other primitive value types as needed
                _ => false
            };
        }

        /// <summary>
        /// Generates code to read a list-like collection
        /// </summary>
        private static string GenerateListRead(string typeName, string variableName)
        {
            string elementType = ExtractElementType(typeName);
            bool elementIsNullable = !IsPrimitive(elementType) || elementType == "string";
            bool isPrimitiveElement = IsPrimitive(elementType);

            if (isPrimitiveElement)
            {
                // For primitive types, use the simple approach
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
            else
            {
                // For complex types, use ReadOnlyMemory<byte> slicing
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
        /// Generates code to write a list-like collection
        /// </summary>
        private static string GenerateListWrite(string variableName, string typeName, bool isNullable)
        {
            string elementType = ExtractElementType(typeName);

            return $$"""
                writer.WriteStartArray({{variableName}}.Count);
                foreach (var item in {{variableName}})
                {
                    {{GenerateWriteCode("item", elementType, isNullable)}}
                }
                writer.WriteEndArray();
                """;
        }

        /// <summary>
        /// Generates code to read a dictionary-like collection
        /// </summary>
        private static string GenerateDictionaryRead(string typeName, string variableName)
        {
            string keyType = ExtractKeyType(typeName);
            string valueType = ExtractElementType(typeName);
            bool valueIsNullable = !IsPrimitive(valueType) || valueType == "string";

            // Dictionary implementation is the same regardless of value type
            // because our ReadCode implementation handles both primitives and complex types
            return $$"""
                {{variableName}} = new {{typeName}}();
                reader.ReadStartMap();
                while (reader.PeekState() != CborReaderState.EndMap)
                {
                    {{keyType}} key = default;
                    {{GenerateReadCode(keyType, "key", false)}}
                    
                    {{valueType}} value = default;
                    {{GenerateReadCode(valueType, "value", valueIsNullable)}}
                    
                    {{variableName}}[key] = value;
                }
                reader.ReadEndMap();
                """;
        }

        /// <summary>
        /// Generates code to write a dictionary-like collection
        /// </summary>
        private static string GenerateDictionaryWrite(string variableName, string typeName)
        {
            string keyType = ExtractKeyType(typeName);
            string valueType = ExtractElementType(typeName);
            bool valueIsNullable = !IsPrimitive(valueType) || valueType == "string";

            return $$"""
                writer.WriteStartMap({{variableName}}.Count);
                foreach (var kvp in {{variableName}})
                {
                    {{GenerateWriteCode("kvp.Key", keyType, false)}}
                    {{GenerateWriteCode("kvp.Value", valueType, valueIsNullable)}}
                }
                writer.WriteEndMap();
                """;
        }
    }
}