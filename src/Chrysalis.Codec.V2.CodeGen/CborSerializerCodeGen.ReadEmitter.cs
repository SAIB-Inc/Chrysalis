using System.Text;

namespace Chrysalis.Codec.V2.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static partial class Emitter
    {
        public static StringBuilder EmitSerializableTypeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            string xmlSafeId = metadata.Indentifier.Replace("<", "{").Replace(">", "}");

            _ = sb.AppendLine($"/// <summary>");
            _ = sb.AppendLine($"/// Deserializes a <see cref=\"{xmlSafeId}\"/> instance from CBOR-encoded bytes and reports bytes consumed.");
            _ = sb.AppendLine($"/// </summary>");
            _ = sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data, out int bytesConsumed)");
            _ = sb.AppendLine("{");
            ICborSerializerEmitter emitter = GetEmitter(metadata);
            _ = emitter.EmitReader(sb, metadata);
            _ = sb.AppendLine("}");

            _ = sb.AppendLine();

            _ = sb.AppendLine($"/// <summary>");
            _ = sb.AppendLine($"/// Deserializes a <see cref=\"{xmlSafeId}\"/> instance from CBOR-encoded bytes.");
            _ = sb.AppendLine($"/// </summary>");
            _ = sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"return Read(data, out _);");
            _ = sb.AppendLine("}");

            return sb;
        }

        /// <summary>
        /// Emits a lazy Read() for CborList record struct types.
        /// Scans field boundaries with SkipDataItem(), stores slices in _fieldN.
        /// </summary>
        public static StringBuilder EmitLazyListReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            bool hasTag = metadata.CborTag.HasValue;
            bool isConstr = metadata.SerializationType == SerializationType.Constr;
            int? constrIndex = isConstr ? ResolveTag(metadata.CborIndex) : null;

            // Read optional semantic tag
            if (hasTag)
            {
                _ = EmitTagReader(sb, metadata.CborTag, "tagIndex");
            }

            // Read constr tag if applicable
            if (isConstr && constrIndex.HasValue)
            {
                _ = EmitTagReader(sb, constrIndex, "constrIndex");
            }

            // Read array header
            bool needsArrayHeader = !(isConstr && (metadata.CborIndex is null || metadata.CborIndex < 0));
            if (needsArrayHeader)
            {
                _ = sb.AppendLine("reader.ReadBeginArray();");
                _ = sb.AppendLine("int arraySize = reader.ReadSize();");
                _ = sb.AppendLine("bool isIndefiniteArray = arraySize == -1;");
            }

            // Scan field boundaries with SkipDataItem()
            int fieldCount = metadata.Properties.Count;
            // Find first nullable field index to know when to guard with size checks
            int firstNullableIndex = -1;
            for (int i = 0; i < fieldCount; i++)
            {
                if (metadata.Properties[i].IsTypeNullable)
                {
                    firstNullableIndex = i;
                    break;
                }
            }

            for (int i = 0; i < fieldCount; i++)
            {
                if (firstNullableIndex >= 0 && i >= firstNullableIndex)
                {
                    // Guard nullable fields — they may not be present in the array
                    if (needsArrayHeader)
                    {
                        _ = sb.AppendLine($"int _s{i} = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"if ((arraySize == -1 ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : arraySize > {i}))");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine("reader.SkipDataItem();");
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine($"int _s{i} = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine("if (reader.Buffer.Length > 0) reader.SkipDataItem();");
                    }
                }
                else
                {
                    _ = sb.AppendLine($"int _s{i} = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine("reader.SkipDataItem();");
                }
            }

            // Capture end of last field before any break byte
            _ = sb.AppendLine("int _lastFieldEnd = data.Length - reader.Buffer.Length;");

            // Handle indefinite array break byte
            if (needsArrayHeader)
            {
                _ = sb.AppendLine("if (isIndefiniteArray && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
            }

            // Calculate bytesConsumed and build result
            _ = sb.AppendLine("bytesConsumed = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"return new {metadata.FullyQualifiedName}");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("_raw = data[..bytesConsumed],");

            for (int i = 0; i < fieldCount; i++)
            {
                string end = (i < fieldCount - 1) ? $"_s{i + 1}" : "_lastFieldEnd";
                _ = sb.AppendLine($"_field{i} = data[_s{i}..{end}],");
            }

            if (needsArrayHeader)
            {
                _ = sb.AppendLine("_isIndefinite = isIndefiniteArray,");
            }

            if (isConstr && constrIndex.HasValue && constrIndex.Value < 0)
            {
                _ = sb.AppendLine("_constrIndex = constrIndex,");
            }
            else if (isConstr && constrIndex.HasValue)
            {
                _ = sb.AppendLine($"_constrIndex = {metadata.CborIndex ?? 0},");
            }

            _ = sb.AppendLine("};");

            return sb;
        }

        public static StringBuilder EmitLazyContainerReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            bool hasTag = metadata.CborTag.HasValue;

            // Read optional semantic tag
            if (hasTag)
            {
                _ = EmitTagReader(sb, metadata.CborTag, "tagIndex");
            }

            // For single-primitive containers, validate CBOR major type so union try-catch can discriminate
            if (metadata.Properties.Count == 1)
            {
                string? guard = GetCborMajorTypeGuard(metadata.Properties[0]);
                if (guard is not null)
                {
                    _ = sb.AppendLine(guard);
                }
            }

            // For a container, the single field IS the entire CBOR data item (no array wrapper)
            _ = sb.AppendLine("int _s0 = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine("reader.SkipDataItem();");

            // Calculate bytesConsumed and build result
            _ = sb.AppendLine("bytesConsumed = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"return new {metadata.FullyQualifiedName}");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("_raw = data[..bytesConsumed],");
            _ = sb.AppendLine("_field0 = data[_s0..bytesConsumed],");
            _ = sb.AppendLine("};");

            return sb;
        }

        /// <summary>
        /// Emits a lazy Read() for CborMap record struct types.
        /// Scans field boundaries with SkipDataItem(), stores slices in _fieldN.
        /// </summary>
        public static StringBuilder EmitLazyMapReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            bool hasTag = metadata.CborTag.HasValue;
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;
            int fieldCount = metadata.Properties.Count;

            // Read optional semantic tag
            if (hasTag)
            {
                _ = EmitTagReader(sb, metadata.CborTag, "tagIndex");
            }

            // Read map header
            _ = sb.AppendLine("reader.ReadBeginMap();");
            _ = sb.AppendLine("int mapSize = reader.ReadSize();");
            _ = sb.AppendLine("bool isIndefiniteMap = mapSize == -1;");
            _ = sb.AppendLine("int mapRemaining = mapSize;");

            // Declare slice variables for each property
            for (int i = 0; i < fieldCount; i++)
            {
                _ = sb.AppendLine($"ReadOnlyMemory<byte> _sf{i} = default;");
            }

            // Loop through map entries
            _ = sb.AppendLine("while (isIndefiniteMap ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : mapRemaining > 0)");
            _ = sb.AppendLine("{");

            // Read key
            _ = isIntKey
                ? sb.AppendLine("int key = reader.ReadInt32();")
                : sb.AppendLine("string key = reader.ReadString();");

            // Mark value start, skip value, mark value end
            _ = sb.AppendLine("int _vs = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine("reader.SkipDataItem();");
            _ = sb.AppendLine("int _ve = data.Length - reader.Buffer.Length;");

            // Switch on key to store slice in corresponding field
            _ = sb.AppendLine("switch (key)");
            _ = sb.AppendLine("{");
            for (int i = 0; i < fieldCount; i++)
            {
                SerializablePropertyMetadata prop = metadata.Properties[i];
                string keyLiteral = isIntKey
                    ? prop.PropertyKeyInt?.ToString(System.Globalization.CultureInfo.InvariantCulture)!
                    : $"\"{prop.PropertyKeyString}\"";
                _ = sb.AppendLine($"case {keyLiteral}:");
                _ = sb.AppendLine($"_sf{i} = data[_vs.._ve];");
                _ = sb.AppendLine("break;");
            }
            _ = sb.AppendLine("default:");
            _ = sb.AppendLine("break;");
            _ = sb.AppendLine("}");

            _ = sb.AppendLine("if (mapSize > 0) mapRemaining--;");
            _ = sb.AppendLine("}");

            // Handle indefinite break byte
            _ = sb.AppendLine("if (isIndefiniteMap && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");

            // Calculate bytesConsumed and build result
            _ = sb.AppendLine("bytesConsumed = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"return new {metadata.FullyQualifiedName}");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("_raw = data[..bytesConsumed],");

            for (int i = 0; i < fieldCount; i++)
            {
                _ = sb.AppendLine($"_field{i} = _sf{i},");
            }

            _ = sb.AppendLine("_isIndefinite = isIndefiniteMap,");
            _ = sb.AppendLine("};");

            return sb;
        }

        public static StringBuilder EmitSerializablePropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, bool isList = false, bool trackFields = false, int fieldIndex = -1, string? parentRemainingVar = null)
        {
            _ = sb.AppendLine($"{metadata.PropertyTypeFullName} {propertyName} = default{(metadata.PropertyType.Contains("?") ? "" : "!")};");

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

                if (trackFields && fieldIndex >= 0)
                {
                    _ = sb.AppendLine($"fieldsRead[{fieldIndex}] = true;");
                }
            }

            if (metadata.IsNullable || metadata.IsTypeNullable)
            {
                bool isValueTypeMemory = !metadata.IsTypeNullable && IsReadOnlyMemoryByteType(metadata.PropertyTypeFullName);
                bool isGenericTypeParam = (metadata.PropertyTypeFullName.StartsWith("T", StringComparison.Ordinal) && metadata.PropertyTypeFullName.Length <= 2)
                    || (metadata.PropertyTypeFullName.Contains("?") && metadata.IsOpenGeneric);
                string nullAssignment = (isValueTypeMemory || isGenericTypeParam) ? "default" : "null";
                _ = sb.AppendLine($"if (reader.Buffer.Length > 0 && (reader.Buffer[0] == 0xF6 || reader.Buffer[0] == 0xF7))");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"reader.ReadNull();");
                _ = sb.AppendLine($"{propertyName} = {nullAssignment};");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"else");
                _ = sb.AppendLine("{");
            }

            _ = EmitPrimitiveOrObjectReader(sb, metadata, propertyName);

            if (metadata.IsNullable || metadata.IsTypeNullable)
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
                    _ = sb.AppendLine($"{propertyName} = reader.ReadByteStringToArray();");
                    break;
                case "ReadOnlyMemory<byte>?":
                case "ReadOnlyMemory<byte>":
                case "System.ReadOnlyMemory<byte>?":
                case "System.ReadOnlyMemory<byte>":
                case "global::System.ReadOnlyMemory<byte>?":
                case "global::System.ReadOnlyMemory<byte>":
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("bool _isIndef = reader.Buffer.Length > 0 && reader.Buffer[0] == 0x5F;");
                    _ = sb.AppendLine("if (_isIndef)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"{propertyName} = (ReadOnlyMemory<byte>)reader.ReadByteStringToArray();");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("ReadOnlySpan<byte> _bs = reader.ReadByteString();");
                    _ = sb.AppendLine("int _after = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine($"{propertyName} = data.Slice(_after - _bs.Length, _bs.Length);");
                    _ = sb.AppendLine("}");
                    _ = sb.AppendLine("}");
                    break;
                case "CborEncodedValue":
                case "Chrysalis.Codec.V2.Types.CborEncodedValue":
                case "global::Chrysalis.Codec.V2.Types.CborEncodedValue":
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine($"var _span = reader.ReadDataItem();");
                    _ = sb.AppendLine($"{propertyName} = new {CborEncodeValueFullName}(data.Slice(_pos, _span.Length));");
                    _ = sb.AppendLine("}");
                    break;
                case "CborLabel":
                case "Chrysalis.Codec.V2.Types.CborLabel":
                case "global::Chrysalis.Codec.V2.Types.CborLabel":
                    _ = sb.AppendLine($"{propertyName} = reader.GetCurrentDataItemType() switch");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    CborDataItemType.Unsigned or CborDataItemType.Signed => new Chrysalis.Codec.V2.Types.CborLabel(reader.ReadInt64()),");
                    _ = sb.AppendLine("    CborDataItemType.String => new Chrysalis.Codec.V2.Types.CborLabel(reader.ReadString()),");
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
                _ = sb.AppendLine($"reader.ReadBeginArray();");
                _ = sb.AppendLine($"int {propertyName}ArraySize = reader.ReadSize();");
                _ = sb.AppendLine($"bool {propertyName}IsIndefinite = {propertyName}ArraySize == -1;");

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
                    else if (metadata.IsListItemTypeUnion)
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"{propertyName}TempItem = ({metadata.ListItemTypeFullName}){metadata.ListItemTypeFullName}.Read(data.Slice(_pos), out int _consumed);");
                        _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"{propertyName}TempItem = ({metadata.ListItemTypeFullName}){metadata.ListItemTypeFullName}.Read(data.Slice(_pos), out int _consumed);");
                        _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                        _ = sb.AppendLine("}");
                    }
                }

                _ = sb.AppendLine($"{propertyName}TempList.Add({propertyName}TempItem);");
                _ = sb.AppendLine($"if ({propertyName}ArraySize > 0) {propertyName}ArrayRemaining--;");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}IsIndefinite && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
                _ = sb.AppendLine($"{propertyName} = {propertyName}TempList;");
                _ = sb.AppendLine($"if ({propertyName}IsIndefinite)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    Chrysalis.Codec.V2.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
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
                    ? sb.AppendLine($"Dictionary<{metadata.MapKeyTypeFullName}, {metadata.MapValueTypeFullName}> {propertyName}TempMap = new(global::Chrysalis.Codec.V2.Serialization.Utils.ReadOnlyMemoryComparer.Instance);")
                    : sb.AppendLine($"Dictionary<{metadata.MapKeyTypeFullName}, {metadata.MapValueTypeFullName}> {propertyName}TempMap = new();");
                _ = sb.AppendLine($"{metadata.MapKeyTypeFullName} {propertyName}TempKeyItem = default;");
                _ = sb.AppendLine($"{metadata.MapValueTypeFullName} {propertyName}TempValueItem = default;");
                _ = sb.AppendLine($"reader.ReadBeginMap();");
                _ = sb.AppendLine($"int {propertyName}MapSize = reader.ReadSize();");
                _ = sb.AppendLine($"bool {propertyName}MapIsIndefinite = {propertyName}MapSize == -1;");

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
                    else if (metadata.IsMapKeyTypeUnion)
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"{propertyName}TempKeyItem = ({metadata.MapKeyTypeFullName}){metadata.MapKeyTypeFullName}.Read(data.Slice(_pos), out int _consumed);");
                        _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"{propertyName}TempKeyItem = ({metadata.MapKeyTypeFullName}){metadata.MapKeyTypeFullName}.Read(data.Slice(_pos), out int _consumed);");
                        _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
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
                    else if (metadata.IsMapValueTypeUnion)
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"{propertyName}TempValueItem = ({metadata.MapValueTypeFullName}){metadata.MapValueTypeFullName}.Read(data.Slice(_pos), out int _consumed);");
                        _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"{propertyName}TempValueItem = ({metadata.MapValueTypeFullName}){metadata.MapValueTypeFullName}.Read(data.Slice(_pos), out int _consumed);");
                        _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                        _ = sb.AppendLine("}");
                    }
                }

                _ = sb.AppendLine($"if (!{propertyName}TempMap.ContainsKey({propertyName}TempKeyItem))");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"{propertyName}TempMap.Add({propertyName}TempKeyItem, {propertyName}TempValueItem);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}MapSize > 0) {propertyName}MapRemaining--;");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
                _ = sb.AppendLine($"{propertyName} = {propertyName}TempMap;");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    Chrysalis.Codec.V2.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
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
            else if (metadata.IsPropertyTypeUnion)
            {
                string callType = metadata.PropertyTypeFullName.Replace("?", "");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                _ = sb.AppendLine($"{propertyName} = ({metadata.PropertyTypeFullName}){callType}.Read(data.Slice(_pos), out int _consumed);");
                _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                _ = sb.AppendLine("}");
            }
            else
            {
                string callType = metadata.PropertyTypeFullName.Replace("?", "");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                _ = sb.AppendLine($"{propertyName} = ({metadata.PropertyTypeFullName}){callType}.Read(data.Slice(_pos), out int _consumed);");
                _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                _ = sb.AppendLine("}");
            }
            return sb;
        }

        public static StringBuilder EmitUnionHintReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            string discriminantProp = metadata.UnionHintDiscriminantProperty!;
            string suffix = metadata.PropertyName;
            string prefix = propertyName.EndsWith(suffix, StringComparison.Ordinal)
                ? propertyName.Substring(0, propertyName.Length - suffix.Length)
                : propertyName;
            string discriminantVar = $"{prefix}{discriminantProp}";

            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"int _consumed;");
            _ = sb.AppendLine($"{propertyName} = {discriminantVar} switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, string> hint in metadata.UnionHints)
            {
                _ = sb.AppendLine($"    {hint.Key} => ({metadata.PropertyTypeFullName}){hint.Value}.Read(data.Slice(_pos), out _consumed),");
            }
            _ = sb.AppendLine($"    _ => ({metadata.PropertyTypeFullName}){metadata.PropertyTypeFullName.Replace("?", "")}.Read(data.Slice(_pos), out _consumed)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
            _ = sb.AppendLine("}");
            return sb;
        }

        /// <summary>
        /// Returns a guard statement that validates the CBOR major type for a primitive property,
        /// or null if no guard is needed.
        /// </summary>
        private static string? GetCborMajorTypeGuard(SerializablePropertyMetadata prop)
        {
            string cleanType = prop.PropertyType.Replace("?", "");
            // CBOR major types: 0=uint, 1=nint, 2=bstr, 3=tstr, 4=array, 5=map, 7=simple/float
            return cleanType switch
            {
                "int" or "long" =>
                    "{ byte _mt = (byte)(reader.Buffer[0] >> 5); if (_mt != 0 && _mt != 1) throw new Exception(\"Expected CBOR integer\"); }",
                "uint" or "ulong" =>
                    "{ byte _mt = (byte)(reader.Buffer[0] >> 5); if (_mt != 0) throw new Exception(\"Expected CBOR unsigned integer\"); }",
                "string" =>
                    "{ byte _mt = (byte)(reader.Buffer[0] >> 5); if (_mt != 3) throw new Exception(\"Expected CBOR text string\"); }",
                "bool" =>
                    "{ byte _b = reader.Buffer[0]; if (_b != 0xF4 && _b != 0xF5) throw new Exception(\"Expected CBOR boolean\"); }",
                "float" =>
                    "{ byte _b = reader.Buffer[0]; if (_b != 0xFA) throw new Exception(\"Expected CBOR float32\"); }",
                "double" =>
                    "{ byte _b = reader.Buffer[0]; if (_b != 0xFB) throw new Exception(\"Expected CBOR float64\"); }",
                "byte[]" =>
                    "{ byte _mt = (byte)(reader.Buffer[0] >> 5); if (_mt != 2) throw new Exception(\"Expected CBOR byte string\"); }",
                _ => null
            };
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
                if (tag.Value >= 0)
                {
                    _ = sb.AppendLine($"reader.TryReadSemanticTag(out ulong _{propertyName}Raw);");
                    _ = sb.AppendLine($"var {propertyName} = (int)_{propertyName}Raw;");
                    _ = sb.AppendLine($"if ({propertyName} != {tag}) throw new Exception(\"Invalid tag\");");
                }
                else
                {
                    _ = sb.AppendLine($"if (!reader.TryReadSemanticTag(out ulong _{propertyName}Raw))");
                    _ = sb.AppendLine($"    throw new Exception(\"Expected semantic tag for {propertyName}\");");
                    _ = sb.AppendLine($"var {propertyName} = (int)_{propertyName}Raw;");
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
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"{propertyName} = {GenericSerializationUtilFullname}.ReadAnyWithConsumed<{type}>(data.Slice(_pos), out int _consumed);");
            _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
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

        public static StringBuilder EmitMapValueReader(StringBuilder sb, SerializablePropertyMetadata prop)
        {
            string cleanType = prop.PropertyType.Replace("?", "");

            if (prop.IsOpenGeneric)
            {
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                _ = sb.AppendLine($"value = {GenericSerializationUtilFullname}.ReadAnyWithConsumed<{prop.PropertyType}>(data.Slice(_pos), out int _consumed);");
                _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                _ = sb.AppendLine("}");
            }
            else if (IsPrimitiveType(cleanType))
            {
                string tempVar = $"_val_{prop.PropertyName}";
                _ = sb.AppendLine($"{cleanType} {tempVar};");
                _ = EmitPrimitivePropertyReader(sb, prop.PropertyType, tempVar);
                _ = sb.AppendLine($"value = {tempVar};");
            }
            else if (prop.IsPropertyTypeUnion)
            {
                string nonNullType = prop.PropertyTypeFullName.Replace("?", "");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                _ = sb.AppendLine($"value = {nonNullType}.Read(data.Slice(_pos), out int _consumed);");
                _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                _ = sb.AppendLine("}");
            }
            else
            {
                string nonNullType = prop.PropertyTypeFullName.Replace("?", "");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                _ = sb.AppendLine($"value = {nonNullType}.Read(data.Slice(_pos), out int _consumed);");
                _ = sb.AppendLine($"reader = new CborReader(data.Span.Slice(_pos + _consumed));");
                _ = sb.AppendLine("}");
            }

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
                _ = sb.AppendLine("reader.ReadBeginArray();");
                _ = sb.AppendLine("int arraySize = reader.ReadSize();");
                _ = sb.AppendLine("bool isIndefiniteArray = arraySize == -1;");
                _ = sb.AppendLine($"int {metadata.BaseIdentifier}ArrayRemaining = arraySize;");
                detectIndefinite = true;

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
                _ = sb.AppendLine($"int {metadata.BaseIdentifier}ArrayRemaining = {metadata.Properties.Count};");
            }

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

            _ = sb.AppendLine($"bytesConsumed = data.Length - reader.Buffer.Length;");

            // V2: All types preserve raw
            if (metadata.ShouldPreserveRaw)
            {
                _ = sb.AppendLine($"{resultName}.Raw = data[..bytesConsumed];");
            }

            _ = sb.AppendLine($"return {resultName};");
            return sb;
        }
    }
}
