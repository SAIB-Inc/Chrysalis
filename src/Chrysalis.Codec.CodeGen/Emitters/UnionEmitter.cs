using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class UnionEmitter(ImmutableDictionary<string, SerializableTypeMetadata>? typeRegistry) : ICborSerializerEmitter
    {
        private const int MaxNestedTupleDepth = 4;

        public DiagnosticDescriptor? ReadDiagnostic { get; private set; }

        public StringBuilder? StructHelpers { get; private set; }
        public static EquatableArray<SerializableTypeMetadata> GetShapeDependencies(SerializableTypeMetadata union,
            ImmutableDictionary<string, SerializableTypeMetadata> registry)
        {
            List<SerializableTypeMetadata> leaves = GetUnionLeaves(union);
            if (leaves.Count < 2)
            {
                return default;
            }

            // The structural probe looks up leaf fields. The fallback tuple probe can do the
            // same inside untagged nested lists. Walk breadth-first so each tuple is expanded
            // once, at the shallowest depth at which the reader could consult its fields.
            bool probeNested = leaves.All(leaf => leaf.SerializationType == SerializationType.List && leaf.CborTag is null);
            Queue<(SerializableTypeMetadata Model, int Depth)> pending = new(leaves.Select(leaf => (leaf, 0)));
            Dictionary<string, SerializableTypeMetadata> dependencies = [];
            while (pending.Count != 0)
            {
                (SerializableTypeMetadata tuple, int depth) = pending.Dequeue();
                foreach (SerializablePropertyMetadata property in tuple.Properties)
                {
                    string name = NormalizeTypeName(property.PropertyTypeFullName);
                    if (!registry.TryGetValue(name, out SerializableTypeMetadata? model) || model is null || ClassifyTypeShape(model) == "unknown"
                        || dependencies.ContainsKey(name))
                    {
                        continue;
                    }
                    dependencies.Add(name, model);
                    if (probeNested && depth < MaxNestedTupleDepth && IsProbeableTuple(model))
                    {
                        pending.Enqueue((model, depth + 1));
                    }
                }
            }
            return new(dependencies.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Value));
        }

        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (EmitBuiltInReader(sb, metadata) || TryEmitStructuralProbeReader(sb, metadata))
            {
                return sb;
            }

            if (TryEmitCandidateShapes(sb, metadata))
            {
                return sb;
            }

            ReadDiagnostic = UnknownUnionShape;
            return sb.AppendLine(AmbiguityError(metadata, GetUnionLeaves(metadata)));
        }

        private static string AmbiguityError(SerializableTypeMetadata union, List<SerializableTypeMetadata> leaves)
        {
            string alternatives = string.Join(", ", leaves.Select(leaf => leaf.BaseIdentifier));
            string help = union.FullyQualifiedName switch
            {
                "Chrysalis.Codec.Types.Cardano.Core.IBlock" => "; use BlockWithEra.Read or a concrete block reader",
                "Chrysalis.Codec.Types.Cardano.Core.Common.IMultiAsset" => "; use MultiAssetOutput.Read or MultiAssetMint.Read",
                "Chrysalis.Codec.Types.Cardano.Core.Common.ITokenBundle" => "; use TokenBundleOutput.Read or TokenBundleMint.Read",
                _ => ""
            };
            return $"throw CborError.Decode(data, 0, \"{union.FullyQualifiedName}\", \"unambiguous union case among {alternatives}{help}\");";
        }

        private bool TryEmitCandidateShapes(StringBuilder sb, SerializableTypeMetadata metadata, bool bySize = false)
        {
            List<SerializableTypeMetadata> leaves = GetUnionLeaves(metadata);
            if (leaves.Count == 1)
            {
                _ = sb.AppendLine($"return {EmitCaseReadExpr(metadata, leaves[0], "data", "bytesConsumed", cast: false)};");
                return true;
            }
            if (leaves.Count == 0 || leaves.Any(leaf => leaf.SerializationType != SerializationType.List || leaf.CborTag is not null))
            {
                return false;
            }

            if (leaves.Where((leaf, index) => leaves.Take(index).Any(other => TupleShapesOverlap(leaf, other, 0))).Any())
            {
                ReadDiagnostic = OverlappingUnionShape;
            }

            _ = sb.AppendLine("int matched = -1;");
            _ = sb.AppendLine($"(int Remaining, string Path, string Expected) failure = (data.Length, \"{metadata.FullyQualifiedName}\", \"registered union shape\");");
            _ = sb.AppendLine("try {");
            if (bySize)
            {
                // Disjoint ranges leave a full-length tuple one possible case. Every other length,
                // indefinite included, and every non-array input still takes the walk below.
                _ = sb.AppendLine("if (!data.IsEmpty && (data.Span[0] >> 5) == 4)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("var _sizeReader = new CborReader(data.Span);");
                _ = sb.AppendLine("_sizeReader.ReadBeginArray();");
                _ = sb.AppendLine("switch (_sizeReader.ReadSize())");
                _ = sb.AppendLine("{");
                foreach (SerializableTypeMetadata leaf in leaves)
                {
                    _ = sb.AppendLine($"case {ItemCountRange(leaf).Maximum}: return {EmitCaseReadExpr(metadata, leaf, "data", "bytesConsumed", cast: false)};");
                }
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("}");
            }
            string matchName = metadata.IsStructUnion ? "__Matches" : "_Matches";
            _ = sb.AppendLine("try {");
            for (int i = 0; i < leaves.Count; i++)
            {
                _ = sb.AppendLine($"if ({matchName}{i}(new CborReader(data.Span), ref failure))");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    if (matched >= 0) {AmbiguityError(metadata, leaves)}");
                _ = sb.AppendLine($"    matched = {i};");
                _ = sb.AppendLine("}");
            }
            _ = sb.AppendLine("} catch (global::SAIB.Cbor.CborException error) {");
            _ = sb.AppendLine("throw CborError.Wrap(error, data, failure.Path, failure.Expected);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("return matched switch");
            _ = sb.AppendLine("{");
            for (int i = 0; i < leaves.Count; i++)
            {
                _ = sb.AppendLine($"    {i} => {EmitCaseReadExpr(metadata, leaves[i], "data", "bytesConsumed", cast: false)},");
            }
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, data.Length - failure.Remaining, failure.Path, failure.Expected)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine("} catch (Exception error) when (error is CborDecodeException or global::SAIB.Cbor.CborException) {");
            _ = sb.AppendLine($"throw CborError.Wrap(error, data, \"{metadata.FullyQualifiedName}\", \"registered union shape\");");
            _ = sb.AppendLine("}");

            StringBuilder target = metadata.IsStructUnion ? (StructHelpers ??= new()) : sb;
            string modifiers = metadata.IsStructUnion ? "private static " : "static ";
            for (int i = 0; i < leaves.Count; i++)
            {
                _ = target.AppendLine($"{modifiers}bool {matchName}{i}(CborReader reader, ref (int Remaining, string Path, string Expected) failure)");
                _ = target.AppendLine("{");
                int index = 0;
                EmitTupleShape(target, metadata.FullyQualifiedName, leaves[i], ref index, 0);
                _ = target.AppendLine("return true;");
                _ = target.AppendLine("}");
            }
            return true;
        }

        private bool TupleShapesOverlap(SerializableTypeMetadata first, SerializableTypeMetadata second, int depth)
        {
            if (first.CborIndex is >= 0 && second.CborIndex is >= 0 && first.CborIndex != second.CborIndex)
            {
                return false;
            }
            int common = Math.Max(ItemCountRange(first).Minimum, ItemCountRange(second).Minimum);
            if (common > Math.Min(first.Properties.Count, second.Properties.Count))
            {
                return false;
            }
            foreach ((SerializablePropertyMetadata a, SerializablePropertyMetadata b) in first.Properties.Zip(second.Properties, (a, b) => (a, b)).Take(common))
            {
                if (a.IsTypeNullable && b.IsTypeNullable)
                {
                    continue;
                }
                string left = TupleFieldShape(a);
                string right = TupleFieldShape(b);
                if (left != "unknown" && right != "unknown" && left != right)
                {
                    return false;
                }
                if (FindNestedTuple(a, depth) is { } x && FindNestedTuple(b, depth) is { } y && !TupleShapesOverlap(x, y, depth + 1))
                {
                    return false;
                }
            }
            return true;
        }

        private string TupleFieldShape(SerializablePropertyMetadata property) => NormalizeTypeName(property.PropertyTypeFullName) == "Chrysalis.Codec.Types.CborEncodedValue"
            ? "unknown" : ClassifyPropertyShape(property);

        private SerializableTypeMetadata? FindNestedTuple(SerializablePropertyMetadata prop, int depth) =>
            depth < MaxNestedTupleDepth && typeRegistry is not null
            && typeRegistry.TryGetValue(NormalizeTypeName(prop.PropertyTypeFullName), out SerializableTypeMetadata? nested)
            && IsProbeableTuple(nested) ? nested : null;

        // The item counts a tuple's shape walk accepts: its required fields up to all of them.
        private static (int Minimum, int Maximum) ItemCountRange(SerializableTypeMetadata tuple)
        {
            Emitter.TryGetListArityBounds(tuple, out int min, out int max);
            return (min, max);
        }

        private static bool IsProbeableTuple(SerializableTypeMetadata metadata) =>
            metadata is { SerializationType: SerializationType.List, CborTag: null } && metadata.Properties.All(p => !p.IsTypeNullable);

        private void EmitTupleShape(StringBuilder sb, string union, SerializableTypeMetadata tuple, ref int index, int depth)
        {
            string count = $"_size{index++}";
            string path = $"{union}.{NormalizeTypeName(tuple.FullyQualifiedName)}";
            _ = sb.AppendLine("if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) != 4) return false;");
            _ = sb.AppendLine("reader.ReadBeginArray();");
            _ = sb.AppendLine($"int {count} = reader.ReadSize();");
            (int minimum, int maximum) = ItemCountRange(tuple);
            _ = sb.AppendLine($"if ({count} != -1 && ({count} < {minimum} || {count} > {maximum})) return false;");
            int position = 0;
            foreach (SerializablePropertyMetadata prop in tuple.Properties)
            {
                if (prop.IsTypeNullable && position >= minimum)
                {
                    _ = sb.AppendLine($"if ({count} >= 0 && {count} <= {position}) return true;");
                    _ = sb.AppendLine($"if ({count} == -1 && !reader.Buffer.IsEmpty && reader.Buffer[0] == 0xFF) {{ reader.SkipDataItem(); return true; }}");
                }
                bool leadingIndex = position++ == 0 && tuple.CborIndex is >= 0;
                if (leadingIndex)
                {
                    _ = sb.AppendLine($"if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) != 0) return false;");
                    _ = sb.AppendLine($"var _idx{index} = reader;");
                    _ = sb.AppendLine($"if (_idx{index++}.ReadUInt64() != {tuple.CborIndex}) return false;");
                }
                if (!prop.IsTypeNullable && FindNestedTuple(prop, depth) is { } nested)
                {
                    EmitTupleShape(sb, union, nested, ref index, depth + 1);
                    continue;
                }
                string shape = TupleFieldShape(prop);
                bool? requiredIndefinite = !(prop.IsList || prop.IsMap) ? null : prop.IsIndefinite ? true : prop.IsDefinite ? false : null;
                string expected = requiredIndefinite is bool wantIndefinite
                    ? $"{(wantIndefinite ? "indefinite" : "definite")} {shape}"
                    : shape switch
                    {
                        "text" => "text string",
                        "bytes" => "byte string",
                        "unknown" => "CBOR item",
                        _ => shape
                    };
                _ = sb.AppendLine($"if (reader.Buffer.Length <= failure.Remaining) failure = (reader.Buffer.Length, \"{path}.{prop.PropertyName}\", \"{expected}\");");
                _ = sb.AppendLine("if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xFF) return false;");
                if (shape != "unknown")
                {
                    string pattern = GetCborReaderStatePattern(shape);
                    string nullable = prop.IsTypeNullable ? "reader.Buffer[0] is not (0xF6 or 0xF7) && " : "";
                    if (requiredIndefinite is bool indefinite)
                    {
                        string probe = $"_probe{index++}";
                        _ = sb.AppendLine($"var {probe} = reader;");
                        string check = $"{probe}.GetCurrentDataItemType() is not ({pattern}) || {probe}.ReadSize() {(indefinite ? "!=" : "==")} -1";
                        _ = sb.AppendLine($"if ({(prop.IsTypeNullable ? $"{nullable}({check})" : check)}) return false;");
                    }
                    else
                    {
                        _ = sb.AppendLine($"if ({nullable}reader.GetCurrentDataItemType() is not ({pattern})) return false;");
                    }
                }
                _ = sb.AppendLine("reader.SkipDataItem();");
            }
            _ = sb.AppendLine($"if ({count} == -1)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("if (reader.Buffer.IsEmpty || reader.Buffer[0] != 0xFF) return false;");
            _ = sb.AppendLine("reader.SkipDataItem();");
            _ = sb.AppendLine("}");
        }

        private static string? GetBuiltInReaderMethod(SerializableTypeMetadata metadata) => NormalizeTypeName(metadata.FullyQualifiedName) switch
        {
            "Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusData" => "PlutusWire.ReadData",
            "Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusBigInt" => "PlutusWire.ReadInteger",
            "Chrysalis.Codec.Types.Cardano.Core.Transaction.ITransactionBody" => "TransactionEraReader.ReadBody",
            "Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.ITransactionWitnessSet" => "global::Chrysalis.Codec.Types.Cardano.Core.Transaction.TransactionEraReader.ReadWitnesses",
            "Chrysalis.Codec.Types.Cardano.Core.Protocol.ProtocolParamUpdate" => "ProtocolUpdateWire.Read",
            "Chrysalis.Codec.Types.ICborPrimitive" => "PrimitiveWire.Read",
            _ => null
        };

        internal static bool HasBuiltInReader(SerializableTypeMetadata metadata) =>
            GetBuiltInReaderMethod(metadata) is not null
            || NormalizeTypeName(metadata.FullyQualifiedName) == "Chrysalis.Codec.Types.Cardano.Core.Transaction.ITransaction";

        internal static string? TryEmitBuiltInCaseValidationCall(
            SerializableTypeMetadata union, SerializableTypeMetadata caseMeta, string rawExpr, string unionPath)
        {
            if (NormalizeTypeName(union.FullyQualifiedName) != "Chrysalis.Codec.Types.Cardano.Core.Protocol.ProtocolParamUpdate")
            {
                return null;
            }

            string isBabbage = NormalizeTypeName(caseMeta.FullyQualifiedName) == "Chrysalis.Codec.Types.Cardano.Core.Protocol.BabbageProtocolParamUpdate"
                ? "true"
                : "false";
            return $"global::Chrysalis.Codec.Types.Cardano.Core.Protocol.ProtocolUpdateWire.ValidateConstructorBytes({rawExpr}, {isBabbage}, \"{unionPath}\");";
        }

        private static bool EmitBuiltInReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (GetBuiltInReaderMethod(metadata) is { } method)
            {
                _ = sb.AppendLine("try");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"return {method}(data, out bytesConsumed);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"throw CborError.Wrap(error, data, \"{metadata.FullyQualifiedName}\", \"registered union shape\");");
                _ = sb.AppendLine("}");
                return true;
            }

            if (NormalizeTypeName(metadata.FullyQualifiedName) == "Chrysalis.Codec.Types.Cardano.Core.Transaction.ITransaction")
            {
                _ = sb.AppendLine($"throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"explicit transaction era\");");
                return true;
            }

            return false;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = sb.AppendLine("ArgumentNullException.ThrowIfNull(data);");

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
            _ = sb.AppendLine($"throw CborError.Validation(-1, \"{metadata.FullyQualifiedName}\", \"registered union case\", data.GetType().FullName ?? \"unknown\");");
            _ = sb.AppendLine("}");
            return sb;
        }

        private bool TryEmitStructuralProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            List<SerializableTypeMetadata> children = GetUnionLeaves(metadata);
            if (children.Count < 2) { return false; }

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

            if (metadata.IsStructUnion && probeGroups.TryGetValue("signed", out List<SerializableTypeMetadata>? signedGroup) && !probeGroups.ContainsKey("unsigned"))
            {
                _ = probeGroups.Remove("signed");
                probeGroups["integer"] = signedGroup;
            }
            if (probeGroups.ContainsKey("unknown")) { return false; }

            bool hasTagChildren = probeGroups.Keys.Any(k => k.StartsWith("tag:", StringComparison.Ordinal));
            bool hasConstrChildren = probeGroups.ContainsKey("constr");
            bool hasIdxChildren = probeGroups.Keys.Any(k => k.StartsWith("idx:", StringComparison.Ordinal));
            bool hasArraySizeChildren = probeGroups.Keys.Any(k => k.StartsWith("array:", StringComparison.Ordinal));
            bool hasStateChildren = probeGroups.Keys.Any(k => !k.StartsWith("tag:", StringComparison.Ordinal) && !k.StartsWith("idx:", StringComparison.Ordinal) && !k.StartsWith("array:", StringComparison.Ordinal) && k != "constr");

            if ((hasArraySizeChildren && probeGroups.ContainsKey("array")) || (hasIdxChildren && (hasArraySizeChildren || probeGroups.ContainsKey("array"))))
            {
                return false;
            }
            if (probeGroups.Values.Any(g => g.Count > 1) && probeGroups.Where(g => g.Value.Count > 1)
                .Any(g => !g.Key.StartsWith("idx:", StringComparison.Ordinal) || FindSeparatingDepth(g.Value) < 0))
            {
                return false;
            }
            if (!metadata.IsStructUnion && hasArraySizeChildren && !hasTagChildren && !hasConstrChildren && !hasIdxChildren && !hasStateChildren
                && children.Any(child => child.Properties.Any(prop => prop.IsTypeNullable)))
            {
                List<(int Minimum, int Maximum)> ranges = [.. children.Select(ItemCountRange)];
                return !ranges.Where((range, i) => ranges.Take(i).Any(other => range.Minimum <= other.Maximum && other.Minimum <= range.Maximum)).Any()
                    && TryEmitCandidateShapes(sb, metadata, bySize: true);
            }

            List<SerializableTypeMetadata> arraySizeChildren = [.. children.Where(c => GetProbeKey(c).StartsWith("array:", StringComparison.Ordinal))];
            int arraySizeGroupCount = probeGroups.Keys.Count(k => k.StartsWith("array:", StringComparison.Ordinal));
            bool handleArraySize = hasArraySizeChildren && (metadata.IsStructUnion || arraySizeGroupCount > 1);
            if (handleArraySize)
            {
                List<(int Min, int Max)> bounds = [with(arraySizeChildren.Count)];
                foreach (SerializableTypeMetadata child in arraySizeChildren)
                {
                    Emitter.TryGetListArityBounds(child, out int min, out int max);
                    if (bounds.Any(b => min <= b.Max && b.Min <= max)) { return false; }
                    bounds.Add((min, max));
                }
            }

            if (hasTagChildren || hasConstrChildren) { EmitTagBranchWithConstr(sb, metadata, probeGroups, hasConstrChildren); }
            else if (metadata.IsStructUnion)
            {
                _ = sb.AppendLine($"if (data.Length > 0 && (data.Span[0] >> 5) == 6) throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"untagged union case\", CborError.Actual(data, 0));");
            }

            if (handleArraySize && !hasTagChildren && !hasConstrChildren && !hasIdxChildren && !hasStateChildren)
            {
                EmitArraySizeBranch(sb, metadata, arraySizeChildren);
            }
            else if (hasIdxChildren && !hasTagChildren && !hasConstrChildren && !hasStateChildren && !hasArraySizeChildren)
            {
                EmitIdxBranchWithNesting(sb, metadata, probeGroups);
            }
            else if (hasIdxChildren || handleArraySize)
            {
                _ = sb.AppendLine("var reader = new CborReader(data.Span);");
                _ = sb.AppendLine("var state = reader.GetCurrentDataItemType();");
                _ = sb.AppendLine("if (state == CborDataItemType.Array)");
                _ = sb.AppendLine("{");
                if (hasIdxChildren)
                {
                    EmitIdxBranchWithNesting(sb, metadata, probeGroups.Where(g => g.Key.StartsWith("idx:", StringComparison.Ordinal))
                        .ToDictionary(g => g.Key, g => g.Value, StringComparer.Ordinal));
                }
                else { EmitArraySizeBranch(sb, metadata, arraySizeChildren); }
                _ = sb.AppendLine("}");
                if (hasStateChildren) { EmitStateBranch(sb, metadata, probeGroups, excludeArraySize: handleArraySize); }
                else { _ = sb.AppendLine($"throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"semantic tag or array\");"); }
            }
            else if ((hasTagChildren || hasConstrChildren) && !hasStateChildren && !hasArraySizeChildren)
            {
                _ = sb.AppendLine($"throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"semantic tag\");");
            }
            else
            {
                _ = sb.AppendLine("var reader = new CborReader(data.Span);");
                _ = sb.AppendLine("var state = reader.GetCurrentDataItemType();");
                EmitStateBranch(sb, metadata, probeGroups, excludeArraySize: false);
            }
            return true;
        }
        private static void EmitTagBranchWithConstr(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool hasConstr)
        {
            bool structUnion = metadata.IsStructUnion;
            _ = sb.AppendLine($"if (data.Length > 0 && (data.Span[0] >> 5) == 6)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    var _tagReader = new CborReader(data.Span);");
            _ = sb.AppendLine($"    _tagReader.TryReadSemanticTag(out ulong _tagVal);");
            if (!structUnion) { _ = sb.AppendLine($"    var tag = (int)_tagVal;"); }

            _ = sb.AppendLine($"    {metadata.FullyQualifiedName} _tagResult = {(structUnion ? "_tagVal" : "tag")} switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {(structUnion ? $"{tagValue}UL" : tagValue.ToString(CultureInfo.InvariantCulture))} => {EmitCaseReadExpr(metadata, group.Value[0], "data", "bytesConsumed", cast: true)},");
            }
            if (hasConstr && probeGroups.TryGetValue("constr", out List<SerializableTypeMetadata> constrGroup))
            {
                _ = sb.AppendLine($"        _ => {EmitCaseReadExpr(metadata, constrGroup[0], "data", "bytesConsumed", cast: true)},");
            }
            else
            {
                _ = sb.AppendLine(structUnion
                    ? $"        _ => throw CborError.DecodeTag(\"{metadata.FullyQualifiedName}\", \"registered union tag\", _tagVal)"
                    : $"        _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union tag\", tag)");
            }
            _ = sb.AppendLine("    };");
            _ = sb.AppendLine($"    return _tagResult;");
            _ = sb.AppendLine("}");
        }

        private static void EmitStateBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool excludeArraySize)
        {
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _stateResult = state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g =>
                !g.Key.StartsWith("tag:", StringComparison.Ordinal) && !g.Key.StartsWith("idx:", StringComparison.Ordinal)
                && (!excludeArraySize || !g.Key.StartsWith("array:", StringComparison.Ordinal)) && g.Key != "constr"))
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => {EmitCaseReadExpr(metadata, group.Value[0], "data", "bytesConsumed", cast: true)},");
            }
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union kind\", state)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine("return _stateResult;");
        }
        /// <summary>
        /// Emits an index switch where each colliding index group (more than one member sharing a
        /// leading index) is routed to a generated local function that resolves the variant by a
        /// secondary structural probe. Non-colliding indices read their single member directly.
        /// </summary>
        private void EmitIdxBranchWithNesting(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            // Emit one resolver local function per colliding index group.
            Dictionary<string, string> resolvers = [];
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => g.Value.Count > 1))
            {
                int idxValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                string resolverName = $"_ResolveIdx{idxValue}";
                EmitSecondaryProbeLocalFunction(sb, metadata, resolverName, group.Value);
                resolvers[group.Key] = resolverName;
            }

            _ = sb.AppendLine($"var _idxReader = new CborReader(data.Span);");
            _ = sb.AppendLine($"_idxReader.ReadBeginArray();");
            _ = sb.AppendLine($"_idxReader.ReadSize();");
            _ = sb.AppendLine($"int _idx = _idxReader.ReadInt32();");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _idxResult = _idx switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                int idxValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = group.Value.Count == 1
                    ? sb.AppendLine($"    {idxValue} => {EmitCaseReadExpr(metadata, group.Value[0], "data", "bytesConsumed", cast: true)},")
                    : sb.AppendLine($"    {idxValue} => {resolvers[group.Key]}(data, out bytesConsumed),");
            }
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union index\", _idx)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _idxResult;");
        }

        /// <summary>
        /// Emits a local function that resolves between members sharing a leading index by reading
        /// the CBOR data-item type of the first field that distinguishes them (skipping any
        /// semantic tag), then dispatching to the matching member's reader.
        /// </summary>
        private void EmitSecondaryProbeLocalFunction(StringBuilder sb, SerializableTypeMetadata metadata, string resolverName, List<SerializableTypeMetadata> members)
        {
            int depth = FindSeparatingDepth(members);

            _ = sb.AppendLine($"{metadata.FullyQualifiedName} {resolverName}(ReadOnlyMemory<byte> _d, out int _bc)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"var _pr = new CborReader(_d.Span);");
            _ = sb.AppendLine($"_pr.ReadBeginArray();");
            _ = sb.AppendLine($"_pr.ReadSize();");
            for (int i = 0; i < depth; i++)
            {
                _ = sb.AppendLine($"_pr.ReadDataItem();");
            }
            _ = sb.AppendLine($"var _shape = _pr.GetCurrentDataItemType();");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _r = _shape switch");
            _ = sb.AppendLine("{");
            foreach (SerializableTypeMetadata member in members)
            {
                string statePattern = GetCborReaderStatePattern(ClassifyProbeAtDepth(member, depth));
                _ = sb.AppendLine($"    {statePattern} => {EmitCaseReadExpr(metadata, member, "_d", "_bc", cast: true)},");
            }
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"unambiguous union case\")");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _r;");
            _ = sb.AppendLine("}");
        }

        /// <summary>
        /// Returns the shallowest field depth (>= 1) at which the given members have pairwise
        /// distinct, known CBOR shapes, or -1 if no field separates them structurally.
        /// </summary>
        private int FindSeparatingDepth(List<SerializableTypeMetadata> members)
        {
            int fallback = -1;
            int maxDepth = members.Min(m => m.Properties.Count);
            for (int depth = 1; depth < maxDepth; depth++)
            {
                List<string> keys = [.. members.Select(m => ClassifyProbeAtDepth(m, depth))];
                if (keys.Any(k => k == "unknown") || keys.Distinct().Count() != keys.Count)
                {
                    continue;
                }
                if (members.All(m => !m.Properties[depth].IsTypeNullable)) { return depth; }
                if (fallback < 0) { fallback = depth; }
            }
            return fallback;
        }

        /// <summary>
        /// Classifies the CBOR data-item shape of the field at the given CBOR order within a
        /// member, as a probe key understood by <see cref="GetCborReaderStatePattern"/>.
        /// </summary>
        private string ClassifyProbeAtDepth(SerializableTypeMetadata member, int depth) =>
            depth < 0 || depth >= member.Properties.Count
                ? "unknown"
                : ClassifyPropertyShape(member.Properties[depth]);

        /// <summary>
        /// Maps a property to the CBOR data-item shape it deserializes from. Tag-wrapped values
        /// (e.g. <c>CborEncodedValue</c>) resolve to their inner shape because
        /// <c>GetCurrentDataItemType</c> skips semantic tags.
        /// </summary>
        private string ClassifyPropertyShape(SerializablePropertyMetadata prop)
        {
            string cleanType = NormalizeTypeName(prop.PropertyTypeFullName);

            if (cleanType is "Chrysalis.Codec.Types.CborEncodedValue" or "CborEncodedValue")
            {
                return "bytes";
            }
            if (prop.CborMap is not null)
            {
                return "map";
            }
            if (prop.IsList)
            {
                return "array";
            }
            if (prop.IsMap)
            {
                return "map";
            }

            switch (cleanType)
            {
                case "string":
                    return "text";
                case "bool":
                    return "boolean";
                case "byte[]":
                case "ReadOnlyMemory<byte>":
                case "System.ReadOnlyMemory<byte>":
                    return "bytes";
                case "int":
                case "long":
                case "uint":
                case "ulong":
                    return "integer";
                default:
                    break;
            }

            return typeRegistry is not null && typeRegistry.TryGetValue(cleanType, out SerializableTypeMetadata? typeMeta) && typeMeta is not null
                ? ClassifyTypeShape(typeMeta)
                : "unknown";
        }

        /// <summary>
        /// Maps a record type to the CBOR data-item shape it serializes as, ignoring any semantic
        /// tag (which <c>GetCurrentDataItemType</c> skips). Constr types encode as a (possibly
        /// tagged) array.
        /// </summary>
        private static string ClassifyTypeShape(SerializableTypeMetadata meta) => meta.SerializationType switch
        {
            SerializationType.List => "array",
            SerializationType.Constr => "array",
            SerializationType.Map => "map",
            SerializationType.Container => "unknown",
            SerializationType.Union => "unknown",
            _ => "unknown"
        };

        internal static bool TryGetSecondaryProbe(
            SerializableTypeMetadata union, SerializableTypeMetadata caseMeta,
            ImmutableDictionary<string, SerializableTypeMetadata>? registry, out int depth, out string shapeKey)
        {
            depth = -1;
            shapeKey = "";
            string key = GetProbeKey(caseMeta);
            if (!key.StartsWith("idx:", StringComparison.Ordinal)) { return false; }
            List<SerializableTypeMetadata> group = [.. GetUnionLeaves(union).Where(c => GetProbeKey(c) == key)];
            if (group.Count < 2) { return false; }
            UnionEmitter emitter = new(registry);
            int found = emitter.FindSeparatingDepth(group);
            if (found < 1) { return false; }
            string classified = emitter.ClassifyProbeAtDepth(caseMeta, found);
            if (classified == "unknown") { return false; }
            depth = found;
            shapeKey = classified;
            return true;
        }

        private static void EmitArraySizeBranch(StringBuilder sb, SerializableTypeMetadata metadata, List<SerializableTypeMetadata> arraySizeChildren)
        {
            int maxSize = arraySizeChildren.Max(c => c.Properties.Count);
            _ = sb.AppendLine($"int _size = global::Chrysalis.Codec.Serialization.CborUnionWire.ReadArrayLength(data.Span, {maxSize});");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} _sizeResult = _size switch");
            _ = sb.AppendLine("{");
            foreach (SerializableTypeMetadata child in arraySizeChildren)
            {
                Emitter.TryGetListArityBounds(child, out int min, out int max);
                string arm = min == max ? $"{min}" : $">= {min} and <= {max}";
                _ = sb.AppendLine($"    {arm} => {EmitCaseReadExpr(metadata, child, "data", "bytesConsumed", cast: true)},");
            }
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered array size\", _size)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine("return _sizeResult;");
        }

        private static List<SerializableTypeMetadata> GetUnionLeaves(SerializableTypeMetadata union) =>
            union.IsStructUnion ? [.. union.ChildTypes] : FlattenUnionChildren(union.ChildTypes);

        private static string EmitCaseReadExpr(
            SerializableTypeMetadata union,
            SerializableTypeMetadata leaf,
            string dataExpr,
            string consumedExpr,
            bool cast)
        {
            if (union.IsStructUnion)
            {
                int caseIndex = FindStructCaseIndex(union, leaf);
                return $"__Read{caseIndex}({dataExpr}, out {consumedExpr})";
            }

            string call = $"{leaf.FullyQualifiedName}.Read({dataExpr}, out {consumedExpr})";
            return cast ? $"({union.FullyQualifiedName}){call}" : call;
        }

        private static int FindStructCaseIndex(SerializableTypeMetadata union, SerializableTypeMetadata leaf)
        {
            for (int i = 0; i < union.ChildTypes.Count; i++)
            {
                if (union.ChildTypes[i].FullyQualifiedName == leaf.FullyQualifiedName)
                {
                    return i + 1;
                }
            }
            throw new InvalidOperationException($"Case '{leaf.FullyQualifiedName}' not found on union '{union.FullyQualifiedName}'.");
        }

        private static List<SerializableTypeMetadata> FlattenUnionChildren(EquatableArray<SerializableTypeMetadata> children)
        {
            Dictionary<string, SerializableTypeMetadata> seen = [];
            FlattenUnionChildrenRec(children, seen);
            return [.. seen.Values];
        }

        private static void FlattenUnionChildrenRec(EquatableArray<SerializableTypeMetadata> children, Dictionary<string, SerializableTypeMetadata> seen)
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

        internal static string GetProbeKey(SerializableTypeMetadata child)
        {
            if (child.CborTag is not null)
            {
                return $"tag:{child.CborTag}";
            }

            if (child.SerializationType == SerializationType.Constr && child.CborIndex is not null && child.CborIndex >= 0)
            {
                int tag = Emitter.ResolveTag(child.CborIndex);
                return $"tag:{tag}";
            }

            return child.SerializationType switch
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

        internal static string GetContainerProbeKey(SerializableTypeMetadata child)
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
                if (child.Properties[0].IsMap || child.Properties[0].CborMap is not null)
                {
                    return "map";
                }
                if (propType.Contains("List<"))
                {
                    return "array";
                }
            }
            return "unknown";
        }

        private static string GetCborReaderStatePattern(string probeKey) => probeKey.StartsWith("array:", StringComparison.Ordinal)
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
}
