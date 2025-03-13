using System;
using System.Collections.Generic;
using System.Text;

namespace Chrysalis.Cbor.Generators.Utils
{
    /// <summary>
    /// Utility for handling primitive types in CBOR serialization/deserialization.
    /// </summary>
    public static class CborPrimitiveUtil
    {
        /// <summary>
        /// Checks if a type is a primitive type (including strings, numbers, etc.) in CBOR context.
        /// </summary>
        public static bool IsPrimitive(string fullyQualifiedName)
        {
            return fullyQualifiedName switch
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
                _ => IsStandardCollection(fullyQualifiedName) // Treat collections as primitives
            };
        }

        /// <summary>
        /// Checks if a type is a standard .NET collection that should be treated as a primitive.
        /// </summary>
        public static bool IsStandardCollection(string fullyQualifiedName)
        {
            return IsListType(fullyQualifiedName) || IsDictionaryType(fullyQualifiedName);
        }

        /// <summary>
        /// Checks if a type is a List<T> or derived from it.
        /// </summary>
        public static bool IsListType(string fullyQualifiedName)
        {
            return fullyQualifiedName.Contains("System.Collections.Generic.List<") ||
                   fullyQualifiedName.Contains("System.Collections.Generic.IList<") ||
                   fullyQualifiedName.Contains("System.Collections.Generic.ICollection<") ||
                   fullyQualifiedName.Contains("System.Collections.Generic.IEnumerable<");
        }

        /// <summary>
        /// Checks if a type is a Dictionary<TKey, TValue> or derived from it.
        /// </summary>
        public static bool IsDictionaryType(string fullyQualifiedName)
        {
            return fullyQualifiedName.Contains("System.Collections.Generic.Dictionary<") ||
                   fullyQualifiedName.Contains("System.Collections.Generic.IDictionary<");
        }

