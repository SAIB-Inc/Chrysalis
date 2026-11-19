using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    internal static partial class Emitter
    {
        public static DiagnosticDescriptor? EmitSerializableTypeReader(StringBuilder sb, SerializableTypeMetadata metadata, ImmutableDictionary<string, SerializableTypeMetadata>? typeRegistry)
        {
            DiagnosticDescriptor? diagnostic = null;
            string xmlSafeId = metadata.Indentifier.Replace("<", "{").Replace(">", "}");

            _ = sb.AppendLine($"/// <summary>");
            _ = sb.AppendLine($"/// Deserializes a <see cref=\"{xmlSafeId}\"/> instance from CBOR-encoded bytes and reports bytes consumed.");
            _ = sb.AppendLine($"/// </summary>");
            _ = sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data, out int bytesConsumed)");
            _ = sb.AppendLine("{");
            if (NormalizeTypeName(metadata.FullyQualifiedName) == "Chrysalis.Codec.Types.Cardano.Core.Common.PlutusConstr")
            {
                _ = sb.AppendLine("try { return PlutusWire.ReadConstructor(data, out bytesConsumed); }");
                _ = sb.AppendLine("catch (global::SAIB.Cbor.CborException error) { throw CborError.Wrap(error, data, nameof(PlutusConstr), \"Plutus constructor\"); }");
            }
            else
            {
                ICborSerializerEmitter emitter = GetEmitter(metadata, typeRegistry);
                _ = emitter.EmitReader(sb, metadata);
                diagnostic = (emitter as UnionEmitter)?.ReadDiagnostic;
            }
            _ = sb.AppendLine("}");

            _ = sb.AppendLine();

            _ = sb.AppendLine($"/// <summary>");
            _ = sb.AppendLine($"/// Deserializes a <see cref=\"{xmlSafeId}\"/> instance from CBOR-encoded bytes.");
            _ = sb.AppendLine($"/// </summary>");
            _ = sb.AppendLine($"public static new {metadata.FullyQualifiedName} Read(ReadOnlyMemory<byte> data)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"return Read(data, out _);");
            _ = sb.AppendLine("}");

            if (metadata.Validator is not null)
            {
                _ = sb.AppendLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]");
                _ = sb.AppendLine("private static Exception __CborValidationError(int offset)");
                _ = sb.AppendLine($"    => CborError.Validation(offset, \"{metadata.FullyQualifiedName}\", \"validator {metadata.Validator} succeeds\", \"validation failed\");");
                if (metadata.IsRecordStruct && metadata.Properties.All(property => !property.IsTypeNullable)
                    && metadata.SerializationType is SerializationType.List or SerializationType.Constr)
                {
                    _ = sb.AppendLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]");
                    _ = sb.AppendLine("private static Exception __CborArrayLengthError(ReadOnlyMemory<byte> data, int offset, int length)");
                    _ = sb.AppendLine($"    => CborError.Decode(data, offset, \"{metadata.FullyQualifiedName}\", \"array length {metadata.Properties.Count}\", length < 0 ? \"indefinite array\" : $\"array length {{length}}\");");
                }
            }

            return diagnostic;
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
                _ = EmitTagReader(sb, metadata.CborTag, "tagIndex", metadata.FullyQualifiedName);
            }

            // Read constr tag if applicable
            if (isConstr && constrIndex.HasValue)
            {
                _ = EmitTagReader(sb, constrIndex, "constrIndex", metadata.FullyQualifiedName);
            }

            // Read array header
            bool needsArrayHeader = !(isConstr && (metadata.CborIndex is null || metadata.CborIndex < 0));
            bool validateSize = metadata.Validator is not null && metadata.Properties.All(property => !property.IsTypeNullable);
            string arityFailure = "throw __CborArrayLengthError(data, _arrayStart, arraySize);";
            if (needsArrayHeader)
            {
                if (validateSize)
                {
                    _ = sb.AppendLine("int _arrayStart = data.Length - reader.Buffer.Length;");
                }
                _ = sb.AppendLine("reader.ReadBeginArray();");
                _ = sb.AppendLine("int arraySize = reader.ReadSize();");
                _ = sb.AppendLine("bool isIndefiniteArray = arraySize == -1;");
                if (validateSize)
                {
                    _ = sb.AppendLine($"if (arraySize != -1 && arraySize != {metadata.Properties.Count}) {arityFailure}");
                }
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
                SerializablePropertyMetadata property = metadata.Properties[i];
                string skipField = property.IsLedgerText
                    ? $"global::Chrysalis.Codec.Serialization.LedgerText.Skip(ref reader, data, \"{metadata.FullyQualifiedName}.{property.PropertyName}\");"
                    : "reader.SkipDataItem();";
                if (firstNullableIndex >= 0 && i >= firstNullableIndex)
                {
                    // Guard nullable fields — they may not be present in the array
                    if (needsArrayHeader)
                    {
                        _ = sb.AppendLine($"int _s{i} = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"if ((arraySize == -1 ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : arraySize > {i}))");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine(skipField);
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine($"int _s{i} = data.Length - reader.Buffer.Length;");
                        _ = sb.AppendLine($"if (reader.Buffer.Length > 0) {skipField}");
                    }
                }
                else
                {
                    if (validateSize && needsArrayHeader)
                    {
                        _ = sb.AppendLine($"if (isIndefiniteArray && (reader.Buffer.Length == 0 || reader.Buffer[0] == 0xFF)) {arityFailure}");
                    }
                    _ = sb.AppendLine($"int _s{i} = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine(skipField);
                }
            }

            // Capture end of last field before any break byte
            _ = sb.AppendLine("int _lastFieldEnd = data.Length - reader.Buffer.Length;");

            // Handle indefinite array break byte
            if (needsArrayHeader)
            {
                if (validateSize)
                {
                    _ = sb.AppendLine($"if (isIndefiniteArray && (reader.Buffer.Length == 0 || reader.Buffer[0] != 0xFF)) {arityFailure}");
                }
                _ = sb.AppendLine("if (isIndefiniteArray && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
            }

            // Calculate bytesConsumed and build result
            _ = sb.AppendLine("bytesConsumed = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine(metadata.Validator is null
                ? $"return new {metadata.FullyQualifiedName}"
                : $"{metadata.FullyQualifiedName} result = new()");
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
            if (metadata.Validator is not null)
            {
                _ = EmitSerializableTypeValidatorReader(sb, metadata, "result");
                _ = sb.AppendLine("return result;");
            }

            return sb;
        }

        public static StringBuilder EmitLazyContainerReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            bool hasTag = metadata.CborTag.HasValue;

            // Read optional semantic tag
            if (hasTag)
            {
                _ = EmitTagReader(sb, metadata.CborTag, "tagIndex", metadata.FullyQualifiedName);
            }

            // For single-primitive containers, validate CBOR major type so union try-catch can discriminate
            string? taggedScalarKind = null;
            string? taggedScalarPath = null;
            if (metadata.Properties.Count == 1)
            {
                SerializablePropertyMetadata prop = metadata.Properties[0];
                taggedScalarKind = prop.PropertyType.Replace("?", "") switch
                {
                    "string" => "text string",
                    "byte[]" => "byte string",
                    _ => null
                };
                string shapeBuffer = taggedScalarKind is not null ? "_shapeBuffer" : "reader.Buffer";
                string? guard = GetCborMajorTypeGuard(prop, metadata.FullyQualifiedName, shapeBuffer);
                if (guard is not null)
                {
                    if (taggedScalarKind is not null)
                    {
                        taggedScalarPath = $"{metadata.FullyQualifiedName}.{prop.PropertyName}";
                        _ = EmitContentBuffer(sb, "reader", shapeBuffer);
                    }
                    _ = sb.AppendLine(guard);
                }
            }

            // For a container, the single field IS the entire CBOR data item (no array wrapper)
            _ = sb.AppendLine("int _s0 = data.Length - reader.Buffer.Length;");
            if (taggedScalarPath is not null)
            {
                // The shape guard used to reject tagged scalars with a typed error. Preserve that
                // contract if the newly accepted header's content is invalid, without wrapping untagged scans.
                _ = sb.AppendLine("try");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("reader.SkipDataItem();");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("catch (global::SAIB.Cbor.CborException error) when ((data.Span[_s0] >> 5) == 6)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"throw CborError.Reader(error, data, reader.Buffer.Length, _s0, false, \"{taggedScalarPath}\", \"{taggedScalarKind}\");");
                _ = sb.AppendLine("}");
            }
            else
            {
                _ = sb.AppendLine("reader.SkipDataItem();");
            }

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
                _ = EmitTagReader(sb, metadata.CborTag, "tagIndex", metadata.FullyQualifiedName);
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

            // Track only unrecognized keys separately; known fields already have distinct slice slots.
            _ = sb.AppendLine($"HashSet<{(isIntKey ? "int" : "string")}>? unknownKeys = null;");

            // Loop through map entries
            _ = sb.AppendLine("while (isIndefiniteMap ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : mapRemaining > 0)");
            _ = sb.AppendLine("{");

            // Read key
            _ = sb.AppendLine("int keyOffset = data.Length - reader.Buffer.Length;");
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
                _ = sb.AppendLine($"if (!_sf{i}.IsEmpty) throw CborError.Decode(data, keyOffset, \"{metadata.FullyQualifiedName}\", \"unique field key\", key);");
                _ = sb.AppendLine($"_sf{i} = data[_vs.._ve];");
                _ = sb.AppendLine("break;");
            }
            _ = sb.AppendLine("default:");
            _ = sb.AppendLine($"if (!(unknownKeys ??= new()).Add(key)) throw CborError.Decode(data, keyOffset, \"{metadata.FullyQualifiedName}\", \"unique field key\", key);");
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

        public static StringBuilder EmitSerializablePropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, string typePath, bool isList = false, bool trackFields = false, int fieldIndex = -1, string? parentRemainingVar = null)
        {
            _ = sb.AppendLine($"{metadata.PropertyTypeFullName} {propertyName} = default{(metadata.PropertyType.Contains("?") ? "" : "!")};");

            if (metadata.PropertyName == "Value" && metadata.PropertyType == "object")
            {
                _ = sb.AppendLine($"{propertyName} = reader.GetCurrentDataItemType() switch");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("    CborDataItemType.Unsigned or CborDataItemType.Signed => (object)reader.ReadInt64(),");
                _ = sb.AppendLine("    CborDataItemType.String => (object)reader.ReadString(),");
                _ = sb.AppendLine($"    _ => throw CborError.Decode(data, data.Length - reader.Buffer.Length, \"{typePath}.{metadata.PropertyName}\", \"integer or text string\")");
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

            string errorStart = $"_errorStart{propertyName}";
            string errorSlice = $"_errorSlice{propertyName}";
            string fieldPath = $"{typePath}.{metadata.PropertyName}";
            string fieldType = $"typeof({metadata.PropertyTypeFullName.Replace("?", "")})";
            string? errorSlot = null;
            string expectedType = fieldType;
            if (metadata.IsList && !metadata.IsListItemTypeOpenGeneric)
            {
                errorSlot = $"_errorSlot{propertyName}";
                expectedType = $"({errorSlot} == 1 ? typeof({metadata.ListItemTypeFullName!.Replace("?", "")}) : {fieldType})";
            }
            else if (metadata.IsMap && (!metadata.IsMapKeyTypeOpenGeneric || !metadata.IsMapValueTypeOpenGeneric))
            {
                errorSlot = $"_errorSlot{propertyName}";
                string keyType = metadata.IsMapKeyTypeOpenGeneric ? fieldType : $"typeof({metadata.MapKeyTypeFullName!.Replace("?", "")})";
                string valueType = metadata.IsMapValueTypeOpenGeneric ? fieldType : $"typeof({metadata.MapValueTypeFullName!.Replace("?", "")})";
                expectedType = $"({errorSlot} switch {{ 1 => {keyType}, 2 => {valueType}, _ => {fieldType} }})";
            }
            _ = sb.AppendLine($"int {errorStart} = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"bool {errorSlice} = false;");
            if (errorSlot is not null)
            {
                _ = sb.AppendLine($"int {errorSlot} = 0;");
            }
            _ = sb.AppendLine("try");
            _ = sb.AppendLine("{");
            _ = EmitPrimitiveOrObjectReader(sb, metadata, propertyName, fieldPath, errorStart, errorSlice, errorSlot);
            _ = sb.AppendLine("}");
            string context = $"error, data, reader.Buffer.Length, {errorStart}, {errorSlice}, \"{fieldPath}\"";
            _ = sb.AppendLine($"catch (Exception error) when (CborError.ShouldWrapReader({context}, {expectedType}))");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"throw CborError.Reader({context}, CborError.Expected({expectedType}));");
            _ = sb.AppendLine("}");

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

        public static StringBuilder EmitPrimitivePropertyReader(StringBuilder sb, string type, string propertyName, string? typePath = null)
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
                    _ = EmitByteMemoryRead(sb, "reader", "data", $"{propertyName} =");
                    break;
                case "CborEncodedValue":
                case "Chrysalis.Codec.Types.CborEncodedValue":
                case "global::Chrysalis.Codec.Types.CborEncodedValue":
                    _ = EmitByteMemoryRead(sb, "reader", "data", $"{propertyName} =", encoded: true);
                    break;
                case "CborAny":
                case "Chrysalis.Codec.Types.CborAny":
                case "global::Chrysalis.Codec.Types.CborAny":
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"int _pos = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine($"reader.ReadDataItem();");
                    _ = sb.AppendLine($"int _end = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine($"{propertyName} = new global::Chrysalis.Codec.Types.CborAny(data[_pos.._end]);");
                    _ = sb.AppendLine("}");
                    break;
                case "CborLabel":
                case "Chrysalis.Codec.Types.CborLabel":
                case "global::Chrysalis.Codec.Types.CborLabel":
                    _ = sb.AppendLine($"{propertyName} = reader.GetCurrentDataItemType() switch");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine("    CborDataItemType.Unsigned or CborDataItemType.Signed => new Chrysalis.Codec.Types.CborLabel(reader.ReadInt64()),");
                    _ = sb.AppendLine("    CborDataItemType.String => new Chrysalis.Codec.Types.CborLabel(reader.ReadString()),");
                    _ = sb.AppendLine($"    _ => throw CborError.Decode(data, data.Length - reader.Buffer.Length, \"{typePath ?? propertyName}\", \"integer or text string\")");
                    _ = sb.AppendLine("};");
                    break;
                default:
                    break;
            }

            return sb;
        }

        // A failed TryReadSemanticTag can cache the non-tag header and move Buffer past it.
        private static string HasTag(string buffer) => $"!{buffer}.IsEmpty && ({buffer}[0] >> 5) == 6";

        private static StringBuilder EmitContentBuffer(StringBuilder sb, string reader, string buffer)
        {
            _ = sb.AppendLine($"var {buffer} = {reader}.Buffer;");
            _ = sb.AppendLine($"if ({HasTag(buffer)})");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"var _tagProbe = {reader};");
            _ = sb.AppendLine("try");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("_ = _tagProbe.TryReadSemanticTag(out _);");
            _ = sb.AppendLine($"{buffer} = _tagProbe.Buffer;");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("catch (global::SAIB.Cbor.CborException)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("// Leave malformed tag prefixes to the real reader or the original shape guard.");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("}");
            return sb;
        }

        private static StringBuilder EmitByteMemoryRead(StringBuilder sb, string reader, string data, string assignment, bool encoded = false)
        {
            string Wrap(string memory) => encoded ? $"new {CborEncodeValueFullName}({memory})" : memory;
            _ = sb.AppendLine("{");
            if (encoded)
            {
                // A concrete eager encoded field accepts one optional tag in addition to the one SAIB skips.
                _ = sb.AppendLine($"if ({HasTag($"{reader}.Buffer")}) _ = {reader}.TryReadSemanticTag(out _);");
            }
            // Probe on a copy: don't extend the set of tag prefixes the real reader accepts.
            _ = EmitContentBuffer(sb, reader, "_content");
            _ = sb.AppendLine("if (!_content.IsEmpty && _content[0] == 0x5F)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"{assignment} {Wrap($"(ReadOnlyMemory<byte>){reader}.ReadByteStringToArray()")};");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("else");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"ReadOnlySpan<byte> _bs = {reader}.ReadByteString();");
            _ = sb.AppendLine($"int _after = {data}.Length - {reader}.Buffer.Length;");
            _ = sb.AppendLine($"{assignment} {Wrap($"{data}.Slice(_after - _bs.Length, _bs.Length)")};");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("}");
            return sb;
        }

        public static StringBuilder EmitObjectPropertyReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, string typePath, string errorStart, string errorSlice, string? errorSlot)
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
                    _ = sb.AppendLine($"    throw CborError.Decode(data, {errorStart}, \"{typePath}\", \"indefinite array\", \"definite array\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine($"if ({propertyName}IsIndefinite)");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, {errorStart}, \"{typePath}\", \"definite array\", \"indefinite array\");");
                }
                _ = sb.AppendLine($"int {propertyName}ArrayRemaining = {propertyName}ArraySize;");
                _ = sb.AppendLine($"while ({propertyName}IsIndefinite ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : {propertyName}ArrayRemaining > 0)");
                _ = sb.AppendLine("{");

                _ = sb.AppendLine($"{errorStart} = data.Length - reader.Buffer.Length;");
                bool slicedListItemTypeFullName = metadata.IsListItemTypeOpenGeneric || !IsPrimitiveType(metadata.ListItemTypeFullName);
                _ = sb.AppendLine($"{errorSlice} = {(slicedListItemTypeFullName ? "true" : "false")};");
                if (errorSlot is not null)
                {
                    _ = sb.AppendLine($"{errorSlot} = 1;");
                }
                if (metadata.IsListItemTypeOpenGeneric)
                {
                    _ = EmitGenericWithTypeParamsReader(sb, metadata.ListItemType, $"{propertyName}TempItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.ListItemTypeFullName))
                    {
                        _ = EmitPrimitivePropertyReader(sb, metadata.ListItemTypeFullName, $"{propertyName}TempItem", typePath);
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
                _ = sb.AppendLine($"    Chrysalis.Codec.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
                _ = sb.AppendLine("}");

                return sb;
            }

            if (metadata.IsMap)
            {
                if (metadata.MapKeyTypeFullName is null || metadata.MapValueTypeFullName is null)
                {
                    throw new InvalidOperationException($"Map key or value type is null for property {metadata.PropertyName}");
                }

                _ = sb.AppendLine($"var {propertyName}TempMap = {MapInitializer(metadata, typePath)};");
                _ = sb.AppendLine($"{metadata.MapKeyTypeFullName} {propertyName}TempKeyItem = default;");
                _ = sb.AppendLine($"{metadata.MapValueTypeFullName} {propertyName}TempValueItem = default;");
                _ = sb.AppendLine($"reader.ReadBeginMap();");
                _ = sb.AppendLine($"int {propertyName}MapSize = reader.ReadSize();");
                _ = sb.AppendLine($"bool {propertyName}MapIsIndefinite = {propertyName}MapSize == -1;");

                if (metadata.IsIndefinite)
                {
                    _ = sb.AppendLine($"if (!{propertyName}MapIsIndefinite)");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, {errorStart}, \"{typePath}\", \"indefinite map\", \"definite map\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite)");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, {errorStart}, \"{typePath}\", \"definite map\", \"indefinite map\");");
                }
                _ = sb.AppendLine($"int {propertyName}MapRemaining = {propertyName}MapSize;");
                _ = sb.AppendLine($"while ({propertyName}MapIsIndefinite ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : {propertyName}MapRemaining > 0)");
                _ = sb.AppendLine("{");

                _ = sb.AppendLine($"{errorStart} = data.Length - reader.Buffer.Length;");
                bool slicedMapKeyTypeFullName = metadata.IsMapKeyTypeOpenGeneric || !IsPrimitiveType(metadata.MapKeyTypeFullName);
                _ = sb.AppendLine($"{errorSlice} = {(slicedMapKeyTypeFullName ? "true" : "false")};");
                if (errorSlot is not null)
                {
                    _ = sb.AppendLine($"{errorSlot} = 1;");
                }
                if (metadata.IsMapKeyTypeOpenGeneric)
                {
                    _ = EmitGenericWithTypeParamsReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapKeyTypeFullName))
                    {
                        _ = EmitPrimitivePropertyReader(sb, metadata.MapKeyTypeFullName, $"{propertyName}TempKeyItem", typePath);
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

                _ = sb.AppendLine($"{errorStart} = data.Length - reader.Buffer.Length;");
                bool slicedMapValueTypeFullName = metadata.IsMapValueTypeOpenGeneric || !IsPrimitiveType(metadata.MapValueTypeFullName);
                _ = sb.AppendLine($"{errorSlice} = {(slicedMapValueTypeFullName ? "true" : "false")};");
                if (errorSlot is not null)
                {
                    _ = sb.AppendLine($"{errorSlot} = 2;");
                }
                if (metadata.IsMapValueTypeOpenGeneric)
                {
                    _ = EmitGenericWithTypeParamsReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem");
                }
                else
                {
                    if (IsPrimitiveType(metadata.MapValueTypeFullName))
                    {
                        _ = EmitPrimitivePropertyReader(sb, metadata.MapValueTypeFullName, $"{propertyName}TempValueItem", typePath);
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

                if (metadata.IsPairList)
                {
                    _ = sb.AppendLine($"{propertyName}TempMap.Add(new({propertyName}TempKeyItem, {propertyName}TempValueItem));");
                }
                else if (IsLedgerMap(typePath))
                {
                    _ = sb.AppendLine($"{propertyName}TempMap[{propertyName}TempKeyItem] = {propertyName}TempValueItem;");
                }
                else
                {
                    _ = sb.AppendLine($"_ = {propertyName}TempMap.TryAdd({propertyName}TempKeyItem, {propertyName}TempValueItem);");
                }
                _ = sb.AppendLine($"if ({propertyName}MapSize > 0) {propertyName}MapRemaining--;");

                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
                _ = sb.AppendLine($"{propertyName} = {propertyName}TempMap;");
                _ = sb.AppendLine($"if ({propertyName}MapIsIndefinite)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    Chrysalis.Codec.Serialization.IndefiniteStateTracker.SetIndefinite({propertyName});");
                _ = sb.AppendLine("}");

                return sb;
            }

            _ = sb.AppendLine($"{errorSlice} = true;");
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
        private static string? GetCborMajorTypeGuard(SerializablePropertyMetadata prop, string owningType, string buffer)
        {
            string cleanType = prop.PropertyType.Replace("?", "");
            string fullType = prop.PropertyTypeFullName.Replace("?", "");
            (string? condition, string? kind) = cleanType switch
            {
                "int" or "long" => ($"({buffer}[0] >> 5) is not (0 or 1)", "integer"),
                "uint" or "ulong" => ($"({buffer}[0] >> 5) != 0", "unsigned integer"),
                "string" => ($"({buffer}[0] >> 5) != 3", "text string"),
                "bool" => ($"{buffer}[0] is not (0xF4 or 0xF5)", "boolean"),
                "float" => ($"{buffer}[0] != 0xFA", "floating point"),
                "double" => ($"{buffer}[0] != 0xFB", "floating point"),
                "byte[]" => ($"({buffer}[0] >> 5) != 2", "byte string"),
                _ when prop.IsPairList => ($"({buffer}[0] >> 5) != 5", "map"),
                _ when cleanType.Contains("Dictionary<") => ($"({buffer}[0] >> 5) != 5", KnownCollectionKind(fullType, "Dictionary", "map")),
                _ when cleanType.Contains("List<") || cleanType.Contains("ICborMaybeIndefList<") =>
                    ($"({buffer}[0] >> 5) is not (4 or 6)", fullType.Contains("Chrysalis.Codec.Types.ICborMaybeIndefList<") ? "array" : KnownCollectionKind(fullType, "List", "array")),
                _ => (null, null)
            };
            string expected = kind is null ? $"typeof({fullType})" : $"\"{kind}\"";
            return condition is null
                ? null
                : $"if ({buffer}.IsEmpty || {condition}) throw CborError.Decode(data, data.Length - reader.Buffer.Length, \"{owningType}.{prop.PropertyName}\", {expected});";

            static string? KnownCollectionKind(string type, string definition, string kind) =>
                (type.StartsWith($"global::System.Collections.Generic.{definition}<", StringComparison.Ordinal)
                    || type.StartsWith($"System.Collections.Generic.{definition}<", StringComparison.Ordinal)) ? kind : null;
        }

        public static StringBuilder EmitCborReaderInstance(StringBuilder sb, string dataName)
        {
            _ = sb.AppendLine($"var reader = new CborReader({dataName}.Span);");
            return sb;
        }

        public static StringBuilder EmitTagReader(StringBuilder sb, int? tag, string propertyName, string typePath)
        {
            if (tag.HasValue)
            {
                if (tag.Value >= 0)
                {
                    _ = sb.AppendLine($"int _{propertyName}Start = data.Length - reader.Buffer.Length;");
                    _ = sb.AppendLine($"if (!reader.TryReadSemanticTag(out ulong _{propertyName}Raw))");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, _{propertyName}Start, \"{typePath}\", \"tag {tag}\");");
                    _ = sb.AppendLine($"var {propertyName} = (int)_{propertyName}Raw;");
                    _ = sb.AppendLine($"if (_{propertyName}Raw != {tag}) throw CborError.DecodeTag(\"{typePath}\", \"tag {tag}\", _{propertyName}Raw);");
                }
                else
                {
                    _ = sb.AppendLine($"if (!reader.TryReadSemanticTag(out ulong _{propertyName}Raw))");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, 0, \"{typePath}.{propertyName}\", \"semantic tag\");");
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
                _ = sb.AppendLine($"if (!validator.Validate({propertyName})) throw __CborValidationError(data.Length - reader.Buffer.Length);");
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

        public static StringBuilder EmitMapValueReader(StringBuilder sb, SerializablePropertyMetadata prop, string typePath)
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
                _ = EmitPrimitivePropertyReader(sb, prop.PropertyType, tempVar, $"{typePath}.{prop.PropertyName}");
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

        public static StringBuilder EmitPrimitiveOrObjectReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName, string typePath, string errorStart, string errorSlice, string? errorSlot)
        {
            string cleanPropertyType = metadata.PropertyType.Replace("?", "");
            if (metadata.IsOpenGeneric)
            {
                _ = sb.AppendLine($"{errorSlice} = true;");
                return EmitGenericWithTypeParamsReader(sb, metadata.PropertyType, propertyName);
            }
            return IsPrimitiveType(cleanPropertyType)
                ? EmitPrimitivePropertyReader(sb, metadata.PropertyType, propertyName, typePath)
                : EmitObjectPropertyReader(sb, metadata, propertyName, typePath, errorStart, errorSlice, errorSlot);
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

                if (metadata.IsIndefinite && metadata.SerializationType is not (SerializationType.Constr or SerializationType.List))
                {
                    _ = sb.AppendLine("if (!isIndefiniteArray)");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"indefinite array\", \"definite array\");");
                }
                else if (metadata.IsDefinite)
                {
                    _ = sb.AppendLine("if (isIndefiniteArray)");
                    _ = sb.AppendLine($"    throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"definite array\", \"indefinite array\");");
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
                _ = EmitSerializablePropertyReader(sb, prop, propName, metadata.FullyQualifiedName, true, shouldTrackFields, fieldIndex, $"{metadata.BaseIdentifier}ArrayRemaining");
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
                    _ = sb.AppendLine($"    throw CborError.Decode(data, data.Length - reader.Buffer.Length, \"{metadata.FullyQualifiedName}.{requiredProp.PropertyName}\", \"required field\", \"missing\");");
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
