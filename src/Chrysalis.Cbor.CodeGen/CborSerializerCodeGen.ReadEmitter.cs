using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static partial class Emitter
    {
        public static StringBuilder EmitSerializableTypeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            string xmlSafeId = metadata.Indentifier.Replace("<", "{").Replace(">", "}");
            _ = sb.AppendLine($"/// <summary>");
            _ = sb.AppendLine($"/// Deserializes a <see cref=\"{xmlSafeId}\"/> instance from CBOR-encoded bytes.");
            _ = sb.AppendLine($"/// </summary>");
            _ = sb.AppendLine($"/// <param name=\"data\">The CBOR-encoded bytes to deserialize.</param>");
            _ = sb.AppendLine($"/// <returns>A deserialized <see cref=\"{xmlSafeId}\"/> instance.</returns>");
            _ = sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data)");
            _ = sb.AppendLine("{");
            ICborSerializerEmitter emitter = GetEmitter(metadata);
            _ = emitter.EmitReader(sb, metadata);
            _ = sb.AppendLine("}");

            return sb;
        }

        public static StringBuilder EmitSerializablePropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, bool isList = false, bool trackFields = false, int fieldIndex = -1, string? parentRemainingVar = null)
        {
            _ = sb.AppendLine($"{metadata.PropertyTypeFullName} {propertyName} = default{(metadata.PropertyType.Contains("?") ? "" : "!")};");

            // Special handling for CborLabel's Value property
            if (metadata.PropertyName == "Value" && metadata.PropertyType == "object")
            {
                _ = sb.AppendLine($"{propertyName} = reader.GetCurrentDataItemType() switch");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("    CborDataItemType.Unsigned or CborDataItemType.Signed => (object)reader.ReadInt64(),");
                _ = sb.AppendLine("    CborDataItemType.String => (object)reader.ReadString(),");
                _ = sb.AppendLine($"    _ => throw new InvalidOperationException($\"Invalid CBOR type for Label: {{reader.GetCurrentDataItemType()}}\")");
                _ = sb.AppendLine("};");
                if (isList && parentRemainingVar is not null)
                {
                    _ = sb.AppendLine($"if ({parentRemainingVar} > 0) {parentRemainingVar}--;");
                }
                return sb;
            }

            if (isList && parentRemainingVar is not null)
            {
                _ = sb.AppendLine($"if ({parentRemainingVar} != 0)");
                _ = sb.AppendLine("{");

                // Track that this field was read for validation - only if we actually declared the array
                if (trackFields && fieldIndex >= 0)
                {
                    _ = sb.AppendLine($"fieldsRead[{fieldIndex}] = true;");
                }
            }

            if (metadata.IsNullable)
            {
                bool isValueTypeMemory = !metadata.IsTypeNullable && IsReadOnlyMemoryByteType(metadata.PropertyTypeFullName);
                string nullAssignment = isValueTypeMemory ? "default" : "null";
                _ = sb.AppendLine($"if (reader.GetCurrentDataItemType() == CborDataItemType.Null)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"reader.ReadNull();");
                _ = sb.AppendLine($"{propertyName} = {nullAssignment};");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"else");
                _ = sb.AppendLine("{");
            }

            _ = EmitPrimitiveOrObjectReader(sb, metadata, propertyName);

            if (metadata.IsNullable)
            {
                _ = sb.AppendLine("}");
            }

            if (isList && parentRemainingVar is not null)
            {
                _ = sb.AppendLine($"if ({parentRemainingVar} > 0) {parentRemainingVar}--;");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine();

            return sb;
        }

        public static StringBuilder EmitPrimitivePropertyReader(StringBuilder sb, string type, string propertyName)
        {
            type = type.Replace("?", "");
            switch (type)
            {
                case "bool":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadBoolean();");
                    break;
                case "int":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadInt32();");
                    break;
                case "uint":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadUInt32();");
                    break;
                case "long":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadInt64();");
                    break;
                case "ulong":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadUInt64();");
                    break;
                case "float":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadSingle();");
                    break;
                case "double":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadDouble();");
                    break;
                case "decimal":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadDecimal();");
                    break;
                case "string":
                    _ = sb.AppendLine($"{propertyName} = reader.ReadString();");
                    break;
                case "byte[]?":
                case "byte[]":
                    // Indefinite byte string: peek first byte for 0x5F
                    _ = sb.AppendLine($"if (reader.Buffer.Length > 0 && reader.Buffer[0] == 0x5F)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("     reader.ReadDataItem(); // skip 0x5F header");
                    _ = sb.AppendLine("     using (var stream = new MemoryStream())");
                    _ = sb.AppendLine("     {");
                    _ = sb.AppendLine("         while (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF)");
                    _ = sb.AppendLine("         {");
                    _ = sb.AppendLine("             var chunk = reader.ReadByteString();");
                    _ = sb.AppendLine("             stream.Write(chunk);");
                    _ = sb.AppendLine("         }");
                    _ = sb.AppendLine("         reader.ReadDataItem(); // skip 0xFF break");
                    _ = sb.AppendLine($"         {propertyName} = stream.ToArray();");
                    _ = sb.AppendLine("     }");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    {propertyName} = reader.ReadByteString().ToArray();");
                    _ = sb.AppendLine("}");

                    break;
                case "ReadOnlyMemory<byte>?":
                case "ReadOnlyMemory<byte>":
                case "System.ReadOnlyMemory<byte>?":
                case "System.ReadOnlyMemory<byte>":
                case "global::System.ReadOnlyMemory<byte>?":
                case "global::System.ReadOnlyMemory<byte>":
                    // Indefinite byte string: peek first byte for 0x5F
                    _ = sb.AppendLine($"if (reader.Buffer.Length > 0 && reader.Buffer[0] == 0x5F)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("     reader.ReadDataItem(); // skip 0x5F header");
                    _ = sb.AppendLine("     using (var stream = new MemoryStream())");
                    _ = sb.AppendLine("     {");
                    _ = sb.AppendLine("         while (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF)");
                    _ = sb.AppendLine("         {");
                    _ = sb.AppendLine("             var chunk = reader.ReadByteString();");
                    _ = sb.AppendLine("             stream.Write(chunk);");
                    _ = sb.AppendLine("         }");
                    _ = sb.AppendLine("         reader.ReadDataItem(); // skip 0xFF break");
                    _ = sb.AppendLine($"         {propertyName} = (ReadOnlyMemory<byte>)stream.ToArray();");
                    _ = sb.AppendLine("     }");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    {propertyName} = (ReadOnlyMemory<byte>)reader.ReadByteString().ToArray();");
                    _ = sb.AppendLine("}");

                    break;
                case "CborEncodedValue":
                case "Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                case "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                    // Zero-copy: track position, read item, slice from original data
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
                    _ = sb.AppendLine($"{propertyName} = new {CborEncodeValueFullName}(data.Slice(_pos, _span.Length));");
                    _ = sb.AppendLine("}");
                    break;
                case "CborLabel":
                case "Chrysalis.Cbor.Types.CborLabel":
                case "global::Chrysalis.Cbor.Types.CborLabel":
                    _ = sb.AppendLine($"{propertyName} = reader.GetCurrentDataItemType() switch");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    CborDataItemType.Unsigned or CborDataItemType.Signed => new Chrysalis.Cbor.Types.CborLabel(reader.ReadInt64()),");
                    _ = sb.AppendLine("    CborDataItemType.String => new Chrysalis.Cbor.Types.CborLabel(reader.ReadString()),");
                    _ = sb.AppendLine($"    _ => throw new InvalidOperationException($\"Invalid CBOR type for Label: {{reader.GetCurrentDataItemType()}}\")");
                    _ = sb.AppendLine("};");
                    break;
                default:
                    break;
            }

            return sb;
        }

        public static StringBuilder EmitObjectPropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            if (metadata.IsList)
            {
                if (metadata.ListItemTypeFullName is null || metadata.ListItemType is null)
                {
                    throw new InvalidOperationException($"List item type is null for property {metadata.PropertyName}");
                }

                _ = sb.AppendLine($"{metadata.ListItemTypeFullName} {propertyName}TempItem = default;");
                _ = sb.AppendLine($"List<{metadata.ListItemTypeFullName}> {propertyName}TempList = new();");
                // Read array start and size
                _ = sb.AppendLine($"reader.ReadBeginArray();");
                _ = sb.AppendLine($"int {propertyName}ArraySize = reader.ReadSize();");
                _ = sb.AppendLine($"bool {propertyName}IsIndefinite = {propertyName}ArraySize == -1;");

                // Validate encoding based on attributes
                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine($"if (!{propertyName}IsIndefinite)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Property '{metadata.PropertyName}' requires indefinite CBOR array encoding due to [CborIndefinite] attribute\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine($"if ({propertyName}IsIndefinite)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Property '{metadata.PropertyName}' requires definite CBOR array encoding due to [CborDefinite] attribute\");");
                }
                // Size-based loop: for definite use counter, for indefinite check break byte
                _ = sb.AppendLine($"int {propertyName}ArrayRemaining = {propertyName}ArraySize;");
                _ = sb.AppendLine($"while ({propertyName}IsIndefinite ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : {propertyName}ArrayRemaining > 0)");
                _ = sb.AppendLine("{");

                if (metadata.IsListItemTypeOpenGeneric)
                {
                    _ = EmitGenericWithTypeParamsReader(sb, metadata.ListItemType, $"{propertyName}TempItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.ListItemTypeFullName))
                    {
                        _ = EmitPrimitivePropertyReader(sb, metadata.ListItemTypeFullName, $"{propertyName}TempItem");
                    }
                    else
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
                        _ = sb.AppendLine($"{propertyName}TempItem = ({metadata.ListItemTypeFullName}){metadata.ListItemTypeFullName}.Read(data.Slice(_pos, _span.Length));");
                        _ = sb.AppendLine("}");
                    }
                }

                _ = sb.AppendLine($"{propertyName}TempList.Add({propertyName}TempItem);");
                _ = sb.AppendLine($"if ({propertyName}ArraySize > 0) {propertyName}ArrayRemaining--;");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}IsIndefinite && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();"); // skip break
                _ = sb.AppendLine($"{propertyName} = {propertyName}TempList;");
                _ = sb.AppendLine($"if ({propertyName}IsIndefinite)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    Chrysalis.Cbor.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
                _ = sb.AppendLine("}");

                return sb;
            }

            if (metadata.IsMap)
            {
                if (metadata.MapKeyTypeFullName is null || metadata.MapValueTypeFullName is null)
                {
                    throw new InvalidOperationException($"Map key or value type is null for property {metadata.PropertyName}");
                }

                _ = IsReadOnlyMemoryByteType(metadata.MapKeyTypeFullName)
                    ? sb.AppendLine($"Dictionary<{metadata.MapKeyTypeFullName}, {metadata.MapValueTypeFullName}> {propertyName}TempMap = new(global::Chrysalis.Cbor.Serialization.Utils.ReadOnlyMemoryComparer.Instance);")
                    : sb.AppendLine($"Dictionary<{metadata.MapKeyTypeFullName}, {metadata.MapValueTypeFullName}> {propertyName}TempMap = new();");
                _ = sb.AppendLine($"{metadata.MapKeyTypeFullName} {propertyName}TempKeyItem = default;");
                _ = sb.AppendLine($"{metadata.MapValueTypeFullName} {propertyName}TempValueItem = default;");
                // Read map start and size
                _ = sb.AppendLine($"reader.ReadBeginMap();");
                _ = sb.AppendLine($"int {propertyName}MapSize = reader.ReadSize();");
                _ = sb.AppendLine($"bool {propertyName}MapIsIndefinite = {propertyName}MapSize == -1;");

                // Validate encoding based on attributes
                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine($"if (!{propertyName}MapIsIndefinite)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Property '{metadata.PropertyName}' requires indefinite CBOR map encoding due to [CborIndefinite] attribute\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Property '{metadata.PropertyName}' requires definite CBOR map encoding due to [CborDefinite] attribute\");");
                }
                // Size-based loop
                _ = sb.AppendLine($"int {propertyName}MapRemaining = {propertyName}MapSize;");
                _ = sb.AppendLine($"while ({propertyName}MapIsIndefinite ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : {propertyName}MapRemaining > 0)");
                _ = sb.AppendLine("{");

                if (metadata.IsMapKeyTypeOpenGeneric)
                {
                    _ = EmitGenericWithTypeParamsReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapKeyTypeFullName))
                    {
                        _ = EmitPrimitivePropertyReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                    }
                    else
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
                        _ = sb.AppendLine($"{propertyName}TempKeyItem = ({metadata.MapKeyTypeFullName}){metadata.MapKeyTypeFullName}.Read(data.Slice(_pos, _span.Length));");
                        _ = sb.AppendLine("}");
                    }
                }

                if (metadata.IsMapValueTypeOpenGeneric)
                {
                    _ = EmitGenericWithTypeParamsReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapValueTypeFullName))
                    {
                        _ = EmitPrimitivePropertyReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                    }
                    else
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
                        _ = sb.AppendLine($"{propertyName}TempValueItem = ({metadata.MapValueTypeFullName}){metadata.MapValueTypeFullName}.Read(data.Slice(_pos, _span.Length));");
                        _ = sb.AppendLine("}");
                    }
                }

                _ = sb.AppendLine($"if (!{propertyName}TempMap.ContainsKey({propertyName}TempKeyItem))");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"{propertyName}TempMap.Add({propertyName}TempKeyItem, {propertyName}TempValueItem);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}MapSize > 0) {propertyName}MapRemaining--;");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();"); // skip break
                _ = sb.AppendLine($"{propertyName} = {propertyName}TempMap;");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    Chrysalis.Cbor.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
                _ = sb.AppendLine("}");

                return sb;
            }

            if (metadata.IsOpenGeneric)
            {
                _ = EmitGenericWithTypeParamsReader(sb, metadata.PropertyTypeFullName, propertyName);
            }
            else if (metadata.UnionHints.Count > 0 && metadata.UnionHintDiscriminantProperty is not null)
            {
                _ = EmitUnionHintReader(sb, metadata, propertyName);
            }
            else
            {
                // Zero-copy ReadEncodedValue replacement
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
                _ = sb.AppendLine($"{propertyName} = ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName}.Read(data.Slice(_pos, _span.Length));");
                _ = sb.AppendLine("}");
            }
            return sb;
        }

        public static StringBuilder EmitUnionHintReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            // The discriminant variable was already read by a previous property.
            // Its local variable name follows the pattern: {TypeBaseIdentifier}{PropertyName}
            // We can't know the type base identifier here, but the caller's pattern uses
            // the parent type's BaseIdentifier + discriminant property name.
            // We'll use a convention: the discriminant variable is named the same way as
            // other properties in the same containing type's generated code.
            // Since EmitCustomListReader creates variables as {metadata.BaseIdentifier}{prop.PropertyName},
            // we need the parent type's base identifier. We don't have it here, so we reference the
            // discriminant property variable using the same naming convention.
            // The propertyName we receive is already the full variable name (e.g., "BlockWithEraBlock").
            // We need to derive the discriminant variable name by replacing our property name part
            // with the discriminant property name.
            // Example: propertyName = "BlockWithEraBlock", discriminantProp = "EraNumber"
            // discriminant variable = "BlockWithEraEraNumber"

            // Extract the prefix (everything before this property's name in the variable)
            string discriminantProp = metadata.UnionHintDiscriminantProperty!;
            string suffix = metadata.PropertyName;
            string prefix = propertyName.EndsWith(suffix, StringComparison.Ordinal)
                ? propertyName.Substring(0, propertyName.Length - suffix.Length)
                : propertyName;
            string discriminantVar = $"{prefix}{discriminantProp}";

            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
            _ = sb.AppendLine($"var {propertyName}EncodedValue = data.Slice(_pos, _span.Length);");
            _ = sb.AppendLine($"{propertyName} = {discriminantVar} switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, string> hint in metadata.UnionHints)
            {
                _ = sb.AppendLine($"    {hint.Key} => ({metadata.PropertyTypeFullName}){hint.Value}.Read({propertyName}EncodedValue),");
            }
            _ = sb.AppendLine($"    _ => ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName}.Read({propertyName}EncodedValue)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine("}");
            return sb;
        }

        public static StringBuilder EmitCborReaderInstance(StringBuilder sb, string dataName)
        {
            _ = sb.AppendLine($"var reader = new CborReader({dataName}.Span);");
            return sb;
        }

        public static StringBuilder EmitTagReader(StringBuilder sb, int? tag, string propertyName)
        {
            if (tag.HasValue)
            {
                _ = sb.AppendLine($"reader.TryReadSemanticTag(out ulong _{propertyName}Raw);");
                _ = sb.AppendLine($"var {propertyName} = (int)_{propertyName}Raw;");
                if (tag.Value >= 0)
                {
                    _ = sb.AppendLine($"if ({propertyName} != {tag}) throw new Exception(\"Invalid tag\");");
                }
            }

            return sb;
        }

        public static StringBuilder EmitSerializableTypeValidatorReader(StringBuilder sb, SerializableTypeMetadata metadata, string propertyName)
        {
            if (metadata.Validator is not null)
            {
                _ = sb.AppendLine($"{metadata.Validator} validator = new();");
                _ = sb.AppendLine($"if (!validator.Validate({propertyName})) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitGenericWithTypeParamsReader(StringBuilder sb, string type, string propertyName)
        {
            // Extract encoded value from current reader position, then dispatch via ReadOnlyMemory<byte>
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
            _ = sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read<{type}>(data.Slice(_pos, _span.Length));");
            _ = sb.AppendLine("}");
            return sb;
        }

        public static StringBuilder EmitGenericReader(StringBuilder sb, string type, string propertyName)
        {
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
            _ = sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read(data.Slice(_pos, _span.Length), {type});");
            _ = sb.AppendLine("}");
            return sb;
        }

        public static StringBuilder EmitPrimitiveOrObjectReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            string cleanPropertyType = metadata.PropertyType.Replace("?", "");
            _ = metadata.IsOpenGeneric
                ? EmitGenericWithTypeParamsReader(sb, metadata.PropertyType, propertyName)
                : IsPrimitiveType(cleanPropertyType)
                    ? EmitPrimitivePropertyReader(sb, metadata.PropertyType, propertyName)
                    : EmitObjectPropertyReader(sb, metadata, propertyName);

            return sb;
        }

        public static StringBuilder EmitCustomListReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Dictionary<string, string> propMapping = [];
            bool isListSerialization = metadata.SerializationType == SerializationType.List;
            bool detectIndefinite = false;

            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                // Read the array and size using Dahomey API
                _ = sb.AppendLine("reader.ReadBeginArray();");
                _ = sb.AppendLine("int arraySize = reader.ReadSize();");
                _ = sb.AppendLine("bool isIndefiniteArray = arraySize == -1;");
                // Track remaining for size-based termination
                _ = sb.AppendLine($"int {metadata.BaseIdentifier}ArrayRemaining = arraySize;");
                detectIndefinite = true;

                // Validate encoding based on type-level attributes
                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine("if (!isIndefiniteArray)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Type '{metadata.FullyQualifiedName}' requires indefinite CBOR array encoding due to [CborIndefinite] attribute\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine("if (isIndefiniteArray)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Type '{metadata.FullyQualifiedName}' requires definite CBOR array encoding due to [CborDefinite] attribute\");");
                }
            }

            if (!detectIndefinite)
            {
                // No array wrapper — declare a dummy remaining counter for property readers
                _ = sb.AppendLine($"int {metadata.BaseIdentifier}ArrayRemaining = {metadata.Properties.Count};");
            }

            // Add field tracking for List serialization to validate required fields
            bool shouldTrackFields = isListSerialization && metadata.Properties.Any(p => p.IsRequired);
            if (shouldTrackFields)
            {
                _ = sb.AppendLine($"bool[] fieldsRead = new bool[{metadata.Properties.Count}];");
            }

            int fieldIndex = 0;
            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
                propMapping.Add(prop.PropertyName, propName);
                _ = EmitSerializablePropertyReader(sb, prop, propName, true, shouldTrackFields, fieldIndex, $"{metadata.BaseIdentifier}ArrayRemaining");
                fieldIndex++;
            }

            // Add validation for required fields in List serialization
            if (shouldTrackFields)
            {
                List<SerializablePropertyMetadata> requiredProps = [.. metadata.Properties.Where(p => p.IsRequired)];
                foreach (SerializablePropertyMetadata requiredProp in requiredProps)
                {
                    int propIndex = metadata.Properties.ToList().IndexOf(requiredProp);
                    _ = sb.AppendLine($"if (!fieldsRead[{propIndex}])");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    throw new System.Exception(\"Required field '{requiredProp.PropertyName}' is missing from CBOR data\");");
                    _ = sb.AppendLine("}");
                }
            }

            // Skip break byte for indefinite arrays
            if (metadata.SerializationType == SerializationType.Constr && metadata.CborIndex is not null && metadata.CborIndex >= 0)
            {
                _ = sb.AppendLine("if (isIndefiniteArray && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
            }
            else if (metadata.SerializationType == SerializationType.List)
            {
                _ = sb.AppendLine("if (isIndefiniteArray && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
            }

            _ = sb.AppendLine($"{metadata.FullyQualifiedName} result;");

            if (metadata.Properties.Count == 0)
            {
                _ = sb.AppendLine($"result = new {metadata.FullyQualifiedName}();");
            }
            else
            {
                _ = sb.AppendLine($"result = new {metadata.FullyQualifiedName}(");
                IOrderedEnumerable<SerializablePropertyMetadata> properties = metadata.Properties.OrderBy(p => p.Order);
                IEnumerable<string> propStrings = properties.Select(prop => propMapping[prop.PropertyName]);
                _ = sb.AppendLine(string.Join(",\n", propStrings));
                _ = sb.AppendLine(");");
            }

            // Set the IsIndefinite flag if we detected it
            if (detectIndefinite)
            {
                _ = sb.AppendLine("if (isIndefiniteArray)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    result.IsIndefinite = true;");
                _ = sb.AppendLine("}");
            }


            _ = EmitReaderValidationAndResult(sb, metadata, "result");

            return sb;
        }

        public static StringBuilder EmitReaderValidationAndResult(StringBuilder sb, SerializableTypeMetadata metadata, string resultName)
        {
            _ = EmitSerializableTypeValidatorReader(sb, metadata, resultName);

            if (metadata.SerializationType == SerializationType.Constr)
            {
                _ = sb.AppendLine($"{resultName}.ConstrIndex = constrIndex;");
            }

            if (metadata.ShouldPreserveRaw)
            {
                _ = sb.AppendLine($"{resultName}.Raw = data;");
            }

            _ = sb.AppendLine($"return {resultName};");
            return sb;
        }
    }
}