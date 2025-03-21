using System.Formats.Cbor;
using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private interface ICborSerializerEmitter
    {
        StringBuilder EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata metadata);
        StringBuilder EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata metadata);
    }

    private static class Emitter
    {
        public const string GenericSerializationUtilFullname = "global::Chrysalis.Cbor.Serialization.Utils.GenericSerializationUtil";
        public const string CborEncodeValueFullName = "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue";

        public static StringBuilder EmitGenericCborReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data)");
            sb.AppendLine("{");

            ICborSerializerEmitter emitter = metadata.SerializationType switch
            {
                SerializationType.Constr => new ConstructorEmitter(),
                SerializationType.Container => new ContainerEmitter(),
                SerializationType.List => new ListEmitter(),
                SerializationType.Map => new MapEmitter(),
                SerializationType.Union => new UnionEmitter(),
                _ => throw new NotSupportedException($"Serialization type {metadata.SerializationType} is not supported.")
            };

            emitter.EmitCborDeserializer(sb, metadata);
            sb.AppendLine("}");

            return sb;
        }

        public static StringBuilder EmitGenericCborReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, bool isList = false)
        {
            sb.AppendLine($"{metadata.PropertyTypeFullName} {propertyName} = default{(metadata.PropertyType.Contains("?") ? "" : "!")};");

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

            EmitPropertyRead(sb, metadata, propertyName);

            if (metadata.IsNullable) sb.AppendLine("}");
            if (isList)
            {
                sb.AppendLine("}");
            }

            sb.AppendLine();

            return sb;
        }

        public static StringBuilder EmitPrimitiveCborReader(StringBuilder sb, string type, string propertyName)
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
            }

            return sb;
        }

        private static bool IsPrimitiveType(string type) => type switch
        {
            "bool" => true,
            "int" => true,
            "long" => true,
            "ulong" => true,
            "uint" => true,
            "float" => true,
            "double" => true,
            "decimal" => true,
            "string" => true,
            "byte[]" => true,
            "CborEncodedValue" => true,
            "Chrysalis.Cbor.Types.Primitives.CborEncodedValue" => true,
            "global::Chrysalis.Cbor.Types.Primitives.CborEncodedValue" => true,
            _ => false
        };

        public static StringBuilder EmitCustomCborReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
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
                    EmitGenericSerializationRead(sb, metadata.ListItemType, $"{propertyName}TempItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.ListItemTypeFullName))
                    {
                        EmitPrimitiveCborReader(sb, metadata.ListItemTypeFullName, $"{propertyName}TempItem");
                    }
                    else
                    {
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
                    EmitGenericSerializationRead(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapKeyTypeFullName))
                    {
                        EmitPrimitiveCborReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                    }
                    else
                    {
                        sb.AppendLine($"{propertyName}TempKeyItem = ({metadata.MapKeyTypeFullName}){metadata.MapKeyTypeFullName}.Read(reader.ReadEncodedValue(true));");
                    }
                }

                if (metadata.IsMapValueTypeOpenGeneric)
                {
                    EmitGenericSerializationRead(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapValueTypeFullName))
                    {
                        EmitPrimitiveCborReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                    }
                    else
                    {
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

            // otherwise, handle as custom type
            if (metadata.IsOpenGeneric)
            {
                EmitGenericSerializationRead(sb, metadata.PropertyTypeFullName, propertyName);
            }
            else
            {
                sb.AppendLine($"{propertyName} = ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName}.Read(reader.ReadEncodedValue(true));");
            }
            return sb;
        }

        public static StringBuilder EmitNewCborReader(StringBuilder sb, string dataName)
        {
            sb.AppendLine($"var reader = new CborReader({dataName}, CborConformanceMode.Lax);");
            return sb;
        }

        public static StringBuilder EmitTagReader(StringBuilder sb, int? tag, string propertyName)
        {
            if (tag.HasValue)
            {
                if (tag.Value == -1)
                {
                    sb.AppendLine("reader.ReadTag();");
                }
                else
                {
                    sb.AppendLine($"var {propertyName} = (int)reader.ReadTag();");
                    sb.AppendLine($"if ({propertyName} != {tag}) throw new Exception(\"Invalid tag\");");
                }
            }

            return sb;
        }

        public static StringBuilder EmitValidator(StringBuilder sb, SerializableTypeMetadata metadata, string propertyName)
        {
            if (metadata.Validator is not null)
            {
                sb.AppendLine($"{metadata.Validator} validator = new();");
                sb.AppendLine($"if (!validator.Validate({propertyName})) throw new Exception(\"Validation failed\");");
            }

            return sb;
        }

        public static StringBuilder EmitGenericSerializationRead(StringBuilder sb, string type, string propertyName)
        {
            sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read<{type}>(reader);");
            return sb;
        }

        public static StringBuilder EmitSerializationRead(StringBuilder sb, string type, string propertyName)
        {
            sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.Read(reader, {type});");
            return sb;
        }

        public static StringBuilder EmitPropertyRead(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            string cleanPropertyType = metadata.PropertyType.Replace("?", "");
            if (metadata.IsOpenGeneric)
            {
                EmitGenericSerializationRead(sb, metadata.PropertyType, propertyName);
            }
            else
            {
                if (IsPrimitiveType(cleanPropertyType))
                {
                    EmitPrimitiveCborReader(sb, metadata.PropertyType, propertyName);
                }
                else
                {
                    EmitCustomCborReader(sb, metadata, propertyName);
                }
            }

            return sb;
        }

        public static StringBuilder EmitListRead(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Dictionary<string, string> propMapping = [];

            if (!(metadata.SerializationType == SerializationType.Constr && (metadata.CborIndex is null || metadata.CborIndex < 0)))
            {
                sb.AppendLine("reader.ReadStartArray();");
            }

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                // if not yet end of array process property
                string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
                propMapping.Add(prop.PropertyName, propName);
                EmitGenericCborReader(sb, prop, propName, true);
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

            EmitReadFinalizer(sb, metadata, "result");

            return sb;
        }

        public static StringBuilder EmitReadFinalizer(StringBuilder sb, SerializableTypeMetadata metadata, string resultName)
        {
            EmitValidator(sb, metadata, resultName);

            if (metadata.ShouldPreserveRaw)
            {
                sb.AppendLine($"{resultName}.Raw = data;");
            }

            sb.AppendLine($"return {resultName};");

            return sb;
        }
    }
}
