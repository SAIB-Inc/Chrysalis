using System.Globalization;
using System.Text;

namespace Chrysalis.Codec.V2.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class UnionEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (IsCborMaybeIndefListUnion(metadata))
            {
                return EmitMaybeIndefListProbeReader(sb, metadata);
            }

            if (TryEmitStructuralProbeReader(sb, metadata))
            {
                return sb;
            }

            return EmitTryCatchReader(sb, metadata);
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            // V2: Flatten union hierarchy to concrete leaf types and use type pattern matching.
            // Derived types must appear before base types to avoid unreachable switch cases.
            List<SerializableTypeMetadata> leafTypes = FlattenUnionChildren(metadata.ChildTypes);

            _ = sb.AppendLine("switch (data)");
            _ = sb.AppendLine("{");
            foreach (SerializableTypeMetadata childType in leafTypes)
            {
                string varName = $"_typed{childType.BaseIdentifier}";
                _ = sb.AppendLine($"case {childType.FullyQualifiedName} {varName}:");
                _ = sb.AppendLine($"{childType.FullyQualifiedName}.Write(output, {varName});");
                _ = sb.AppendLine($"break;");
            }
            _ = sb.AppendLine($"default:");
            _ = sb.AppendLine($"throw new Exception(\"Union serialization failed. {metadata.FullyQualifiedName} \");");
            _ = sb.AppendLine("}");
            return sb;
        }

        private static bool IsCborMaybeIndefListUnion(SerializableTypeMetadata metadata)
        {
            return metadata.FullyQualifiedName.Contains("ICborMaybeIndefList");
        }

        private static StringBuilder EmitMaybeIndefListProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            string typeParams = metadata.TypeParams ?? "<T>";

            string defListType = $"Chrysalis.Codec.V2.Types.CborDefList{typeParams}";
            string indefListType = $"Chrysalis.Codec.V2.Types.CborIndefList{typeParams}";
            string defListWithTagType = $"Chrysalis.Codec.V2.Types.CborDefListWithTag{typeParams}";
            string indefListWithTagType = $"Chrysalis.Codec.V2.Types.CborIndefListWithTag{typeParams}";

            _ = sb.AppendLine("var reader = new CborReader(data.Span);");
            _ = sb.AppendLine("bool hasTag258 = reader.TryReadSemanticTag(out _);");
            _ = sb.AppendLine("reader.ReadBeginArray();");
            _ = sb.AppendLine("int _arraySize = reader.ReadSize();");
            _ = sb.AppendLine("bool isIndefinite = _arraySize == -1;");

            _ = sb.AppendLine($"{metadata.FullyQualifiedName} result;");
            _ = sb.AppendLine("if (hasTag258)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    result = isIndefinite");
            _ = sb.AppendLine($"        ? ({metadata.FullyQualifiedName}){indefListWithTagType}.Read(data, out bytesConsumed)");
            _ = sb.AppendLine($"        : ({metadata.FullyQualifiedName}){defListWithTagType}.Read(data, out bytesConsumed);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("else");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    result = isIndefinite");
            _ = sb.AppendLine($"        ? ({metadata.FullyQualifiedName}){indefListType}.Read(data, out bytesConsumed)");
            _ = sb.AppendLine($"        : ({metadata.FullyQualifiedName}){defListType}.Read(data, out bytesConsumed);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("return result;");

            return sb;
        }

        private static bool TryEmitStructuralProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            List<SerializableTypeMetadata> children = FlattenUnionChildren(metadata.ChildTypes);
            if (children.Count < 2)
            {
                return false;
            }

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

            if (probeGroups.ContainsKey("unknown") || probeGroups.Values.Any(g => g.Count > 1))
            {
                return false;
            }

            bool hasTagChildren = probeGroups.Keys.Any(k => k.StartsWith("tag:", StringComparison.Ordinal));
            bool hasConstrChildren = probeGroups.ContainsKey("constr");
            bool hasIdxChildren = probeGroups.Keys.Any(k => k.StartsWith("idx:", StringComparison.Ordinal));
            bool hasArraySizeChildren = probeGroups.Keys.Any(k => k.StartsWith("array:", StringComparison.Ordinal));
            bool hasStateChildren = probeGroups.Keys.Any(k => !k.StartsWith("tag:", StringComparison.Ordinal) && !k.StartsWith("idx:", StringComparison.Ordinal) && !k.StartsWith("array:", StringComparison.Ordinal) && k != "constr");

            if (hasArraySizeChildren && !hasTagChildren && !hasConstrChildren && !hasIdxChildren && !hasStateChildren)
            {
                EmitArraySizeBranch(sb, metadata, probeGroups);
            }
            else if (hasIdxChildren && !hasTagChildren && !hasConstrChildren && !hasStateChildren && !hasArraySizeChildren)
            {
                EmitIdxOnlyBranch(sb, metadata, probeGroups);
            }
            else if ((hasTagChildren || hasConstrChildren) && (hasStateChildren || hasIdxChildren || hasArraySizeChildren))
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

            _ = sb.AppendLine($"    {metadata.FullyQualifiedName} _tagResult = tag switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {tagValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            if (hasConstr && probeGroups.TryGetValue("constr", out List<SerializableTypeMetadata> constrGroup))
            {
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

        private static void EmitArraySizeBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"var _sizeReader = new CborReader(data.Span);");
            _ = sb.AppendLine($"_sizeReader.ReadBeginArray();");
            _ = sb.AppendLine($"int _size = _sizeReader.ReadSize();");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _sizeResult = _size switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                int sizeValue = int.Parse(group.Key.Substring(6), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"    {sizeValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected array size \" + _size)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _sizeResult;");
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
            if (child.CborIndex.HasValue && child.CborIndex.Value >= 0)
            {
                return $"idx:{child.CborIndex.Value}";
            }

            return $"array:{child.Properties.Count}";
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
                if (propType.Contains("List<") || propType.Contains("ICborMaybeIndefList<")
                    || propType.Contains("CborDefList<") || propType.Contains("CborIndefList<"))
                {
                    return "array";
                }
            }
            return "unknown";
        }

        private static string GetCborReaderStatePattern(string probeKey)
        {
            return probeKey.StartsWith("array:", StringComparison.Ordinal)
                ? "CborDataItemType.Array"
                : probeKey switch
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
