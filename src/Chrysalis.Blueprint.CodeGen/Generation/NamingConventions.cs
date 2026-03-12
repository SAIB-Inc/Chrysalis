using System.Text;
using Chrysalis.Blueprint.CodeGen.Models;

namespace Chrysalis.Blueprint.CodeGen.Generation;

/// <summary>
/// Converts blueprint identifiers to valid C# names.
/// </summary>
internal static class NamingConventions
{
    /// <summary>
    /// Derives a C# type name from a blueprint definition key and schema node.
    /// </summary>
    public static string TypeNameFromDefinitionKey(string defKey, SchemaNode? schema)
    {
        string raw = schema?.Title ?? LastSegment(defKey);

        int angleBracket = raw.IndexOf('<');
        if (angleBracket >= 0)
        {
            raw = raw.Substring(0, angleBracket);
        }

        if (raw.StartsWith("Wrapped ", StringComparison.Ordinal))
        {
            raw = raw.Substring(8);
        }

        return SanitizeIdentifier(ToPascalCase(raw));
    }

    /// <summary>
    /// Derives a type name from a $ref path by resolving the key and extracting the last segment.
    /// </summary>
    public static string TypeNameFromRef(string refPath)
    {
        string? defKey = Analysis.SchemaResolver.ResolveRef(refPath);
        if (defKey == null)
        {
            return "IPlutusData";
        }

        return SanitizeIdentifier(ToPascalCase(LastSegment(defKey)));
    }

    /// <summary>
    /// Extracts the last path segment from a definition key.
    /// </summary>
    public static string LastSegment(string defKey)
    {
        int lastSlash = defKey.LastIndexOf('/');
        return lastSlash >= 0 ? defKey.Substring(lastSlash + 1) : defKey;
    }

    /// <summary>
    /// Converts snake_case or kebab-case to PascalCase.
    /// </summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        StringBuilder sb = new();
        bool capitalizeNext = true;

        foreach (char c in input)
        {
            if (c is '_' or '-' or ' ')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                _ = sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                _ = sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a validator title like "wizard.script.spend" to "WizardScriptSpend".
    /// </summary>
    public static string ValidatorClassName(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return "UnnamedValidator";
        }

        StringBuilder sb = new();
        bool capitalizeNext = true;

        foreach (char c in title)
        {
            if (c is '.' or '_' or '-' or ' ')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                _ = sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                _ = sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Derives a C# namespace from the blueprint preamble title.
    /// </summary>
    public static string NamespaceFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Blueprint";
        }

        return SanitizeIdentifier(ToPascalCase(title!)) + ".Blueprint";
    }

    /// <summary>
    /// Ensures the identifier is a valid C# identifier.
    /// </summary>
    public static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Unknown";
        }

        StringBuilder sb = new();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                _ = sb.Append(c);
            }
        }

        string result = sb.ToString();
        if (result.Length == 0)
        {
            return "Unknown";
        }

        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        if (IsCSharpKeyword(result))
        {
            result = "@" + result;
        }

        return result;
    }

    /// <summary>
    /// Detects name collisions and resolves them by prefixing with module path.
    /// </summary>
    public static Dictionary<string, string> ResolveCollisions(Dictionary<string, string> defKeyToName)
    {
        Dictionary<string, List<string>> nameToKeys = [];
        foreach (KeyValuePair<string, string> kv in defKeyToName)
        {
            if (!nameToKeys.TryGetValue(kv.Value, out List<string>? list))
            {
                list = [];
                nameToKeys[kv.Value] = list;
            }

            list.Add(kv.Key);
        }

        Dictionary<string, string> result = new(defKeyToName);
        foreach (KeyValuePair<string, List<string>> kv in nameToKeys)
        {
            if (kv.Value.Count <= 1)
            {
                continue;
            }

            foreach (string defKey in kv.Value)
            {
                int lastSlash = defKey.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    string module = defKey.Substring(0, lastSlash).Replace("/", "");
                    result[defKey] = ToPascalCase(module) + kv.Key;
                }
            }
        }

        return result;
    }

    private static bool IsCSharpKeyword(string word) => word switch
    {
        "abstract" or "as" or "base" or "bool" or "break" or "byte" or "case" or "catch" or "char" or "checked" or "class" or "const" or "continue" or "decimal" or "default" or "delegate" or "do" or "double" or "else" or "enum" or "event" or "explicit" or "extern" or "false" or "finally" or "fixed" or "float" or "for" or "foreach" or "goto" or "if" or "implicit" or "in" or "int" or "interface" or "internal" or "is" or "lock" or "long" or "namespace" or "new" or "null" or "object" or "operator" or "out" or "override" or "params" or "private" or "protected" or "public" or "readonly" or "ref" or "return" or "sbyte" or "sealed" or "short" or "sizeof" or "stackalloc" or "static" or "string" or "struct" or "switch" or "this" or "throw" or "true" or "try" or "typeof" or "uint" or "ulong" or "unchecked" or "unsafe" or "ushort" or "using" or "virtual" or "void" or "volatile" or "while" => true,
        _ => false,
    };
}
