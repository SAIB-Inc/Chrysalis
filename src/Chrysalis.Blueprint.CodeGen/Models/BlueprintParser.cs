using System.Globalization;
using System.Text;

namespace Chrysalis.Blueprint.CodeGen.Models;

/// <summary>
/// Minimal recursive-descent JSON parser for CIP-0057 blueprint files.
/// Targets netstandard2.0 with no external dependencies.
/// </summary>
internal static class BlueprintParser
{
    /// <summary>
    /// Parses a JSON string into a <see cref="BlueprintFile"/>.
    /// </summary>
    public static BlueprintFile Parse(string json)
    {
        int pos = 0;
        Dictionary<string, object?> root = ParseObject(json, ref pos);
        return MapBlueprintFile(root);
    }

    #region JSON Tokenizer

    private static void SkipWhitespace(string json, ref int pos)
    {
        while (pos < json.Length && char.IsWhiteSpace(json[pos]))
        {
            pos++;
        }
    }

    private static string ParseString(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length || json[pos] != '"')
        {
            throw new FormatException($"Expected '\"' at position {pos}");
        }

        pos++;

        StringBuilder sb = new();
        while (pos < json.Length)
        {
            char c = json[pos++];
            if (c == '"')
            {
                return sb.ToString();
            }

            if (c == '\\' && pos < json.Length)
            {
                char esc = json[pos++];
                switch (esc)
                {
                    case '"': _ = sb.Append('"'); break;
                    case '\\': _ = sb.Append('\\'); break;
                    case '/': _ = sb.Append('/'); break;
                    case 'n': _ = sb.Append('\n'); break;
                    case 'r': _ = sb.Append('\r'); break;
                    case 't': _ = sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 <= json.Length)
                        {
                            string hex = json.Substring(pos, 4);
                            _ = sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                        }

                        break;
                    default: _ = sb.Append(esc); break;
                }
            }
            else
            {
                _ = sb.Append(c);
            }
        }

        throw new FormatException("Unterminated string");
    }

    private static object? ParseValue(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length)
        {
            throw new FormatException("Unexpected end of JSON");
        }

        char c = json[pos];
        if (c == '"') { return ParseString(json, ref pos); }
        if (c == '{') { return ParseObject(json, ref pos); }
        if (c == '[') { return ParseArray(json, ref pos); }
        if (c is 't' or 'f') { return ParseBool(json, ref pos); }
        if (c == 'n') { return ParseNull(json, ref pos); }
        if (c == '-' || char.IsDigit(c)) { return ParseNumber(json, ref pos); }

        throw new FormatException($"Unexpected character '{c}' at position {pos}");
    }

    private static Dictionary<string, object?> ParseObject(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (json[pos] != '{')
        {
            throw new FormatException($"Expected '{{' at position {pos}");
        }

        pos++;

        Dictionary<string, object?> dict = [];
        SkipWhitespace(json, ref pos);
        if (pos < json.Length && json[pos] == '}')
        {
            pos++;
            return dict;
        }

        while (true)
        {
            string key = ParseString(json, ref pos);
            SkipWhitespace(json, ref pos);
            if (json[pos] != ':')
            {
                throw new FormatException($"Expected ':' at position {pos}");
            }

            pos++;
            object? value = ParseValue(json, ref pos);
            dict[key] = value;

            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',')
            {
                pos++;
                continue;
            }

            if (pos < json.Length && json[pos] == '}')
            {
                pos++;
                return dict;
            }

            throw new FormatException($"Expected ',' or '}}' at position {pos}");
        }
    }

    private static List<object?> ParseArray(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (json[pos] != '[')
        {
            throw new FormatException($"Expected '[' at position {pos}");
        }

        pos++;

        List<object?> list = [];
        SkipWhitespace(json, ref pos);
        if (pos < json.Length && json[pos] == ']')
        {
            pos++;
            return list;
        }

        while (true)
        {
            object? value = ParseValue(json, ref pos);
            list.Add(value);

            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',')
            {
                pos++;
                continue;
            }

            if (pos < json.Length && json[pos] == ']')
            {
                pos++;
                return list;
            }

            throw new FormatException($"Expected ',' or ']' at position {pos}");
        }
    }

    private static object ParseNumber(string json, ref int pos)
    {
        int start = pos;
        if (json[pos] == '-')
        {
            pos++;
        }

        while (pos < json.Length && char.IsDigit(json[pos]))
        {
            pos++;
        }

        bool isFloat = false;
        if (pos < json.Length && json[pos] == '.')
        {
            isFloat = true;
            pos++;
            while (pos < json.Length && char.IsDigit(json[pos]))
            {
                pos++;
            }
        }

        if (pos < json.Length && (json[pos] == 'e' || json[pos] == 'E'))
        {
            isFloat = true;
            pos++;
            if (pos < json.Length && (json[pos] == '+' || json[pos] == '-'))
            {
                pos++;
            }

            while (pos < json.Length && char.IsDigit(json[pos]))
            {
                pos++;
            }
        }

        string num = json.Substring(start, pos - start);
        if (isFloat)
        {
            return double.Parse(num, CultureInfo.InvariantCulture);
        }

        if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            return l;
        }

        return double.Parse(num, CultureInfo.InvariantCulture);
    }

    private static bool ParseBool(string json, ref int pos)
    {
        if (json.Length >= pos + 4 && json.Substring(pos, 4) == "true")
        {
            pos += 4;
            return true;
        }

        if (json.Length >= pos + 5 && json.Substring(pos, 5) == "false")
        {
            pos += 5;
            return false;
        }

        throw new FormatException($"Expected 'true' or 'false' at position {pos}");
    }

    private static object? ParseNull(string json, ref int pos)
    {
        if (json.Length >= pos + 4 && json.Substring(pos, 4) == "null")
        {
            pos += 4;
            return null;
        }

        throw new FormatException($"Expected 'null' at position {pos}");
    }

    #endregion

    #region JSON to Model Mapping

    private static BlueprintFile MapBlueprintFile(Dictionary<string, object?> root)
    {
        BlueprintFile file = new();

        if (root.TryGetValue("preamble", out object? preambleObj) && preambleObj is Dictionary<string, object?> preamble)
        {
            file.Preamble = new BlueprintPreamble
            {
                Title = GetString(preamble, "title"),
                Description = GetString(preamble, "description"),
                Version = GetString(preamble, "version"),
                PlutusVersion = GetString(preamble, "plutusVersion"),
                License = GetString(preamble, "license")
            };
            if (preamble.TryGetValue("compiler", out object? compObj) && compObj is Dictionary<string, object?> comp)
            {
                file.Preamble.Compiler = new BlueprintCompiler
                {
                    Name = GetString(comp, "name"),
                    Version = GetString(comp, "version")
                };
            }
        }

        if (root.TryGetValue("validators", out object? valsObj) && valsObj is List<object?> vals)
        {
            foreach (object? v in vals)
            {
                if (v is Dictionary<string, object?> vDict)
                {
                    file.Validators.Add(MapValidator(vDict));
                }
            }
        }

        if (root.TryGetValue("definitions", out object? defsObj) && defsObj is Dictionary<string, object?> defs)
        {
            foreach (KeyValuePair<string, object?> kv in defs)
            {
                if (kv.Value is Dictionary<string, object?> defDict)
                {
                    file.Definitions[kv.Key] = MapSchemaNode(defDict);
                }
            }
        }

        return file;
    }

    private static BlueprintValidator MapValidator(Dictionary<string, object?> dict)
    {
        BlueprintValidator v = new()
        {
            Title = GetString(dict, "title"),
            CompiledCode = GetString(dict, "compiledCode"),
            Hash = GetString(dict, "hash")
        };

        if (dict.TryGetValue("datum", out object? datumObj) && datumObj is Dictionary<string, object?> datum)
        {
            v.Datum = MapSchemaNode(datum);
        }

        if (dict.TryGetValue("redeemer", out object? redeemerObj) && redeemerObj is Dictionary<string, object?> redeemer)
        {
            v.Redeemer = MapSchemaNode(redeemer);
        }

        if (dict.TryGetValue("parameters", out object? paramsObj) && paramsObj is List<object?> pars)
        {
            v.Parameters = [];
            foreach (object? p in pars)
            {
                if (p is Dictionary<string, object?> pDict)
                {
                    v.Parameters.Add(MapSchemaNode(pDict));
                }
            }
        }

        return v;
    }

    private static SchemaNode MapSchemaNode(Dictionary<string, object?> dict)
    {
        SchemaNode node = new()
        {
            Title = GetStringOrNull(dict, "title"),
            Description = GetStringOrNull(dict, "description"),
            Ref = GetStringOrNull(dict, "$ref"),
            DataType = GetStringOrNull(dict, "dataType")
        };

        if (dict.TryGetValue("index", out object? indexObj) && indexObj is long l)
        {
            node.Index = (int)l;
        }

        if (dict.TryGetValue("fields", out object? fieldsObj) && fieldsObj is List<object?> fields)
        {
            node.Fields = [];
            foreach (object? f in fields)
            {
                if (f is Dictionary<string, object?> fDict)
                {
                    node.Fields.Add(MapSchemaNode(fDict));
                }
            }
        }

        if (dict.TryGetValue("anyOf", out object? anyOfObj) && anyOfObj is List<object?> anyOf)
        {
            node.AnyOf = [];
            foreach (object? a in anyOf)
            {
                if (a is Dictionary<string, object?> aDict)
                {
                    node.AnyOf.Add(MapSchemaNode(aDict));
                }
            }
        }

        if (dict.TryGetValue("items", out object? itemsObj))
        {
            if (itemsObj is List<object?> itemsList)
            {
                node.Items = [];
                foreach (object? item in itemsList)
                {
                    if (item is Dictionary<string, object?> iDict)
                    {
                        node.Items.Add(MapSchemaNode(iDict));
                    }
                }
            }
            else if (itemsObj is Dictionary<string, object?> singleItem)
            {
                node.ItemsSchema = MapSchemaNode(singleItem);
            }
        }

        return node;
    }

    private static string GetString(Dictionary<string, object?> dict, string key) => dict.TryGetValue(key, out object? val) && val is string s ? s : "";

    private static string? GetStringOrNull(Dictionary<string, object?> dict, string key) => dict.TryGetValue(key, out object? val) && val is string s ? s : null;

    #endregion
}
