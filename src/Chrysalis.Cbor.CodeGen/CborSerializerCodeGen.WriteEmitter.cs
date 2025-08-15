using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static partial class Emitter
    {
        public static StringBuilder EmitSerializableTypeWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            sb.AppendLine($"public static new void Write(CborWriter writer, {metadata.FullyQualifiedName} data)");
            sb.AppendLine("{");
            ICborSerializerEmitter emitter = GetEmitter(metadata);
            emitter.EmitWriter(sb, metadata);
            sb.AppendLine("}");

            return sb;
        }

        public static StringBuilder EmitSerializablePropertyWriter(StringBuilder sb, SerializablePropertyMetadata metadata)
        {
            string propertyName = $"data.{metadata.PropertyName}";
            
            // Special handling for CborLabel's Value property
            if (metadata.PropertyName == "Value" && metadata.PropertyType == "object")
            {
                sb.AppendLine($"switch ({propertyName})");
                sb.AppendLine("{");
                sb.AppendLine("    case int i:");
                sb.AppendLine("        writer.WriteInt32(i);");
                sb.AppendLine("        break;");
                sb.AppendLine("    case long l:");
                sb.AppendLine("        writer.WriteInt64(l);");
                sb.AppendLine("        break;");
                sb.AppendLine("    case string s:");
                sb.AppendLine("        writer.WriteTextString(s);");
                sb.AppendLine("        break;");
                sb.AppendLine("    default:");
                sb.AppendLine($"        throw new InvalidOperationException($\"CborLabel value must be int, long, or string. Got: {{{propertyName}?.GetType()}}\");");
                sb.AppendLine("}");
                return sb;
            }
            
            if (metadata.IsNullable)
            {
                sb.AppendLine($"if ({propertyName} is null)");
                sb.AppendLine("{");
                sb.AppendLine($"writer.WriteNull();");
                sb.AppendLine("}");
                sb.AppendLine($"else");
                sb.AppendLine("{");
            }

            if (metadata.IsTypeNullable)
            {
                sb.AppendLine($"if ({propertyName} is not null)");
                sb.AppendLine("{");
                EmitPrimitiveOrObjectWriter(sb, metadata, propertyName);
                sb.AppendLine("}");
            }
            else
            {
                EmitPrimitiveOrObjectWriter(sb, metadata, propertyName);
            }

            if (metadata.IsNullable) sb.AppendLine("}");

            sb.AppendLine();

            return sb;
        }

        public static StringBuilder EmitPrimitivePropertyWriter(StringBuilder sb, string type, string propertyName, int? size = null)
        {
            bool isNullable = type.Contains("?");
            type = type.Replace("?", "");
            switch (type)
            {
                case "bool":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteBoolean({propertyName}.Value);"
                        : $"writer.WriteBoolean({propertyName});");
                    break;
                case "int":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteInt32({propertyName}.Value);"
                        : $"writer.WriteInt32({propertyName});");
                    break;
                case "uint":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteUInt32({propertyName}.Value);"
                        : $"writer.WriteUInt32({propertyName});");
                    break;
                case "long":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteInt64({propertyName}.Value);"
                        : $"writer.WriteInt64({propertyName});");
                    break;
                case "ulong":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteUInt64({propertyName}.Value);"
                        : $"writer.WriteUInt64({propertyName});");
                    break;
                case "float":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteSingle({propertyName}.Value);"
                        : $"writer.WriteSingle({propertyName});");
                    break;
                case "double":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteDouble({propertyName}.Value);"
                        : $"writer.WriteDouble({propertyName});");
                    break;
                case "decimal":
                    sb.AppendLine(isNullable
                        ? $"writer.WriteDecimal({propertyName}.Value);"
                        : $"writer.WriteDecimal({propertyName});");
                    break;
                case "string":
                    sb.AppendLine($"writer.WriteTextString({propertyName});");
                    break;
                case "byte[]?":
                case "byte[]":
                    if (size is not null)
                    {
                        sb.AppendLine($"if ({propertyName}.Length > {size})");
                        sb.AppendLine("{");
                        sb.AppendLine($"writer.WriteStartIndefiniteLengthByteString();");
                        sb.AppendLine($"var {propertyName.Replace(".", "")}Chunks = {propertyName}.Chunk({size});");
                        sb.AppendLine($"foreach (var chunk in {propertyName.Replace(".", "")}Chunks)");
                        sb.AppendLine("{");
                        sb.AppendLine($"writer.WriteByteString(chunk);");
                        sb.AppendLine("}");
                        sb.AppendLine($"writer.WriteEndIndefiniteLengthByteString();");
                        sb.AppendLine("}");
                        sb.AppendLine("else");
                        sb.AppendLine("{");
                        sb.AppendLine($"writer.WriteByteString({propertyName});");
                        sb.AppendLine("}");
                    }
                    else
                    {
                        sb.AppendLine($"writer.WriteByteString({propertyName});");
                    }

                    break;
                case "CborEncodedValue":
                case "Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                case "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                    sb.AppendLine("writer.WriteTag(CborTag.EncodedCborDataItem);");
                    sb.AppendLine($"writer.WriteByteString({propertyName}.Value);");
                    break;
                case "CborLabel":
                case "Chrysalis.Cbor.Types.CborLabel":
                case "global::Chrysalis.Cbor.Types.CborLabel":
                    sb.AppendLine($"switch ({propertyName}.Value)");
                    sb.AppendLine("{");
                    sb.AppendLine("    case int i:");
                    sb.AppendLine("        writer.WriteInt32(i);");
                    sb.AppendLine("        break;");
                    sb.AppendLine("    case long l:");
                    sb.AppendLine("        writer.WriteInt64(l);");
                    sb.AppendLine("        break;");
                    sb.AppendLine("    case string s:");
                    sb.AppendLine("        writer.WriteTextString(s);");
                    sb.AppendLine("        break;");
                    sb.AppendLine("    default:");
                    sb.AppendLine($"        throw new InvalidOperationException($\"CborLabel value must be int, long, or string. Got: {{{propertyName}.Value?.GetType()}}\");");
                    sb.AppendLine("}");
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

                if (metadata.IsIndefinite)
                {
                    sb.AppendLine($"writer.WriteStartArray(null);");
                }
                else
                {
                    sb.AppendLine($"writer.WriteStartArray({propertyName}.Count());");
                }

                sb.AppendLine($"foreach (var item in {propertyName})");
                sb.AppendLine("{");
                if (metadata.IsListItemTypeOpenGeneric)
                {
                    EmitGenericWithTypeParamsWriter(sb, metadata.ListItemType, $"item");
                }
                else
                {
                    if (IsPrimitiveType(metadata.ListItemTypeFullName))
                    {
                        EmitPrimitivePropertyWriter(sb, metadata.ListItemTypeFullName, "item");
                    }
                    else
                    {
                        sb.AppendLine($"{metadata.ListItemTypeFullName}.Write(writer, item);");
                    }
                }


                sb.AppendLine("}");
                sb.AppendLine($"writer.WriteEndArray();");

                return sb;
            }

            if (metadata.IsMap)
            {
                if (metadata.MapKeyTypeFullName is null || metadata.MapValueTypeFullName is null)
                {
                    throw new InvalidOperationException($"Map key or value type is null for property {metadata.PropertyName}");
                }

                if (metadata.IsIndefinite)
                {
                    sb.AppendLine($"writer.WriteStartMap(null);");
                }
                else
                {
                    sb.AppendLine($"writer.WriteStartMap({propertyName}.Count());");
                }

                sb.AppendLine($"foreach (var kvp in {propertyName})");
                sb.AppendLine("{");

                if (metadata.IsMapKeyTypeOpenGeneric)
                {
                    EmitGenericWithTypeParamsWriter(sb, metadata.MapKeyTypeFullName, $"kvp.Key");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapKeyTypeFullName))
                    {
                        EmitPrimitivePropertyWriter(sb, metadata.MapKeyTypeFullName, $"kvp.Key");
                    }
                    else
                    {
                        sb.AppendLine($"{metadata.MapKeyTypeFullName}.Write(writer, kvp.Key);");
                    }
                }

                if (metadata.IsMapValueTypeOpenGeneric)
                {
                    EmitGenericWithTypeParamsWriter(sb, metadata.MapValueTypeFullName, $"kvp.Value");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapValueTypeFullName))
                    {
                        EmitPrimitivePropertyWriter(sb, metadata.MapValueTypeFullName, $"kvp.Value");
                    }
                    else
                    {
                        sb.AppendLine($"{metadata.MapValueTypeFullName}.Write(writer, kvp.Value);");
                    }
                }

                sb.AppendLine("}");
                sb.AppendLine($"writer.WriteEndMap();");
                return sb;
            }

            if (metadata.IsOpenGeneric)
            {
                EmitGenericWithTypeParamsWriter(sb, metadata.PropertyTypeFullName, propertyName);
            }
            else
            {
                sb.AppendLine($"{metadata.PropertyTypeFullName}.Write(writer, {propertyName});");
            }

            return sb;
        }

        public static StringBuilder EmitTagWriter(StringBuilder sb, int? tag)
        {
            if (tag.HasValue && tag.Value >= 0)
            {
                sb.AppendLine($"writer.WriteTag((CborTag){tag});");
            }

            return sb;
        }

        public static StringBuilder EmitSerializableTypeValidatorWriter(StringBuilder sb, SerializableTypeMetadata metadata, string propertyName)
        {
            if (metadata.Validator is not null)
            {
                sb.AppendLine($"{metadata.Validator} validator = new();");
                sb.AppendLine($"if (!validator.Validate({propertyName})) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitGenericWithTypeParamsWriter(StringBuilder sb, string type, string propertyName)
        {
            sb.AppendLine($"{GenericSerializationUtilFullname}.Write<{type}>(writer, {propertyName});");
            return sb;
        }

        public static StringBuilder EmitGenericWriter(StringBuilder sb, string type, string propertyName)
        {
            sb.AppendLine($"{GenericSerializationUtilFullname}.Write(writer, {type});");
            return sb;
        }

        public static StringBuilder EmitPrimitiveOrObjectWriter(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            if (metadata.IsOpenGeneric)
            {
                EmitGenericWithTypeParamsWriter(sb, metadata.PropertyType, propertyName);
            }
            else
            {
                if (IsPrimitiveType(metadata.PropertyType))
                {
                    EmitPrimitivePropertyWriter(sb, metadata.PropertyType, propertyName, metadata.Size);
                }
                else
                {
                    EmitObjectPropertyWriter(sb, metadata, propertyName);
                }
            }

            return sb;
        }

        public static StringBuilder EmitCustomListWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            EmitPropertyCountWriter(sb, metadata);
            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                if (metadata.IsIndefinite)
                {
                    sb.AppendLine($"writer.WriteStartArray(null);");
                }
                else
                {
                    sb.AppendLine($"writer.WriteStartArray(propCount);");
                }
            }

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                EmitSerializablePropertyWriter(sb, prop);
            }

            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                sb.AppendLine($"writer.WriteEndArray();");
            }

            return sb;
        }

        public static StringBuilder EmitWriterValidation(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Validator is not null)
            {
                sb.AppendLine($"{metadata.Validator} validator = new();");
                sb.AppendLine($"if (!validator.Validate(data)) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitPreservedRawWriter(StringBuilder sb)
        {
            sb.AppendLine($"if (data.Raw is not null)");
            sb.AppendLine("{");
            sb.AppendLine($"writer.WriteEncodedValue(data.Raw?.ToArray());");
            sb.AppendLine("return;");
            sb.AppendLine("}");
            return sb;
        }

        public static StringBuilder EmitPropertyCountWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            sb.AppendLine($"int propCount = 0;");

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                if (prop.IsNullable || !prop.IsTypeNullable)
                {
                    sb.AppendLine("propCount++;");
                }
                else
                {
                    sb.AppendLine($"if (data.{prop.PropertyName} is not null) propCount++;");
                }
            }

            sb.AppendLine();

            return sb;
        }
    }
}