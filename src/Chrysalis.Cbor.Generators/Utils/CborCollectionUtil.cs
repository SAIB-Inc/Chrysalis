using System;
using System.Collections.Generic;
using System.Text;
using Chrysalis.Cbor.Generators.Models;
using Chrysalis.Cbor.Generators.Utils;

namespace Chrysalis.Cbor.Generators.Utils;
/// <summary>
/// Utility for generating CBOR serialization/deserialization code for standard .NET collections.
/// </summary>
public static class CborCollectionUtil
{
    /// <summary>
    /// Checks if a type is a standard .NET collection.
    /// </summary>
    /// <param name="fullyQualifiedName">Fully qualified name of the type.</param>
    /// <returns>True if the type is a standard collection.</returns>
    public static bool IsStandardCollection(string fullyQualifiedName)
    {
        return IsListType(fullyQualifiedName) || IsDictionaryType(fullyQualifiedName);
    }

    /// <summary>
    /// Checks if a type is a List<T> or derived from it.
    /// </summary>
    public static bool IsListType(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("System.Collections.Generic.List<") ||
               fullyQualifiedName.StartsWith("System.Collections.Generic.IList<") ||
               fullyQualifiedName.StartsWith("System.Collections.Generic.ICollection<") ||
               fullyQualifiedName.StartsWith("System.Collections.Generic.IEnumerable<");
    }

    /// <summary>
    /// Checks if a type is a Dictionary<TKey, TValue> or derived from it.
    /// </summary>
    public static bool IsDictionaryType(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("System.Collections.Generic.Dictionary<") ||
               fullyQualifiedName.StartsWith("System.Collections.Generic.IDictionary<");
    }

