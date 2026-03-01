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
            _ = sb.AppendLine($"/// <param name=\"output\">The output buffer to serialize to.</param>");
            _ = sb.AppendLine($"/// <param name=\"data\">The <see cref=\"{xmlSafeId}\"/> instance to serialize.</param>");
            _ = sb.AppendLine($"public static new void Write(IBufferWriter<byte> output, {metadata.FullyQualifiedName} data)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"var writer = new CborWriter(output);");
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
                _ = sb.AppendLine("        writer.WriteString(s);");
                _ = sb.AppendLine("        break;");
                _ = sb.AppendLine("    default:");
                _ = sb.AppendLine($"        throw new InvalidOperationException($\"CborLabel value must be int, long, or string. Got: {{{propertyName}?.GetType()}}\");");
                _ = sb.AppendLine("}");
                return sb;
            }

            if (metadata.IsNullable)
            {
                // ReadOnlyMemory<byte> is a value type and cannot be null;
                // use Length == 0 as the sentinel for CBOR null when the C# type is non-nullable.
                bool isValueTypeMemory = !metadata.IsTypeNullable && IsReadOnlyMemoryByteType(metadata.PropertyTypeFullName);
                string nullCheck = isValueTypeMemory
                    ? $"{propertyName}.Length == 0"
                    : $"{propertyName} is null";
                _ = sb.AppendLine($"if ({nullCheck})");
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
                    _ = sb.AppendLine($"writer.WriteString({propertyName});");
                    break;
                case "byte[]?":
                case "byte[]":
                    if (size is not null)
                    {
                        _ = sb.AppendLine($"if ({propertyName}.Length > {size})");
                        _ = sb.AppendLine("{");
                        // Manual indefinite byte string: write 0x5F, chunks, 0xFF
                        _ = sb.AppendLine($"output.GetSpan(1)[0] = 0x5F; output.Advance(1);");
                        _ = sb.AppendLine($"var {propertyName.Replace(".", "")}Chunks = {propertyName}.Chunk({size});");
                        _ = sb.AppendLine($"foreach (var chunk in {propertyName.Replace(".", "")}Chunks)");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"writer.WriteByteString(chunk);");
                        _ = sb.AppendLine("}");
                        _ = sb.AppendLine($"output.GetSpan(1)[0] = 0xFF; output.Advance(1);");
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
                case "ReadOnlyMemory<byte>?":
                case "ReadOnlyMemory<byte>":
                case "System.ReadOnlyMemory<byte>?":
                case "System.ReadOnlyMemory<byte>":
                case "global::System.ReadOnlyMemory<byte>?":
                case "global::System.ReadOnlyMemory<byte>":
                    string spanAccess = isNullable ? $"{propertyName}.Value.Span" : $"{propertyName}.Span";
                    string lengthAccess = isNullable ? $"{propertyName}.Value.Length" : $"{propertyName}.Length";
                    if (size is not null)
                    {
                        _ = sb.AppendLine($"if ({lengthAccess} > {size})");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"output.GetSpan(1)[0] = 0x5F; output.Advance(1);");
                        _ = sb.AppendLine($"for (int _i = 0; _i < {lengthAccess}; _i += {size})");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"    int _len = Math.Min({size}, {lengthAccess} - _i);");
                        _ = sb.AppendLine($"    writer.WriteByteString({spanAccess}.Slice(_i, _len));");
                        _ = sb.AppendLine("}");
                        _ = sb.AppendLine($"output.GetSpan(1)[0] = 0xFF; output.Advance(1);");
                        _ = sb.AppendLine("}");
                        _ = sb.AppendLine("else");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"writer.WriteByteString({spanAccess});");
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine($"writer.WriteByteString({spanAccess});");
                    }

                    break;
                case "CborEncodedValue":
                case "Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                case "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                    _ = sb.AppendLine($"writer.WriteSemanticTag(24);");
                    _ = sb.AppendLine($"writer.WriteByteString({propertyName}.Value.Span);");
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
                    _ = sb.AppendLine("        writer.WriteString(s);");
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
                _ = sb.AppendLine($"int {metadata.PropertyName}ArraySize;");
                _ = sb.AppendLine($"if (useIndefiniteFor{metadata.PropertyName})");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    {metadata.PropertyName}ArraySize = -1;");
                _ = sb.AppendLine($"    writer.WriteBeginArray(-1);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("else");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    {metadata.PropertyName}ArraySize = {propertyName}.Count();");
                _ = sb.AppendLine($"    writer.WriteBeginArray({metadata.PropertyName}ArraySize);");
                _ = sb.AppendLine("}");

                _ = sb.AppendLine($"foreach (var item in {propertyName})");
                _ = sb.AppendLine("{");
                _ = metadata.IsListItemTypeOpenGeneric
                    ? EmitGenericWithTypeParamsWriter(sb, metadata.ListItemType, $"item")
                    : IsPrimitiveType(metadata.ListItemTypeFullName)
                        ? EmitPrimitivePropertyWriter(sb, metadata.ListItemTypeFullName, "item")
                        : sb.AppendLine($"{metadata.ListItemTypeFullName}.Write(output, ({metadata.ListItemTypeFullName})item);");


                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"writer.WriteEndArray({metadata.PropertyName}ArraySize);");

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
                _ = sb.AppendLine($"int {metadata.PropertyName}MapSize;");
                _ = sb.AppendLine($"if (useIndefiniteMapFor{metadata.PropertyName})");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    {metadata.PropertyName}MapSize = -1;");
                _ = sb.AppendLine($"    writer.WriteBeginMap(-1);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("else");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    {metadata.PropertyName}MapSize = {propertyName}.Count();");
                _ = sb.AppendLine($"    writer.WriteBeginMap({metadata.PropertyName}MapSize);");
                _ = sb.AppendLine("}");

                _ = sb.AppendLine($"foreach (var kvp in {propertyName})");
                _ = sb.AppendLine("{");

                _ = metadata.IsMapKeyTypeOpenGeneric
                    ? EmitGenericWithTypeParamsWriter(sb, metadata.MapKeyTypeFullName, $"kvp.Key")
                    : IsPrimitiveType(metadata.MapKeyTypeFullName)
                        ? EmitPrimitivePropertyWriter(sb, metadata.MapKeyTypeFullName, $"kvp.Key")
                        : sb.AppendLine($"{metadata.MapKeyTypeFullName}.Write(output, ({metadata.MapKeyTypeFullName})kvp.Key);");

                _ = metadata.IsMapValueTypeOpenGeneric
                    ? EmitGenericWithTypeParamsWriter(sb, metadata.MapValueTypeFullName, $"kvp.Value")
                    : IsPrimitiveType(metadata.MapValueTypeFullName)
                        ? EmitPrimitivePropertyWriter(sb, metadata.MapValueTypeFullName, $"kvp.Value")
                        : sb.AppendLine($"{metadata.MapValueTypeFullName}.Write(output, ({metadata.MapValueTypeFullName})kvp.Value);");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"writer.WriteEndMap({metadata.PropertyName}MapSize);");
                return sb;
            }

            _ = metadata.IsOpenGeneric
                ? EmitGenericWithTypeParamsWriter(sb, metadata.PropertyTypeFullName, propertyName)
                : sb.AppendLine($"{metadata.PropertyTypeFullName}.Write(output, ({metadata.PropertyTypeFullName}){propertyName});");

            return sb;
        }

        public static StringBuilder EmitTagWriter(StringBuilder sb, int? tag)
        {
            if (tag.HasValue && tag.Value >= 0)
            {
                _ = sb.AppendLine($"writer.WriteSemanticTag((ulong){tag});");
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
            _ = sb.AppendLine($"{GenericSerializationUtilFullname}.Write<{type}>(output, {propertyName});");
            return sb;
        }

        public static StringBuilder EmitGenericWriter(StringBuilder sb, string type)
        {
            _ = sb.AppendLine($"{GenericSerializationUtilFullname}.Write(output, {type});");
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
                // Track array size for WriteEndArray
                _ = sb.AppendLine("int _arraySize;");
                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine("_arraySize = -1;");
                    _ = sb.AppendLine($"writer.WriteBeginArray(-1);");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine("_arraySize = propCount;");
                    _ = sb.AppendLine($"writer.WriteBeginArray(propCount);");
                }
                else
                {
                    _ = sb.AppendLine($"bool useIndefinite = data.IsIndefinite;");
                    _ = sb.AppendLine("if (useIndefinite)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    _arraySize = -1;");
                    _ = sb.AppendLine("    writer.WriteBeginArray(-1);");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    _arraySize = propCount;");
                    _ = sb.AppendLine("    writer.WriteBeginArray(propCount);");
                    _ = sb.AppendLine("}");
                }
            }

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                _ = EmitSerializablePropertyWriter(sb, prop);
            }

            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                _ = sb.AppendLine($"writer.WriteEndArray(_arraySize);");
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
            _ = sb.AppendLine($"output.Write(data.Raw.Value.Span);");
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