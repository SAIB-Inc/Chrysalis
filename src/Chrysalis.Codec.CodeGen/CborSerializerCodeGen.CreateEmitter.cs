using System.Globalization;
using System.Text;

namespace Chrysalis.Codec.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    internal static partial class Emitter
    {
        public static StringBuilder EmitCreateMethod(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.SerializationType == SerializationType.Union)
            {
                return sb;
            }

            // Build parameter list
            List<string> requiredParams = [];
            List<string> optionalParams = [];

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string paramName = ToCamelCase(prop.PropertyName);
                string paramType = prop.PropertyTypeFullName;

                if (metadata.SerializationType == SerializationType.Map && prop.IsTypeNullable)
                {
                    optionalParams.Add($"{paramType} {paramName} = default");
                }
                else if (metadata.SerializationType == SerializationType.List && prop.IsTypeNullable)
                {
                    optionalParams.Add($"{paramType} {paramName} = default");
                }
                else
                {
                    requiredParams.Add($"{paramType} {paramName}");
                }
            }

            // For Constr with dynamic index, add constrIndex param
            if (metadata.SerializationType == SerializationType.Constr
                && (metadata.CborIndex is null or < 0))
            {
                requiredParams.Insert(0, "int constrIndex");
            }

            List<string> allParams = [.. requiredParams, .. optionalParams];
            string paramList = string.Join(", ", allParams);

            // Emit method
            _ = sb.AppendLine();
            _ = sb.AppendLine($"/// <summary>Creates a new <see cref=\"{metadata.Indentifier.Replace("<", "{").Replace(">", "}")}\"/> instance from values.</summary>");
            _ = sb.AppendLine($"public static {metadata.FullyQualifiedName} Create({paramList})");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("var _createBuffer = new ArrayBufferWriter<byte>();");
            _ = sb.AppendLine("IBufferWriter<byte> output = _createBuffer;");
            _ = sb.AppendLine("var writer = new CborWriter(_createBuffer);");

            if (metadata.SerializationType == SerializationType.Container)
            {
                EmitCreateBodyContainer(sb, metadata);
            }
            else if (metadata.SerializationType == SerializationType.List)
            {
                EmitCreateBodyList(sb, metadata);
            }
            else if (metadata.SerializationType == SerializationType.Map)
            {
                EmitCreateBodyMap(sb, metadata);
            }
            else if (metadata.SerializationType == SerializationType.Constr)
            {
                EmitCreateBodyConstr(sb, metadata);
            }

            _ = sb.AppendLine($"return Read((ReadOnlyMemory<byte>)_createBuffer.WrittenMemory.ToArray());");
            _ = sb.AppendLine("}");

            return sb;
        }

        private static void EmitCreateBodyContainer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Properties.Count == 0)
            {
                return;
            }

            _ = EmitTagWriter(sb, metadata.CborTag);
            SerializablePropertyMetadata prop = metadata.Properties[0];
            string paramName = ToCamelCase(prop.PropertyName);
            _ = EmitSerializablePropertyWriter(sb, prop, paramName);
        }

        private static void EmitCreateBodyList(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = EmitTagWriter(sb, metadata.CborTag);

            if (metadata.IsDefinite)
            {
                // [CborDefinite]: fixed-size array, write null for missing nullable fields
                int fixedSize = metadata.DefiniteSize ?? metadata.Properties.Count;
                _ = sb.AppendLine($"int propCount = {fixedSize};");
            }
            else
            {
                EmitCreatePropertyCount(sb, metadata);
            }

            _ = sb.AppendLine("writer.WriteBeginArray(propCount);");

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string paramName = ToCamelCase(prop.PropertyName);
                if (metadata.IsDefinite && prop.IsTypeNullable)
                {
                    // [CborDefinite]: always write the field, use null when missing
                    _ = sb.AppendLine($"if ({paramName} is not null)");
                    _ = sb.AppendLine("{");
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("writer.WriteNull();");
                    _ = sb.AppendLine("}");
                }
                else if (prop.IsTypeNullable)
                {
                    // Variable-length: skip nullable fields entirely
                    _ = sb.AppendLine($"if ({paramName} is not null)");
                    _ = sb.AppendLine("{");
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                    _ = sb.AppendLine("}");
                }
                else
                {
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                }
            }

            _ = sb.AppendLine("writer.WriteEndArray(propCount);");
        }

        private static void EmitCreateBodyMap(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = EmitTagWriter(sb, metadata.CborTag);

            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;

            // Count non-null properties
            EmitCreatePropertyCount(sb, metadata);

            _ = sb.AppendLine("writer.WriteBeginMap(propCount);");

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string paramName = ToCamelCase(prop.PropertyName);
                string keyLiteral = isIntKey
                    ? prop.PropertyKeyInt?.ToString(CultureInfo.InvariantCulture)!
                    : $"\"{prop.PropertyKeyString}\"";
                string writeKey = isIntKey
                    ? $"writer.WriteInt32({keyLiteral});"
                    : $"writer.WriteString({keyLiteral});";

                if (prop.IsTypeNullable)
                {
                    _ = sb.AppendLine($"if ({paramName} is not null)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine(writeKey);
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                    _ = sb.AppendLine("}");
                }
                else
                {
                    _ = sb.AppendLine(writeKey);
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                }
            }

            _ = sb.AppendLine("writer.WriteEndMap(propCount);");
        }

        private static void EmitCreateBodyConstr(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = EmitTagWriter(sb, metadata.CborTag);

            // Write constr tag
            if (metadata.CborIndex is null or < 0)
            {
                // Dynamic: use the constrIndex parameter
                _ = sb.AppendLine($"writer.WriteSemanticTag((ulong){ResolveTagExpression("constrIndex")});");
            }
            else
            {
                // Fixed: hardcode the resolved tag
                _ = sb.AppendLine($"writer.WriteSemanticTag((ulong){ResolveTag(metadata.CborIndex)});");
            }

            // Dynamic CborConstr (e.g. PlutusConstr) has a single ICborMaybeIndefList
            // property that writes its own array — skip the outer array wrapper to
            // match the Write and Read paths (WriteEmitter.cs:369, ReadEmitter.cs:58).
            bool isDynamicConstr = metadata.CborIndex is null or < 0;

            if (!isDynamicConstr)
            {
                // Count non-null properties for array size
                EmitCreatePropertyCount(sb, metadata);

                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine("writer.WriteBeginArray(-1);");
                }
                else
                {
                    _ = sb.AppendLine("writer.WriteBeginArray(propCount);");
                }
            }

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string paramName = ToCamelCase(prop.PropertyName);
                if (prop.IsTypeNullable)
                {
                    _ = sb.AppendLine($"if ({paramName} is not null)");
                    _ = sb.AppendLine("{");
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                    _ = sb.AppendLine("}");
                }
                else
                {
                    _ = EmitSerializablePropertyWriter(sb, prop, paramName);
                }
            }

            if (!isDynamicConstr)
            {
                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine("output.GetSpan(1)[0] = 0xFF; output.Advance(1);");
                }
                else
                {
                    _ = sb.AppendLine("writer.WriteEndArray(propCount);");
                }
            }
        }

        private static void EmitCreatePropertyCount(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = sb.AppendLine("int propCount = 0;");
            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string paramName = ToCamelCase(prop.PropertyName);
                if (prop.IsTypeNullable)
                {
                    _ = sb.AppendLine($"if ({paramName} is not null) propCount++;");
                }
                else
                {
                    _ = sb.AppendLine("propCount++;");
                }
            }
        }

        /// <summary>
        /// Emits a runtime expression that converts a logical constructor index to a CBOR tag.
        /// </summary>
        private static string ResolveTagExpression(string varName)
            => $"({varName} > 6 ? 1280 - 7 + {varName} : 121 + {varName})";

        // Names used as local variables in the generated Create() body
        private static readonly HashSet<string> CreateReservedNames =
            ["output", "writer", "buffer", "propCount", "_createBuffer"];

        private static readonly HashSet<string> CSharpKeywords =
        [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal",
            "is", "lock", "long", "namespace", "new", "null", "object",
            "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong",
            "unchecked", "unsafe", "ushort", "using", "virtual", "void",
            "volatile", "while", "value"
        ];

        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            string camel = char.ToLowerInvariant(name[0]) + name.Substring(1);
            if (CSharpKeywords.Contains(camel))
            {
                return "@" + camel;
            }

            if (CreateReservedNames.Contains(camel))
            {
                return camel + "Value";
            }

            return camel;
        }
    }
}
