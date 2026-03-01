using System.Globalization;
using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class UnionEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            // Special-case: CborMaybeIndefList<T> — probe by tag and definite/indefinite
            if (IsCborMaybeIndefListUnion(metadata))
            {
                return EmitMaybeIndefListProbeReader(sb, metadata);
            }

            // General structural probe: children have distinct CBOR major types or tags
            if (TryEmitStructuralProbeReader(sb, metadata))
            {
                return sb;
            }

            // Fallback: existing try-catch approach
            return EmitTryCatchReader(sb, metadata);
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = sb.AppendLine("switch (data.CborTypeName)");
            _ = sb.AppendLine("{");
            foreach (SerializableTypeMetadata childType in metadata.ChildTypes)
            {
                _ = sb.AppendLine($"case \"{childType.FullyQualifiedName}\":");
                _ = sb.AppendLine($"{childType.FullyQualifiedName}.Write(output, ({childType.FullyQualifiedName})data);");
                _ = sb.AppendLine($"break;");
            }
            _ = sb.AppendLine($"default:");
            _ = sb.AppendLine($"throw new Exception(\"Union serialization failed. {metadata.FullyQualifiedName} \");");
            _ = sb.AppendLine("}");
            return sb;
        }

        private static bool IsCborMaybeIndefListUnion(SerializableTypeMetadata metadata)
        {
            return metadata.FullyQualifiedName.Contains("CborMaybeIndefList");
        }

        /// <summary>
        /// Emits a probe-based reader for CborMaybeIndefList that checks tag presence
        /// and definite/indefinite encoding to determine the concrete variant without try-catch.
        /// </summary>
        private static StringBuilder EmitMaybeIndefListProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            string typeParams = metadata.TypeParams ?? "<T>";
            string typeParam = typeParams.TrimStart('<').TrimEnd('>');

            string defListType = $"Chrysalis.Cbor.Types.CborDefList{typeParams}";
            string indefListType = $"Chrysalis.Cbor.Types.CborIndefList{typeParams}";
            string defListWithTagType = $"Chrysalis.Cbor.Types.CborDefListWithTag{typeParams}";
            string indefListWithTagType = $"Chrysalis.Cbor.Types.CborIndefListWithTag{typeParams}";

            _ = sb.AppendLine($"var reader = new CborReader(data.Span);");
            _ = sb.AppendLine($"bool hasTag258 = reader.TryReadSemanticTag(out _);");
            _ = sb.AppendLine($"reader.ReadBeginArray();");
            _ = sb.AppendLine($"int _arraySize = reader.ReadSize();");
            _ = sb.AppendLine($"bool isIndefinite = _arraySize == -1;");
            _ = sb.AppendLine($"int _remaining = _arraySize;");
            _ = sb.AppendLine($"List<{typeParam}> tempList = new();");
            _ = sb.AppendLine($"while (isIndefinite ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : _remaining > 0)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    int _pos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"    var _itemSpan = reader.ReadDataItem();");
            _ = sb.AppendLine($"    tempList.Add({Emitter.GenericSerializationUtilFullname}.Read<{typeParam}>(data.Slice(_pos, _itemSpan.Length)));");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"if (_arraySize > 0) _remaining--;");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    int _endPos = data.Length - reader.Buffer.Length;");
            _ = sb.AppendLine($"    if (isIndefinite && _endPos < data.Length && data.Span[_endPos] == 0xFF) _endPos++;");
            _ = sb.AppendLine($"    bytesConsumed = _endPos;");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} result;");
            _ = sb.AppendLine($"if (hasTag258)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    result = isIndefinite");
            _ = sb.AppendLine($"        ? new {indefListWithTagType}(tempList)");
            _ = sb.AppendLine($"        : new {defListWithTagType}(tempList);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"else");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    result = isIndefinite");
            _ = sb.AppendLine($"        ? new {indefListType}(tempList)");
            _ = sb.AppendLine($"        : new {defListType}(tempList);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"if (isIndefinite)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    result.IsIndefinite = true;");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"return result;");

            return sb;
        }

        /// <summary>
        /// Attempts to emit a structural probe reader. Returns true if successful, false if the union
        /// cannot be probed (children are not structurally distinguishable).
        /// </summary>
        private static bool TryEmitStructuralProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            // Flatten union children: expand intermediate unions to their leaf types
            List<SerializableTypeMetadata> children = FlattenUnionChildren(metadata.ChildTypes);
            if (children.Count < 2)
            {
                return false;
            }

            // Classify children by their CBOR structure probe key
            Dictionary<string, List<SerializableTypeMetadata>> probeGroups = [];

            foreach (SerializableTypeMetadata child in children)
            {
                string probeKey = GetProbeKey(child);
                if (!probeGroups.TryGetValue(probeKey, out List<SerializableTypeMetadata> group))
                {
                    group = [];
                    probeGroups[probeKey] = group;
                }
                group.Add(child);
            }

            // Reject if any child has unknown probe key or if groups have collisions
            if (probeGroups.ContainsKey("unknown") || probeGroups.Values.Any(g => g.Count > 1))
            {
                return false;
            }

            // All children are distinguishable — emit probe
            bool hasTagChildren = probeGroups.Keys.Any(k => k.StartsWith("tag:", StringComparison.Ordinal));
            bool hasConstrChildren = probeGroups.ContainsKey("constr");
            bool hasIdxChildren = probeGroups.Keys.Any(k => k.StartsWith("idx:", StringComparison.Ordinal));
            bool hasStateChildren = probeGroups.Keys.Any(k => !k.StartsWith("tag:", StringComparison.Ordinal) && !k.StartsWith("idx:", StringComparison.Ordinal) && k != "constr");

            if (hasIdxChildren && !hasTagChildren && !hasConstrChildren && !hasStateChildren)
            {
                EmitIdxOnlyBranch(sb, metadata, probeGroups);
            }
            else if ((hasTagChildren || hasConstrChildren) && (hasStateChildren || hasIdxChildren))
            {
                EmitTagBranchWithConstr(sb, metadata, probeGroups, hasConstrChildren);
                _ = sb.AppendLine($"var reader = new CborReader(data.Span);");
                _ = sb.AppendLine($"var state = reader.GetCurrentDataItemType();");
                EmitStateBranch(sb, metadata, probeGroups);
            }
            else if (hasTagChildren || hasConstrChildren)
            {
                EmitTagBranchWithConstr(sb, metadata, probeGroups, hasConstrChildren);
                _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected tag\");");
            }
            else
            {
                _ = sb.AppendLine($"var reader = new CborReader(data.Span);");
                _ = sb.AppendLine($"var state = reader.GetCurrentDataItemType();");
                EmitStateOnlyBranch(sb, metadata, probeGroups);
            }

            return true;
        }

        private static void EmitTagBranchWithConstr(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool hasConstr)
        {
            _ = sb.AppendLine($"if (data.Length > 0 && (data.Span[0] >> 5) == 6)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    var _tagReader = new CborReader(data.Span);");
            _ = sb.AppendLine($"    _tagReader.TryReadSemanticTag(out ulong _tagVal);");
            _ = sb.AppendLine($"    var tag = (int)_tagVal;");

            // Use local variable + assignment to propagate bytesConsumed
            _ = sb.AppendLine($"    {metadata.FullyQualifiedName} _tagResult = tag switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {tagValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            if (hasConstr && probeGroups.TryGetValue("constr", out List<SerializableTypeMetadata> constrGroup))
            {
                // Constr types use semantic tags (121-127, 102, etc.) — catch all unmatched tags
                _ = sb.AppendLine($"        _ => ({metadata.FullyQualifiedName}){constrGroup[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            else
            {
                _ = sb.AppendLine($"        _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected tag\")");
            }
            _ = sb.AppendLine("    };");
            _ = sb.AppendLine($"    return _tagResult;");
            _ = sb.AppendLine("}");
        }

        private static void EmitStateBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _stateResult = state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => !g.Key.StartsWith("tag:", StringComparison.Ordinal) && g.Key != "constr"))
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected state \" + state)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _stateResult;");
        }

        private static void EmitIdxOnlyBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"var _idxReader = new CborReader(data.Span);");
            _ = sb.AppendLine($"_idxReader.ReadBeginArray();");
            _ = sb.AppendLine($"_idxReader.ReadSize();");
            _ = sb.AppendLine($"int _idx = _idxReader.ReadInt32();");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _idxResult = _idx switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                int idxValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"    {idxValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected idx \" + _idx)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _idxResult;");
        }

        private static void EmitStateOnlyBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _stateResult = state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected state \" + state)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _stateResult;");
        }

        /// <summary>
        /// Recursively expands intermediate union types to their leaf (non-union) children.
        /// This allows probe-based dispatch to see through union hierarchies like
        /// PlutusData → PlutusBigInt → PlutusInt → PlutusInt64/PlutusUint64.
        /// </summary>
        private static List<SerializableTypeMetadata> FlattenUnionChildren(List<SerializableTypeMetadata> children)
        {
            Dictionary<string, SerializableTypeMetadata> seen = [];
            FlattenUnionChildrenRec(children, seen);
            return [.. seen.Values];
        }

        private static void FlattenUnionChildrenRec(List<SerializableTypeMetadata> children, Dictionary<string, SerializableTypeMetadata> seen)
        {
            foreach (SerializableTypeMetadata child in children)
            {
                if (child.SerializationType == SerializationType.Union && child.ChildTypes.Count > 0)
                {
                    FlattenUnionChildrenRec(child.ChildTypes, seen);
                }
                else if (!seen.ContainsKey(child.FullyQualifiedName))
                {
                    seen[child.FullyQualifiedName] = child;
                }
            }
        }

        private static string GetProbeKey(SerializableTypeMetadata child)
        {
            if (child.SerializationType == SerializationType.Constr && child.CborIndex is not null && child.CborIndex >= 0)
            {
                int tag = Emitter.ResolveTag(child.CborIndex);
                return $"tag:{tag}";
            }

            return child.CborTag is not null
                ? $"tag:{child.CborTag}"
                : child.SerializationType switch
                {
                    SerializationType.List => GetListProbeKey(child),
                    SerializationType.Map => "map",
                    SerializationType.Constr => "constr",
                    SerializationType.Container => GetContainerProbeKey(child),
                    SerializationType.Union => "unknown",
                    _ => "unknown"
                };
        }

        private static string GetListProbeKey(SerializableTypeMetadata child)
        {
            // [CborIndex(N)] on a [CborList] type provides an explicit discriminant
            if (child.CborIndex.HasValue && child.CborIndex.Value >= 0)
            {
                return $"idx:{child.CborIndex.Value}";
            }

            if (child.Properties.Count > 0)
            {
                string firstPropType = child.Properties[0].PropertyTypeFullName;
                int lastDot = firstPropType.LastIndexOf('.');
                string typeName = lastDot >= 0 ? firstPropType.Substring(lastDot + 1) : firstPropType;
                if (typeName.StartsWith("Value", StringComparison.Ordinal) && typeName.Length > 5
                    && int.TryParse(typeName.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out int idx))
                {
                    return $"idx:{idx}";
                }
            }

            return "array";
        }

        private static string GetContainerProbeKey(SerializableTypeMetadata child)
        {
            if (child.Properties.Count == 1)
            {
                string propType = child.Properties[0].PropertyTypeFullName.Replace("?", "");
                if (propType is "long" or "int")
                {
                    return "signed";
                }
                if (propType is "ulong" or "uint")
                {
                    return "unsigned";
                }
                if (propType is "string")
                {
                    return "text";
                }
                if (propType is "byte[]" or "ReadOnlyMemory<byte>" or "System.ReadOnlyMemory<byte>" or "global::System.ReadOnlyMemory<byte>")
                {
                    return "bytes";
                }
                if (propType is "bool")
                {
                    return "boolean";
                }
                if (propType.Contains("Dictionary<"))
                {
                    return "map";
                }
                if (propType.Contains("List<") || propType.Contains("CborMaybeIndefList<")
                    || propType.Contains("CborDefList<") || propType.Contains("CborIndefList<"))
                {
                    return "array";
                }
            }
            return "unknown";
        }

        private static string GetCborReaderStatePattern(string probeKey)
        {
            return probeKey switch
            {
                "array" => "CborDataItemType.Array",
                "map" => "CborDataItemType.Map",
                "integer" => "CborDataItemType.Unsigned or CborDataItemType.Signed",
                "signed" => "CborDataItemType.Signed",
                "unsigned" => "CborDataItemType.Unsigned",
                "text" => "CborDataItemType.String",
                "bytes" => "CborDataItemType.ByteString",
                "boolean" => "CborDataItemType.Boolean",
                _ => throw new InvalidOperationException($"Unexpected probe key: {probeKey}")
            };
        }

        private static StringBuilder EmitTryCatchReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = sb.AppendLine($"List<string> errors = [];");
            foreach (SerializableTypeMetadata childType in metadata.ChildTypes)
            {
                _ = sb.AppendLine($"try");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"return ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read(data, out bytesConsumed);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"catch (Exception ex)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"errors.Add(ex.Message);");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName} \" + string.Join(\"\\n\", errors));");

            return sb;
        }
    }
}