    /// <summary>
    /// Extracts the element type from a collection type.
    /// </summary>
    public static string ExtractElementType(string fullyQualifiedName)
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
    public static string ExtractKeyType(string fullyQualifiedName)
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
    public static string GenerateListWriteCall(string variableName, string fullyQualifiedName)
    {
        StringBuilder code = new();
        string elementType = ExtractElementType(fullyQualifiedName);

        code.AppendLine($"writer.WriteStartArray({variableName}.Count);");
        code.AppendLine($"foreach (var item in {variableName})");
        code.AppendLine("{");

        if (CborPrimitiveUtil.IsPrimitive(elementType))
        {
            code.AppendLine("    " + CborPrimitiveUtil.GetWriteCall("item", elementType));
        }
        else if (IsStandardCollection(elementType))
        {
            // Recursive handling for nested collections
            code.AppendLine("    if (item != null)");
            code.AppendLine("    {");
            code.AppendLine("        " + GenerateCollectionWriteCall("item", elementType));
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");
            code.AppendLine("        writer.WriteNull();");
            code.AppendLine("    }");
        }
        else
        {
            // Assume the element type has its own Write method
            string typeName = elementType.Split('.').Last();
            code.AppendLine($"    if (item != null)");
            code.AppendLine("    {");
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
    public static string GenerateDictionaryWriteCall(string variableName, string fullyQualifiedName)
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
            code.AppendLine("    " + CborPrimitiveUtil.GetWriteCall("kvp.Key", keyType));
        }

        // Write the value
        if (CborPrimitiveUtil.IsPrimitive(valueType))
        {
            code.AppendLine("    " + CborPrimitiveUtil.GetWriteCall("kvp.Value", valueType));
        }
        else if (IsStandardCollection(valueType))
        {
            code.AppendLine("    if (kvp.Value != null)");
            code.AppendLine("    {");
            code.AppendLine("        " + GenerateCollectionWriteCall("kvp.Value", valueType));
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");
            code.AppendLine("        writer.WriteNull();");
            code.AppendLine("    }");
        }
        else
        {
            // Assume the value type has its own Write method
            string typeName = valueType.Split('.').Last();
            code.AppendLine($"    if (kvp.Value != null)");
            code.AppendLine("    {");
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
    /// Generates the appropriate write call based on the collection type.
    /// </summary>
    public static string GenerateCollectionWriteCall(string variableName, string fullyQualifiedName)
    {
        if (IsListType(fullyQualifiedName))
        {
            return GenerateListWriteCall(variableName, fullyQualifiedName);
        }
        else if (IsDictionaryType(fullyQualifiedName))
        {
            return GenerateDictionaryWriteCall(variableName, fullyQualifiedName);
        }

        throw new ArgumentException($"Unsupported collection type: {fullyQualifiedName}");
    }

    /// <summary>
    /// Generates the read call for a List<T> type.
    /// </summary>
    public static string GenerateListReadCall(string variableName, string fullyQualifiedName)
    {
        StringBuilder code = new();
        string elementType = ExtractElementType(fullyQualifiedName);

        code.AppendLine($"var {variableName} = new {fullyQualifiedName}();");
        code.AppendLine("int count = reader.ReadStartArray();");
        code.AppendLine("for (int i = 0; i < count; i++)");
        code.AppendLine("{");

        if (CborPrimitiveUtil.IsPrimitive(elementType))
        {
            code.AppendLine("    var element = " + CborPrimitiveUtil.GetReadValueCall(elementType) + ";");
            code.AppendLine($"    {variableName}.Add(element);");
        }
        else if (IsStandardCollection(elementType))
        {
            code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
            code.AppendLine("    {");
            code.AppendLine("        reader.ReadNull();");
            code.AppendLine($"        {variableName}.Add(null);");
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");
            code.AppendLine("        " + GenerateCollectionReadCall("element", elementType));
            code.AppendLine($"        {variableName}.Add(element);");
            code.AppendLine("    }");
        }
        else
        {
            // Assume the element type has its own Read method
            string typeName = elementType.Split('.').Last();
            code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
            code.AppendLine("    {");
            code.AppendLine("        reader.ReadNull();");
            code.AppendLine($"        {variableName}.Add(null);");
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");
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
    public static string GenerateDictionaryReadCall(string variableName, string fullyQualifiedName)
    {
        StringBuilder code = new();
        string keyType = ExtractKeyType(fullyQualifiedName);
        string valueType = ExtractElementType(fullyQualifiedName);

        code.AppendLine($"var {variableName} = new {fullyQualifiedName}();");
        code.AppendLine("int count = reader.ReadStartMap();");
        code.AppendLine("for (int i = 0; i < count; i++)");
        code.AppendLine("{");

        // Read the key
        if (keyType == "System.String")
        {
            code.AppendLine("    string key = reader.ReadTextString();");
        }
        else
        {
            code.AppendLine("    var key = " + CborPrimitiveUtil.GetReadValueCall(keyType) + ";");
        }

        // Read the value
        if (CborPrimitiveUtil.IsPrimitive(valueType))
        {
            code.AppendLine("    var value = " + CborPrimitiveUtil.GetReadValueCall(valueType) + ";");
        }
        else if (IsStandardCollection(valueType))
        {
            code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
            code.AppendLine("    {");
            code.AppendLine("        reader.ReadNull();");
            code.AppendLine("        var value = null;");
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");
            code.AppendLine("        " + GenerateCollectionReadCall("value", valueType));
            code.AppendLine("    }");
        }
        else
        {
            // Assume the value type has its own Read method
            string typeName = valueType.Split('.').Last();
            code.AppendLine("    if (reader.PeekState() == CborReaderState.Null)");
            code.AppendLine("    {");
            code.AppendLine("        reader.ReadNull();");
            code.AppendLine("        var value = null;");
            code.AppendLine("    }");
            code.AppendLine("    else");
            code.AppendLine("    {");
            code.AppendLine($"        var value = {valueType}.Read(reader);");
            code.AppendLine("    }");
        }

        code.AppendLine($"    {variableName}[key] = value;");
        code.AppendLine("}");
        code.AppendLine("reader.ReadEndMap();");

        return code.ToString();
    }

    /// <summary>
    /// Generates the appropriate read call based on the collection type.
    /// </summary>
    public static string GenerateCollectionReadCall(string variableName, string fullyQualifiedName)
    {
        if (IsListType(fullyQualifiedName))
        {
            return GenerateListReadCall(variableName, fullyQualifiedName);
        }
        else if (IsDictionaryType(fullyQualifiedName))
        {
            return GenerateDictionaryReadCall(variableName, fullyQualifiedName);
        }

        throw new ArgumentException($"Unsupported collection type: {fullyQualifiedName}");
    }
}