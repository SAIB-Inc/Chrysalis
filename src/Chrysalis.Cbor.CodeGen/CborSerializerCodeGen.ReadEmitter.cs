using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static partial class Emitter
    {
        public static StringBuilder EmitSerializableTypeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data)");
            sb.AppendLine("{");
            ICborSerializerEmitter emitter = GetEmitter(metadata);
            emitter.EmitReader(sb, metadata);
            sb.AppendLine("}");

            return sb;
        }

        public static StringBuilder EmitSerializablePropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, bool isList = false)
        {
            sb.AppendLine($"{metadata.PropertyTypeFullName} {propertyName} = default{(metadata.PropertyType.Contains("?") ? "" : "!")};");
            
            // Special handling for CborLabel's Value property
            if (metadata.PropertyName == "Value" && metadata.PropertyType == "object")
            {
                sb.AppendLine($"{propertyName} = reader.PeekState() switch");
                sb.AppendLine("{");
                sb.AppendLine("    CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => (object)reader.ReadInt64(),");
                sb.AppendLine("    CborReaderState.TextString => (object)reader.ReadTextString(),");
                sb.AppendLine($"    _ => throw new InvalidOperationException($\"Invalid CBOR type for Label: {{reader.PeekState()}}\")");
                sb.AppendLine("};");
                return sb;
            }

            if (isList)
            {
                sb.AppendLine($"if (reader.PeekState() != CborReaderState.EndArray)");
                sb.AppendLine("{");
            }

            if (metadata.IsNullable)
            {
                sb.AppendLine($"if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("{");
                sb.AppendLine($"reader.ReadNull();");
                sb.AppendLine($"{propertyName} = null;");
                sb.AppendLine("}");
                sb.AppendLine($"else");
                sb.AppendLine("{");
            }

            EmitPrimitiveOrObjectReader(sb, metadata, propertyName);

            if (metadata.IsNullable) sb.AppendLine("}");
            if (isList)
            {
                sb.AppendLine("}");
            }

            sb.AppendLine();

            return sb;
        }

        public static StringBuilder EmitPrimitivePropertyReader(StringBuilder sb, string type, string propertyName)
        {
            type = type.Replace("?", "");
            switch (type)
            {
                case "bool":
                    sb.AppendLine($"{propertyName} = reader.ReadBoolean();");
                    break;
                case "int":
                    sb.AppendLine($"{propertyName} = reader.ReadInt32();");
                    break;
                case "uint":
                    sb.AppendLine($"{propertyName} = reader.ReadUInt32();");
                    break;
                case "long":
                    sb.AppendLine($"{propertyName} = reader.ReadInt64();");
                    break;
                case "ulong":
                    sb.AppendLine($"{propertyName} = reader.ReadUInt64();");
                    break;
                case "float":
                    sb.AppendLine($"{propertyName} = reader.ReadSingle();");
                    break;
                case "double":
                    sb.AppendLine($"{propertyName} = reader.ReadDouble();");
                    break;
                case "decimal":
                    sb.AppendLine($"{propertyName} = reader.ReadDecimal();");
                    break;
                case "string":
                    sb.AppendLine($"{propertyName} = reader.ReadTextString();");
                    break;
                case "byte[]?":
                case "byte[]":
                    sb.AppendLine($"if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)");
                    sb.AppendLine("{");
                    sb.AppendLine("     reader.ReadStartIndefiniteLengthByteString();");
                    sb.AppendLine("     using (var stream = new MemoryStream())");
                    sb.AppendLine("     {");
                    sb.AppendLine("         while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                    sb.AppendLine("         {");
                    sb.AppendLine("             byte[] chunk = reader.ReadByteString();");
                    sb.AppendLine("             stream.Write(chunk, 0, chunk.Length);");
                    sb.AppendLine("         }");
                    sb.AppendLine("         reader.ReadEndIndefiniteLengthByteString();");
                    sb.AppendLine($"         {propertyName} = stream.ToArray();");
                    sb.AppendLine("     }");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine($"    {propertyName} = reader.ReadByteString();");
                    sb.AppendLine("}");

                    break;
                case "CborEncodedValue":
                case "Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                case "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue":
                    sb.AppendLine($"{propertyName} = new {CborEncodeValueFullName}(reader.ReadEncodedValue(true).ToArray());");
                    break;
                case "CborLabel":
                case "Chrysalis.Cbor.Types.CborLabel":
                case "global::Chrysalis.Cbor.Types.CborLabel":
                    sb.AppendLine($"{propertyName} = reader.PeekState() switch");
                    sb.AppendLine("{");
                    sb.AppendLine("    CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => new Chrysalis.Cbor.Types.CborLabel(reader.ReadInt64()),");
                    sb.AppendLine("    CborReaderState.TextString => new Chrysalis.Cbor.Types.CborLabel(reader.ReadTextString()),");
                    sb.AppendLine($"    _ => throw new InvalidOperationException($\"Invalid CBOR type for Label: {{reader.PeekState()}}\")");
                    sb.AppendLine("};");
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

                sb.AppendLine($"{metadata.ListItemTypeFullName} {propertyName}TempItem = default;");
                sb.AppendLine($"List<{metadata.ListItemTypeFullName}> {propertyName}TempList = new();");
                sb.AppendLine($"reader.ReadStartArray();");
                sb.AppendLine($"while (reader.PeekState() != CborReaderState.EndArray)");
                sb.AppendLine("{");

                if (metadata.IsListItemTypeOpenGeneric)
                {
                    EmitGenericWithTypeParamsReader(sb, metadata.ListItemType, $"{propertyName}TempItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.ListItemTypeFullName))
                    {
                        EmitPrimitivePropertyReader(sb, metadata.ListItemTypeFullName, $"{propertyName}TempItem");
                    }
                    else
                    {
                        // @TODO: Handle nested lists/maps, right now we are assuming that the list item type is a class
                        sb.AppendLine($"{propertyName}TempItem = ({metadata.ListItemTypeFullName}){metadata.ListItemTypeFullName}.Read(reader.ReadEncodedValue(true));");
                    }
                }

                sb.AppendLine($"{propertyName}TempList.Add({propertyName}TempItem);");

                sb.AppendLine("}");
                sb.AppendLine($"reader.ReadEndArray();");
                sb.AppendLine($"{propertyName} = {propertyName}TempList;");

                return sb;
            }

            if (metadata.IsMap)
            {
                if (metadata.MapKeyTypeFullName is null || metadata.MapValueTypeFullName is null)
                {
                    throw new InvalidOperationException($"Map key or value type is null for property {metadata.PropertyName}");
                }

                sb.AppendLine($"Dictionary<{metadata.MapKeyTypeFullName}, {metadata.MapValueTypeFullName}> {propertyName}TempMap = new();");
                sb.AppendLine($"{metadata.MapKeyTypeFullName} {propertyName}TempKeyItem = default;");
                sb.AppendLine($"{metadata.MapValueTypeFullName} {propertyName}TempValueItem = default;");
                sb.AppendLine($"reader.ReadStartMap();");
                sb.AppendLine($"while (reader.PeekState() != CborReaderState.EndMap)");
                sb.AppendLine("{");

                if (metadata.IsMapKeyTypeOpenGeneric)
                {
                    EmitGenericWithTypeParamsReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapKeyTypeFullName))
                    {
                        EmitPrimitivePropertyReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                    }
                    else
                    {
                        // @TODO: Handle nested lists/maps, right now we are assuming that the map key item type is a class
                        sb.AppendLine($"{propertyName}TempKeyItem = ({metadata.MapKeyTypeFullName}){metadata.MapKeyTypeFullName}.Read(reader.ReadEncodedValue(true));");
                    }
                }

                if (metadata.IsMapValueTypeOpenGeneric)
                {
                    EmitGenericWithTypeParamsReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapValueTypeFullName))
                    {
                        EmitPrimitivePropertyReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                    }
                    else
                    {
                        // @TODO: Handle nested lists/maps, right now we are assuming that the map value item type is a class
                        sb.AppendLine($"{propertyName}TempValueItem = ({metadata.MapValueTypeFullName}){metadata.MapValueTypeFullName}.Read(reader.ReadEncodedValue(true));");
                    }
                }

                sb.AppendLine($"if (!{propertyName}TempMap.ContainsKey({propertyName}TempKeyItem))");
                sb.AppendLine("{");
                sb.AppendLine($"{propertyName}TempMap.Add({propertyName}TempKeyItem, {propertyName}TempValueItem);");
                sb.AppendLine("}");

                sb.AppendLine("}");
                sb.AppendLine($"reader.ReadEndMap();");
                sb.AppendLine($"{propertyName} = {propertyName}TempMap;");

                return sb;
            }

            if (metadata.IsOpenGeneric)
            {
                EmitGenericWithTypeParamsReader(sb, metadata.PropertyTypeFullName, propertyName);
            }
            else
            {
                sb.AppendLine($"{propertyName} = ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName}.Read(reader.ReadEncodedValue(true));");
            }
            return sb;
        }

        public static StringBuilder EmitCborReaderInstance(StringBuilder sb, string dataName)
        {
            sb.AppendLine($"var reader = new CborReader({dataName}, CborConformanceMode.Lax);");
            return sb;
        }

        public static StringBuilder EmitTagReader(StringBuilder sb, int? tag, string propertyName)
        {
            if (tag.HasValue)
            {
                sb.AppendLine($"var {propertyName} = (int)reader.ReadTag();");
                if (tag.Value >= 0)
                {
                    sb.AppendLine($"if ({propertyName} != {tag}) throw new Exception(\"Invalid tag\");");
                }
            }

            return sb;
        }

        public static StringBuilder EmitSerializableTypeValidatorReader(StringBuilder sb, SerializableTypeMetadata metadata, string propertyName)
        {
            if (metadata.Validator is not null)
            {
                sb.AppendLine($"{metadata.Validator} validator = new();");
                sb.AppendLine($"if (!validator.Validate({propertyName})) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitGenericWithTypeParamsReader(StringBuilder sb, string type, string propertyName)
        {
            sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read<{type}>(reader);");
            return sb;
        }

        public static StringBuilder EmitGenericReader(StringBuilder sb, string type, string propertyName)
        {
            sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read(reader, {type});");
            return sb;
        }

        public static StringBuilder EmitPrimitiveOrObjectReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            string cleanPropertyType = metadata.PropertyType.Replace("?", "");
            if (metadata.IsOpenGeneric)
            {
                EmitGenericWithTypeParamsReader(sb, metadata.PropertyType, propertyName);
            }
            else
            {
                if (IsPrimitiveType(cleanPropertyType))
                {
                    EmitPrimitivePropertyReader(sb, metadata.PropertyType, propertyName);
                }
                else
                {
                    EmitObjectPropertyReader(sb, metadata, propertyName);
                }
            }

            return sb;
        }

        public static StringBuilder EmitCustomListReader(StringBuilder sb, SerializableTypeMetadata metadata, int? constrIndex = null)
        {
            Dictionary<string, string> propMapping = [];
            bool detectIndefinite = false;

            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                // Read the array and check if it's indefinite
                sb.AppendLine("int? arrayLength = reader.ReadStartArray();");
                sb.AppendLine("bool isIndefiniteArray = !arrayLength.HasValue;");
                detectIndefinite = true;
            }

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
                propMapping.Add(prop.PropertyName, propName);
                EmitSerializablePropertyReader(sb, prop, propName, true);
            }

            if (metadata.SerializationType == SerializationType.Constr && metadata.CborIndex is not null && metadata.CborIndex >= 0)
            {
                sb.AppendLine("reader.ReadEndArray();");
            }

            sb.AppendLine($"{metadata.FullyQualifiedName} result;");

            if (metadata.Properties.Count == 0)
            {
                sb.AppendLine($"result = new {metadata.FullyQualifiedName}();");
            }
            else
            {
                sb.AppendLine($"result = new {metadata.FullyQualifiedName}(");
                IOrderedEnumerable<SerializablePropertyMetadata> properties = metadata.Properties.OrderBy(p => p.Order);
                IEnumerable<string> propStrings = properties.Select(prop => propMapping[prop.PropertyName]);
                sb.AppendLine(string.Join(",\n", propStrings));
                sb.AppendLine(");");
            }

            // Set the IsIndefinite flag if we detected it
            if (detectIndefinite)
            {
                sb.AppendLine("if (isIndefiniteArray)");
                sb.AppendLine("{");
                sb.AppendLine($"    result.IsIndefinite = true;");
                sb.AppendLine("}");
            }
            

            EmitReaderValidationAndResult(sb, metadata, "result");

            return sb;
        }

        public static StringBuilder EmitReaderValidationAndResult(StringBuilder sb, SerializableTypeMetadata metadata, string resultName)
        {
            EmitSerializableTypeValidatorReader(sb, metadata, resultName);

            if (metadata.SerializationType == SerializationType.Constr)
            {
                sb.AppendLine($"{resultName}.ConstrIndex = constrIndex;");
            }

            if (metadata.ShouldPreserveRaw)
            {
                sb.AppendLine($"{resultName}.Raw = data;");
            }

            sb.AppendLine($"return {resultName};");
            return sb;
        }
    }
}