        /// <summary>
        /// Gets the appropriate CBOR writer call for a given variable of a primitive type.
        /// </summary>
        public static string GetWriteCall(string variableName, string typeName)
        {
            // Handle standard collections
            if (IsListType(typeName))
            {
                return GenerateListWriteCall(variableName, typeName);
            }
            else if (IsDictionaryType(typeName))
            {
                return GenerateDictionaryWriteCall(variableName, typeName);
            }

            // Handle regular primitives
            return typeName switch
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
                "System.Byte[]" or "byte[]" => $"writer.WriteByteString({variableName});",
                _ => throw new ArgumentException($"Unsupported primitive type: {typeName}")
            };
        }

        /// <summary>
        /// Gets the appropriate CBOR reader call for reading a value of a primitive type.
        /// </summary>
        public static string GetReadValueCall(string typeName)
        {
            // Standard collections should not use this method
            if (IsStandardCollection(typeName))
            {
                throw new InvalidOperationException("Collections should use GenerateReadCall instead");
            }

            // Handle regular primitives
            return typeName switch
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
                "System.Byte[]" or "byte[]" => "reader.ReadByteString()",
                _ => throw new ArgumentException($"Unsupported primitive type: {typeName}")
            };
        }

        /// <summary>
        /// Gets the appropriate CBOR reader call for reading a value of a primitive type into a variable.
        /// </summary>
        public static string GetReadCall(string variableName, string typeName)
        {
            // Handle standard collections
            if (IsListType(typeName))
            {
                return GenerateListReadCall(variableName.Replace("var ", ""), typeName);
            }
            else if (IsDictionaryType(typeName))
            {
                return GenerateDictionaryReadCall(variableName.Replace("var ", ""), typeName);
            }

            // For regular primitives
            return $"{variableName} = {GetReadValueCall(typeName)};";
        }

        /// <summary>
        /// Extracts the element type from a collection type.
        /// </summary>
        private static string ExtractElementType(string fullyQualifiedName)
        {
            int start = fullyQualifiedName.IndexOf('<') + 1;
            int end = fullyQualifiedName.LastIndexOf('>');

            if (IsDictionaryType(fullyQualifiedName))
            {
                // For Dictionary<TKey, TValue>, return TValue
                string content = fullyQualifiedName.Substring(start, end - start);
                int commaIndex = content.IndexOf(',');
                return content.Substring(commaIndex + 1).Trim();
            }
            else
            {
                // For List<T>, return T
                return fullyQualifiedName.Substring(start, end - start);
            }
        }

        /// <summary>
        /// Extracts the key type from a dictionary type.
        /// </summary>
        private static string ExtractKeyType(string fullyQualifiedName)
        {
            if (!IsDictionaryType(fullyQualifiedName))
                throw new ArgumentException("Type is not a dictionary type", nameof(fullyQualifiedName));

            int start = fullyQualifiedName.IndexOf('<') + 1;
            int end = fullyQualifiedName.LastIndexOf('>');
            string content = fullyQualifiedName.Substring(start, end - start);
            int commaIndex = content.IndexOf(',');
            return content.Substring(0, commaIndex).Trim();
        }

        /// <summary>
        /// Generates the write call for a List<T> type.
        /// </summary>
        private static string GenerateListWriteCall(string variableName, string fullyQualifiedName)
        {
            StringBuilder code = new();
            string elementType = ExtractElementType(fullyQualifiedName);

            code.AppendLine($"writer.WriteStartArray({variableName}.Count);");
            code.AppendLine($"foreach (var item in {variableName})");
            code.AppendLine("{");

            if (IsPrimitive(elementType) && !IsStandardCollection(elementType))
            {
                // Handle primitive element types directly
                code.AppendLine("    " + GetWriteCall("item", elementType));
            }
            else if (IsStandardCollection(elementType))
            {
                // Nested collection
                code.AppendLine("    if (item != null)");
                code.AppendLine("    {");
                code.AppendLine("        " + GetWriteCall("item", elementType));
                code.AppendLine("    }");
                code.AppendLine("    else");
                code.AppendLine("    {");
                code.AppendLine("        writer.WriteNull();");
                code.AppendLine("    }");
            }
            else
            {
                // Custom type with its own Write method
                code.AppendLine("    if (item != null)");
                code.AppendLine("    {");
                // Use fully qualified name to avoid ambiguity
                code.AppendLine($"        {elementType}.Write(writer, item);");
                code.AppendLine("    }");
                code.AppendLine("    else");
                code.AppendLine("    {");
                code.AppendLine("        writer.WriteNull();");
                code.AppendLine("    }");
            }

            code.AppendLine("}");
            code.AppendLine("writer.WriteEndArray();");

            return code.ToString();
        }

        /// <summary>
        /// Generates the write call for a Dictionary<TKey, TValue> type.
        /// </summary>
        private static string GenerateDictionaryWriteCall(string variableName, string fullyQualifiedName)
        {
            StringBuilder code = new();
            string keyType = ExtractKeyType(fullyQualifiedName);
            string valueType = ExtractElementType(fullyQualifiedName);

            code.AppendLine($"writer.WriteStartMap({variableName}.Count);");
            code.AppendLine($"foreach (var kvp in {variableName})");
            code.AppendLine("{");

            // Write the key
            if (keyType == "System.String")
            {
                code.AppendLine("    writer.WriteTextString(kvp.Key);");
            }
            else
            {
                code.AppendLine("    " + GetWriteCall("kvp.Key", keyType));
            }

            // Write the value
            if (IsPrimitive(valueType) && !IsStandardCollection(valueType))
            {
                code.AppendLine("    " + GetWriteCall("kvp.Value", valueType));
            }
            else if (IsStandardCollection(valueType))
            {
                code.AppendLine("    if (kvp.Value != null)");
                code.AppendLine("    {");
                code.AppendLine("        " + GetWriteCall("kvp.Value", valueType));
                code.AppendLine("    }");
                code.AppendLine("    else");
                code.AppendLine("    {");
                code.AppendLine("        writer.WriteNull();");
                code.AppendLine("    }");
            }
            else
            {
                // Custom type with its own Write method
                code.AppendLine("    if (kvp.Value != null)");
                code.AppendLine("    {");
                // Use fully qualified name to avoid ambiguity
                code.AppendLine($"        {valueType}.Write(writer, kvp.Value);");
                code.AppendLine("    }");
                code.AppendLine("    else");
                code.AppendLine("    {");
                code.AppendLine("        writer.WriteNull();");
                code.AppendLine("    }");
            }

            code.AppendLine("}");
            code.AppendLine("writer.WriteEndMap();");

            return code.ToString();
        }

        /// <summary>
        /// Generates the read call for a List<T> type.
        /// </summary>
        private static string GenerateListReadCall(string variableName, string fullyQualifiedName)
        {
            StringBuilder code = new();
            string elementType = ExtractElementType(fullyQualifiedName);

            code.AppendLine($"var {variableName} = new {fullyQualifiedName}();");
            code.AppendLine("reader.ReadStartArray();");
            code.AppendLine("while(reader.PeekState() != CborReaderState.EndArray)");
            code.AppendLine("{");

            if (IsPrimitive(elementType) && !IsStandardCollection(elementType))
            {
                // Simple primitive elements
                code.AppendLine($"    var element = {GetReadValueCall(elementType)};");
                code.AppendLine($"    {variableName}.Add(element);");
            }
            else if (IsStandardCollection(elementType))
            {
                // Nested collection
                code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                code.AppendLine("    {");
                code.AppendLine("        reader.ReadNull();");
                code.AppendLine($"        {variableName}.Add(null);");
                code.AppendLine("    }");
                code.AppendLine("    else");
                code.AppendLine("    {");
                code.AppendLine($"        {elementType} element = new {elementType}();");
                code.AppendLine("        " + GetReadCall("element", elementType));
                code.AppendLine($"        {variableName}.Add(element);");
                code.AppendLine("    }");
            }
            else
            {
                // Custom type with its own Read method
                code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
                code.AppendLine("    {");
                code.AppendLine("        reader.ReadNull();");
                code.AppendLine($"        {variableName}.Add(null);");
                code.AppendLine("    }");
                code.AppendLine("    else");
                code.AppendLine("    {");
                // Use fully qualified name to avoid ambiguity
                code.AppendLine($"        var element = {elementType}.Read(reader);");
                code.AppendLine($"        {variableName}.Add(element);");
                code.AppendLine("    }");
            }

            code.AppendLine("}");
            code.AppendLine("reader.ReadEndArray();");

            return code.ToString();
        }

        /// <summary>
        /// Generates the read call for a Dictionary<TKey, TValue> type.
        /// </summary>
        private static string GenerateDictionaryReadCall(string variableName, string fullyQualifiedName)
        {
            StringBuilder code = new();
            string keyType = ExtractKeyType(fullyQualifiedName);
            string valueType = ExtractElementType(fullyQualifiedName);

            code.AppendLine($"var {variableName} = new {fullyQualifiedName}();");
            code.AppendLine("reader.ReadStartMap();");
            code.AppendLine("while(reader.PeekState() != CborReaderState.EndMap)");
            code.AppendLine("{");

            // Read the key
            if (keyType == "System.String")
            {
                code.AppendLine("    string key = reader.ReadTextString();");
            }
            else
            {
                code.AppendLine($"    {keyType} key = {GetReadValueCall(keyType)};");
            }

            // Read the value
            code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
            code.AppendLine("    {");
            code.AppendLine("        reader.ReadNull();");
            code.AppendLine($"        {variableName}[key] = default;");
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");

            if (IsPrimitive(valueType) && !IsStandardCollection(valueType))
            {
                code.AppendLine($"        {valueType} value = {GetReadValueCall(valueType)};");
                code.AppendLine($"        {variableName}[key] = value;");
            }
            else if (IsStandardCollection(valueType))
            {
                code.AppendLine($"        {valueType} value = new {valueType}();");
                code.AppendLine("        " + GetReadCall("value", valueType));
                code.AppendLine($"        {variableName}[key] = value;");
            }
            else
            {
                // Custom type with its own Read method
                // Use fully qualified name to avoid ambiguity
                code.AppendLine($"        {valueType} value = {valueType}.Read(reader);");
                code.AppendLine($"        {variableName}[key] = value;");
            }

            code.AppendLine("    }");
            code.AppendLine("}");
            code.AppendLine("reader.ReadEndMap();");

            return code.ToString();
        }
    }
}