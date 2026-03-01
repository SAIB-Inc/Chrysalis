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

        public static StringBuilder EmitSerializablePropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, bool isList = false, bool trackFields = false, int fieldIndex = -1)
        {
            _ = sb.AppendLine($"{metadata.PropertyTypeFullName} {propertyName} = default{(metadata.PropertyType.Contains("?") ? "" : "!")};");

            // Special handling for CborLabel's Value property
            if (metadata.PropertyName == "Value" && metadata.PropertyType == "object")
            {
                _ = sb.AppendLine($"{propertyName} = reader.PeekState() switch");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("    CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => (object)reader.ReadInt64(),");
                _ = sb.AppendLine("    CborReaderState.TextString => (object)reader.ReadTextString(),");
                _ = sb.AppendLine($"    _ => throw new InvalidOperationException($\"Invalid CBOR type for Label: {{reader.PeekState()}}\")");
                _ = sb.AppendLine("};");
                return sb;
            }

            if (isList)
            {
                _ = sb.AppendLine($"if (reader.PeekState() != CborReaderState.EndArray)");
                _ = sb.AppendLine("{");

                // Track that this field was read for validation - only if we actually declared the array
                if (trackFields && fieldIndex >= 0)
                {
                    _ = sb.AppendLine($"fieldsRead[{fieldIndex}] = true;");
                }
            }

            if (metadata.IsNullable)
            {
                // ReadOnlyMemory<byte> is a value type; use default instead of null
                // when the C# type is non-nullable but the CBOR encoding can be null.
                bool isValueTypeMemory = !metadata.IsTypeNullable && IsReadOnlyMemoryByteType(metadata.PropertyTypeFullName);
                string nullAssignment = isValueTypeMemory ? "default" : "null";
                _ = sb.AppendLine($"if (reader.PeekState() == CborReaderState.Null)");
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

            if (isList)
            {
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
                    _ = sb.AppendLine($"{propertyName} = reader.ReadTextString();");
                    break;
                case "byte[]?":
                case "byte[]":
                    _ = sb.AppendLine($"if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("     reader.ReadStartIndefiniteLengthByteString();");
                    _ = sb.AppendLine("     using (var stream = new MemoryStream())");
                    _ = sb.AppendLine("     {");
                    _ = sb.AppendLine("         while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                    _ = sb.AppendLine("         {");
                    _ = sb.AppendLine("             byte[] chunk = reader.ReadByteString();");
                    _ = sb.AppendLine("             stream.Write(chunk, 0, chunk.Length);");
                    _ = sb.AppendLine("         }");
                    _ = sb.AppendLine("         reader.ReadEndIndefiniteLengthByteString();");
                    _ = sb.AppendLine($"         {propertyName} = stream.ToArray();");
                    _ = sb.AppendLine("     }");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    {propertyName} = reader.ReadByteString();");
                    _ = sb.AppendLine("}");

                    break;
                case "ReadOnlyMemory<byte>?":
                case "ReadOnlyMemory<byte>":
                case "System.ReadOnlyMemory<byte>?":
                case "System.ReadOnlyMemory<byte>":
                case "global::System.ReadOnlyMemory<byte>?":
                case "global::System.ReadOnlyMemory<byte>":
                    _ = sb.AppendLine($"if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("     reader.ReadStartIndefiniteLengthByteString();");
                    _ = sb.AppendLine("     using (var stream = new MemoryStream())");
                    _ = sb.AppendLine("     {");
                    _ = sb.AppendLine("         while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                    _ = sb.AppendLine("         {");
                    _ = sb.AppendLine("             byte[] chunk = reader.ReadByteString();");
                    _ = sb.AppendLine("             stream.Write(chunk, 0, chunk.Length);");
                    _ = sb.AppendLine("         }");
                    _ = sb.AppendLine("         reader.ReadEndIndefiniteLengthByteString();");
                    _ = sb.AppendLine($"         {propertyName} = (ReadOnlyMemory<byte>)stream.ToArray();");
                    _ = sb.AppendLine("     }");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    {propertyName} = (ReadOnlyMemory<byte>)reader.ReadByteString();");
                    _ = sb.AppendLine("}");

                    break;
                case "CborEncodedValue":
                case "Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                case "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                    _ = sb.AppendLine($"{propertyName} = new {CborEncodeValueFullName}(reader.ReadEncodedValue(true));");
                    break;
                case "CborLabel":
                case "Chrysalis.Cbor.Types.CborLabel":
                case "global::Chrysalis.Cbor.Types.CborLabel":
                    _ = sb.AppendLine($"{propertyName} = reader.PeekState() switch");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => new Chrysalis.Cbor.Types.CborLabel(reader.ReadInt64()),");
                    _ = sb.AppendLine("    CborReaderState.TextString => new Chrysalis.Cbor.Types.CborLabel(reader.ReadTextString()),");
                    _ = sb.AppendLine($"    _ => throw new InvalidOperationException($\"Invalid CBOR type for Label: {{reader.PeekState()}}\")");
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
                // Read array start and check if it's indefinite
                _ = sb.AppendLine($"int? {propertyName}ArrayLength = reader.ReadStartArray();");
                _ = sb.AppendLine($"bool {propertyName}IsIndefinite = !{propertyName}ArrayLength.HasValue;");

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
                // If no attribute, accept both definite and indefinite (backward compatibility)
                _ = sb.AppendLine($"while (reader.PeekState() != CborReaderState.EndArray)");
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
                        // @TODO: Handle nested lists/maps, right now we are assuming that the list item type is a class
                        _ = sb.AppendLine($"{propertyName}TempItem = ({metadata.ListItemTypeFullName}){metadata.ListItemTypeFullName}.Read(reader.ReadEncodedValue(true));");
                    }
                }

                _ = sb.AppendLine($"{propertyName}TempList.Add({propertyName}TempItem);");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"reader.ReadEndArray();");
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
                // Read map start and check if it's indefinite
                _ = sb.AppendLine($"int? {propertyName}MapLength = reader.ReadStartMap();");
                _ = sb.AppendLine($"bool {propertyName}MapIsIndefinite = !{propertyName}MapLength.HasValue;");

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
                // If no attribute, accept both definite and indefinite (backward compatibility)
                _ = sb.AppendLine($"while (reader.PeekState() != CborReaderState.EndMap)");
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
                        // @TODO: Handle nested lists/maps, right now we are assuming that the map key item type is a class
                        _ = sb.AppendLine($"{propertyName}TempKeyItem = ({metadata.MapKeyTypeFullName}){metadata.MapKeyTypeFullName}.Read(reader.ReadEncodedValue(true));");
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
                        // @TODO: Handle nested lists/maps, right now we are assuming that the map value item type is a class
                        _ = sb.AppendLine($"{propertyName}TempValueItem = ({metadata.MapValueTypeFullName}){metadata.MapValueTypeFullName}.Read(reader.ReadEncodedValue(true));");
                    }
                }

                _ = sb.AppendLine($"if (!{propertyName}TempMap.ContainsKey({propertyName}TempKeyItem))");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"{propertyName}TempMap.Add({propertyName}TempKeyItem, {propertyName}TempValueItem);");
                _ = sb.AppendLine("}");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"reader.ReadEndMap();");
                _ = sb.AppendLine($"{propertyName} = {propertyName}TempMap;");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    Chrysalis.Cbor.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
                _ = sb.AppendLine("}");

                return sb;
            }

            _ = metadata.IsOpenGeneric
                ? EmitGenericWithTypeParamsReader(sb, metadata.PropertyTypeFullName, propertyName)
                : metadata.UnionHints.Count > 0 && metadata.UnionHintDiscriminantProperty is not null
                    ? EmitUnionHintReader(sb, metadata, propertyName)
                    : sb.AppendLine($"{propertyName} = ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName}.Read(reader.ReadEncodedValue(true));");
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

            _ = sb.AppendLine($"var {propertyName}EncodedValue = reader.ReadEncodedValue(true);");
            _ = sb.AppendLine($"{propertyName} = {discriminantVar} switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, string> hint in metadata.UnionHints)
            {
                _ = sb.AppendLine($"    {hint.Key} => ({metadata.PropertyTypeFullName}){hint.Value}.Read({propertyName}EncodedValue),");
            }
            _ = sb.AppendLine($"    _ => ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName}.Read({propertyName}EncodedValue)");
            _ = sb.AppendLine("};");
            return sb;
        }

        public static StringBuilder EmitCborReaderInstance(StringBuilder sb, string dataName)
        {
            _ = sb.AppendLine($"var reader = new CborReader({dataName}, CborConformanceMode.Lax);");
            return sb;
        }

        public static StringBuilder EmitTagReader(StringBuilder sb, int? tag, string propertyName)
        {
            if (tag.HasValue)
            {
                _ = sb.AppendLine($"var {propertyName} = (int)reader.ReadTag();");
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
            _ = sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read<{type}>(reader);");
            return sb;
        }

        public static StringBuilder EmitGenericReader(StringBuilder sb, string type, string propertyName)
        {
            _ = sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read(reader, {type});");
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
                // Read the array and check if it's indefinite
                _ = sb.AppendLine("int? arrayLength = reader.ReadStartArray();");
                _ = sb.AppendLine("bool isIndefiniteArray = !arrayLength.HasValue;");
                detectIndefinite = true;

                // Validate encoding based on type-level attributes
                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine("if (arrayLength.HasValue)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Type '{metadata.FullyQualifiedName}' requires indefinite CBOR array encoding due to [CborIndefinite] attribute\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine("if (!arrayLength.HasValue)");
                    _ = sb.AppendLine($"    throw new InvalidOperationException(\"Type '{metadata.FullyQualifiedName}' requires definite CBOR array encoding due to [CborDefinite] attribute\");");
                }
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
                _ = EmitSerializablePropertyReader(sb, prop, propName, true, shouldTrackFields, fieldIndex);
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

            if (metadata.SerializationType == SerializationType.Constr && metadata.CborIndex is not null && metadata.CborIndex >= 0)
            {
                _ = sb.AppendLine("reader.ReadEndArray();");
            }
            else if (metadata.SerializationType == SerializationType.List)
            {
                _ = sb.AppendLine("reader.ReadEndArray();");
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