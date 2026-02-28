using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static partial class Emitter
    {
        public static StringBuilder EmitSerializableTypeWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            string xmlSafeId = metadata.Indentifier.Replace("<", "{").Replace(">", "}");
            _ = sb.AppendLine($"/// <summary>");
            _ = sb.AppendLine($"/// Serializes a <see cref=\"{xmlSafeId}\"/> instance to CBOR format.");
            _ = sb.AppendLine($"/// </summary>");
            _ = sb.AppendLine($"/// <param name=\"writer\">The CBOR writer to serialize to.</param>");
            _ = sb.AppendLine($"/// <param name=\"data\">The <see cref=\"{xmlSafeId}\"/> instance to serialize.</param>");
            _ = sb.AppendLine($"public static new void Write(CborWriter writer, {metadata.FullyQualifiedName} data)");
            _ = sb.AppendLine("{");
            ICborSerializerEmitter emitter = GetEmitter(metadata);
            _ = emitter.EmitWriter(sb, metadata);
            _ = sb.AppendLine("}");

            return sb;
        }

        public static StringBuilder EmitSerializablePropertyWriter(StringBuilder sb, SerializablePropertyMetadata metadata)
        {
            string propertyName = $"data.{metadata.PropertyName}";

            // Special handling for CborLabel's Value property
            if (metadata.PropertyName == "Value" && metadata.PropertyType == "object")
            {
                _ = sb.AppendLine($"switch ({propertyName})");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("    case int i:");
                _ = sb.AppendLine("        writer.WriteInt32(i);");
                _ = sb.AppendLine("        break;");
                _ = sb.AppendLine("    case long l:");
                _ = sb.AppendLine("        writer.WriteInt64(l);");
                _ = sb.AppendLine("        break;");
                _ = sb.AppendLine("    case string s:");
                _ = sb.AppendLine("        writer.WriteTextString(s);");
                _ = sb.AppendLine("        break;");
                _ = sb.AppendLine("    default:");
                _ = sb.AppendLine($"        throw new InvalidOperationException($\"CborLabel value must be int, long, or string. Got: {{{propertyName}?.GetType()}}\");");
                _ = sb.AppendLine("}");
                return sb;
            }

            if (metadata.IsNullable)
            {
                _ = sb.AppendLine($"if ({propertyName} is null)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"writer.WriteNull();");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"else");
                _ = sb.AppendLine("{");
            }

            if (metadata.IsTypeNullable)
            {
                _ = sb.AppendLine($"if ({propertyName} is not null)");
                _ = sb.AppendLine("{");
                _ = EmitPrimitiveOrObjectWriter(sb, metadata, propertyName);
                _ = sb.AppendLine("}");
            }
            else
            {
                _ = EmitPrimitiveOrObjectWriter(sb, metadata, propertyName);
            }

            if (metadata.IsNullable)
            {
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine();

            return sb;
        }

        public static StringBuilder EmitPrimitivePropertyWriter(StringBuilder sb, string type, string propertyName, int? size = null)
        {
            bool isNullable = type.Contains("?");
            type = type.Replace("?", "");
            switch (type)
            {
                case "bool":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteBoolean({propertyName}.Value);"
                        : $"writer.WriteBoolean({propertyName});");
                    break;
                case "int":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteInt32({propertyName}.Value);"
                        : $"writer.WriteInt32({propertyName});");
                    break;
                case "uint":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteUInt32({propertyName}.Value);"
                        : $"writer.WriteUInt32({propertyName});");
                    break;
                case "long":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteInt64({propertyName}.Value);"
                        : $"writer.WriteInt64({propertyName});");
                    break;
                case "ulong":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteUInt64({propertyName}.Value);"
                        : $"writer.WriteUInt64({propertyName});");
                    break;
                case "float":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteSingle({propertyName}.Value);"
                        : $"writer.WriteSingle({propertyName});");
                    break;
                case "double":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteDouble({propertyName}.Value);"
                        : $"writer.WriteDouble({propertyName});");
                    break;
                case "decimal":
                    _ = sb.AppendLine(isNullable
                        ? $"writer.WriteDecimal({propertyName}.Value);"
                        : $"writer.WriteDecimal({propertyName});");
                    break;
                case "string":
                    _ = sb.AppendLine($"writer.WriteTextString({propertyName});");
                    break;
                case "byte[]?":
                case "byte[]":
                    if (size is not null)
                    {
                        _ = sb.AppendLine($"if ({propertyName}.Length > {size})");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"writer.WriteStartIndefiniteLengthByteString();");
                        _ = sb.AppendLine($"var {propertyName.Replace(".", "")}Chunks = {propertyName}.Chunk({size});");
                        _ = sb.AppendLine($"foreach (var chunk in {propertyName.Replace(".", "")}Chunks)");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"writer.WriteByteString(chunk);");
                        _ = sb.AppendLine("}");
                        _ = sb.AppendLine($"writer.WriteEndIndefiniteLengthByteString();");
                        _ = sb.AppendLine("}");
                        _ = sb.AppendLine("else");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"writer.WriteByteString({propertyName});");
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine($"writer.WriteByteString({propertyName});");
                    }

                    break;
                case "CborEncodedValue":
                case "Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                case "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                    _ = sb.AppendLine("writer.WriteTag(CborTag.EncodedCborDataItem);");
                    _ = sb.AppendLine($"writer.WriteByteString({propertyName}.Value);");
                    break;
                case "CborLabel":
                case "Chrysalis.Cbor.Types.CborLabel":
                case "global::Chrysalis.Cbor.Types.CborLabel":
                    _ = sb.AppendLine($"switch ({propertyName}.Value)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    case int i:");
                    _ = sb.AppendLine("        writer.WriteInt32(i);");
                    _ = sb.AppendLine("        break;");
                    _ = sb.AppendLine("    case long l:");
                    _ = sb.AppendLine("        writer.WriteInt64(l);");
                    _ = sb.AppendLine("        break;");
                    _ = sb.AppendLine("    case string s:");
                    _ = sb.AppendLine("        writer.WriteTextString(s);");
                    _ = sb.AppendLine("        break;");
                    _ = sb.AppendLine("    default:");
                    _ = sb.AppendLine($"        throw new InvalidOperationException($\"CborLabel value must be int, long, or string. Got: {{{propertyName}.Value?.GetType()}}\");");
                    _ = sb.AppendLine("}");
                    break;
                default:
                    break;
            }

            return sb;
        }

        public static StringBuilder EmitObjectPropertyWriter(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            if (metadata.IsList)
            {
                if (metadata.ListItemTypeFullName is null || metadata.ListItemType is null)
                {
                    throw new InvalidOperationException($"List item type is null for property {metadata.PropertyName}");
                }

                // Check attribute, runtime flag, or tracked indefinite state
                _ = sb.AppendLine($"bool useIndefiniteFor{metadata.PropertyName} = {(metadata.IsIndefinite ? "true" : "false")} || ");
                _ = sb.AppendLine($"    Chrysalis.Cbor.Serialization.IndefiniteStateTracker.IsIndefinite({propertyName});");
                _ = sb.AppendLine($"if (useIndefiniteFor{metadata.PropertyName})");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    writer.WriteStartArray(null);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("else");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    writer.WriteStartArray({propertyName}.Count());");
                _ = sb.AppendLine("}");

                _ = sb.AppendLine($"foreach (var item in {propertyName})");
                _ = sb.AppendLine("{");
                _ = metadata.IsListItemTypeOpenGeneric
                    ? EmitGenericWithTypeParamsWriter(sb, metadata.ListItemType, $"item")
                    : IsPrimitiveType(metadata.ListItemTypeFullName)
                        ? EmitPrimitivePropertyWriter(sb, metadata.ListItemTypeFullName, "item")
                        : sb.AppendLine($"{metadata.ListItemTypeFullName}.Write(writer, ({metadata.ListItemTypeFullName})item);");


                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"writer.WriteEndArray();");

                return sb;
            }

            if (metadata.IsMap)
            {
                if (metadata.MapKeyTypeFullName is null || metadata.MapValueTypeFullName is null)
                {
                    throw new InvalidOperationException($"Map key or value type is null for property {metadata.PropertyName}");
                }

                // Check attribute, runtime flag, or tracked indefinite state
                _ = sb.AppendLine($"bool useIndefiniteMapFor{metadata.PropertyName} = {(metadata.IsIndefinite ? "true" : "false")} || ");
                _ = sb.AppendLine($"    Chrysalis.Cbor.Serialization.IndefiniteStateTracker.IsIndefinite({propertyName});");
                _ = sb.AppendLine($"if (useIndefiniteMapFor{metadata.PropertyName})");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    writer.WriteStartMap(null);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("else");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    writer.WriteStartMap({propertyName}.Count());");
                _ = sb.AppendLine("}");

                _ = sb.AppendLine($"foreach (var kvp in {propertyName})");
                _ = sb.AppendLine("{");

                _ = metadata.IsMapKeyTypeOpenGeneric
                    ? EmitGenericWithTypeParamsWriter(sb, metadata.MapKeyTypeFullName, $"kvp.Key")
                    : IsPrimitiveType(metadata.MapKeyTypeFullName)
                        ? EmitPrimitivePropertyWriter(sb, metadata.MapKeyTypeFullName, $"kvp.Key")
                        : sb.AppendLine($"{metadata.MapKeyTypeFullName}.Write(writer, ({metadata.MapKeyTypeFullName})kvp.Key);");

                _ = metadata.IsMapValueTypeOpenGeneric
                    ? EmitGenericWithTypeParamsWriter(sb, metadata.MapValueTypeFullName, $"kvp.Value")
                    : IsPrimitiveType(metadata.MapValueTypeFullName)
                        ? EmitPrimitivePropertyWriter(sb, metadata.MapValueTypeFullName, $"kvp.Value")
                        : sb.AppendLine($"{metadata.MapValueTypeFullName}.Write(writer, ({metadata.MapValueTypeFullName})kvp.Value);");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"writer.WriteEndMap();");
                return sb;
            }

            _ = metadata.IsOpenGeneric
                ? EmitGenericWithTypeParamsWriter(sb, metadata.PropertyTypeFullName, propertyName)
                : sb.AppendLine($"{metadata.PropertyTypeFullName}.Write(writer, ({metadata.PropertyTypeFullName}){propertyName});");

            return sb;
        }

        public static StringBuilder EmitTagWriter(StringBuilder sb, int? tag)
        {
            if (tag.HasValue && tag.Value >= 0)
            {
                _ = sb.AppendLine($"writer.WriteTag((CborTag){tag});");
            }

            return sb;
        }

        public static StringBuilder EmitSerializableTypeValidatorWriter(StringBuilder sb, SerializableTypeMetadata metadata, string propertyName)
        {
            if (metadata.Validator is not null)
            {
                _ = sb.AppendLine($"{metadata.Validator} validator = new();");
                _ = sb.AppendLine($"if (!validator.Validate({propertyName})) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitGenericWithTypeParamsWriter(StringBuilder sb, string type, string propertyName)
        {
            _ = sb.AppendLine($"{GenericSerializationUtilFullname}.Write<{type}>(writer, {propertyName});");
            return sb;
        }

        public static StringBuilder EmitGenericWriter(StringBuilder sb, string type)
        {
            _ = sb.AppendLine($"{GenericSerializationUtilFullname}.Write(writer, {type});");
            return sb;
        }

        public static StringBuilder EmitPrimitiveOrObjectWriter(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            _ = metadata.IsOpenGeneric
                ? EmitGenericWithTypeParamsWriter(sb, metadata.PropertyType, propertyName)
                : IsPrimitiveType(metadata.PropertyType)
                    ? EmitPrimitivePropertyWriter(sb, metadata.PropertyType, propertyName, metadata.Size)
                    : EmitObjectPropertyWriter(sb, metadata, propertyName);

            return sb;
        }

        public static StringBuilder EmitCustomListWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = EmitPropertyCountWriter(sb, metadata);
            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                // Use indefinite if either attribute OR runtime flag is set
                // This supports both compile-time ([CborIndefinite]) and runtime (data.IsIndefinite) control
                if (metadata.IsIndefinite)
                {
                    // Force indefinite encoding due to attribute
                    _ = sb.AppendLine($"writer.WriteStartArray(null);");
                }
                else if (metadata.IsDefinite)
                {
                    // Force definite encoding due to attribute
                    _ = sb.AppendLine($"writer.WriteStartArray(propCount);");
                }
                else
                {
                    // No explicit attribute - check runtime flag for dynamic behavior
                    _ = sb.AppendLine($"bool useIndefinite = data.IsIndefinite;");
                    _ = sb.AppendLine("if (useIndefinite)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    writer.WriteStartArray(null);");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    writer.WriteStartArray(propCount);");
                    _ = sb.AppendLine("}");
                }
            }

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                _ = EmitSerializablePropertyWriter(sb, prop);
            }

            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                _ = sb.AppendLine($"writer.WriteEndArray();");
            }

            return sb;
        }

        public static StringBuilder EmitWriterValidation(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Validator is not null)
            {
                _ = sb.AppendLine($"{metadata.Validator} validator = new();");
                _ = sb.AppendLine($"if (!validator.Validate(data)) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitPreservedRawWriter(StringBuilder sb)
        {
            _ = sb.AppendLine($"if (data.Raw is not null)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"writer.WriteEncodedValue(data.Raw?.ToArray());");
            _ = sb.AppendLine("return;");
            _ = sb.AppendLine("}");
            return sb;
        }

        public static StringBuilder EmitPropertyCountWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = sb.AppendLine($"int propCount = 0;");

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                _ = prop.IsNullable || !prop.IsTypeNullable
                    ? sb.AppendLine("propCount++;")
                    : sb.AppendLine($"if (data.{prop.PropertyName} is not null) propCount++;");
            }

            _ = sb.AppendLine();

            return sb;
        }
    }
}