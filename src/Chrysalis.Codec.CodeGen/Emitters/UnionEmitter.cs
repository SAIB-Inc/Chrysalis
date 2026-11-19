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

        public static EquatableArray<SerializableTypeMetadata> GetShapeDependencies(SerializableTypeMetadata union,
            ImmutableDictionary<string, SerializableTypeMetadata> registry)
        {
            List<SerializableTypeMetadata> leaves = FlattenUnionChildren(union.ChildTypes);
            if (leaves.Count < 2)
            {
                return default;
            }

            // The structural probe looks up leaf fields. The fallback tuple probe can do the
            // same inside untagged nested lists. Walk breadth-first so each tuple is expanded
            // once, at the shallowest depth at which the reader could consult its fields.
            bool probeNested = leaves.All(leaf => leaf.SerializationType == SerializationType.List);
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
            return sb.AppendLine(AmbiguityError(metadata, FlattenUnionChildren(metadata.ChildTypes)));
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

        private bool TryEmitCandidateShapes(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            List<SerializableTypeMetadata> leaves = FlattenUnionChildren(metadata.ChildTypes);
            if (leaves.Count == 1)
            {
                _ = sb.AppendLine($"return {leaves[0].FullyQualifiedName}.Read(data, out bytesConsumed);");
                return true;
            }
            if (leaves.Count == 0 || leaves.Any(leaf => leaf.SerializationType != SerializationType.List))
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
            for (int i = 0; i < leaves.Count; i++)
            {
                _ = sb.AppendLine($"if (_Matches{i}(new CborReader(data.Span), ref failure))");
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
                _ = sb.AppendLine($"    {i} => {leaves[i].FullyQualifiedName}.Read(data, out bytesConsumed),");
            }
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, data.Length - failure.Remaining, failure.Path, failure.Expected)");
            _ = sb.AppendLine("};");

            for (int i = 0; i < leaves.Count; i++)
            {
                _ = sb.AppendLine($"static bool _Matches{i}(CborReader reader, ref (int Remaining, string Path, string Expected) failure)");
                _ = sb.AppendLine("{");
                int index = 0;
                EmitTupleShape(sb, metadata.FullyQualifiedName, leaves[i], ref index, 0);
                _ = sb.AppendLine("return true;");
                _ = sb.AppendLine("}");
            }
            return true;
        }

        private bool TupleShapesOverlap(SerializableTypeMetadata first, SerializableTypeMetadata second, int depth)
        {
            if (first.CborIndex is >= 0 && second.CborIndex is >= 0 && first.CborIndex != second.CborIndex)
            {
                return false;
            }
            int common = Math.Max(first.Properties.Count(p => !p.IsTypeNullable), second.Properties.Count(p => !p.IsTypeNullable));
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

        private static bool IsProbeableTuple(SerializableTypeMetadata metadata) =>
            metadata is { SerializationType: SerializationType.List, CborTag: null } && metadata.Properties.All(p => !p.IsTypeNullable);

        private void EmitTupleShape(StringBuilder sb, string union, SerializableTypeMetadata tuple, ref int index, int depth)
        {
            string count = $"_size{index++}";
            string path = $"{union}.{tuple.FullyQualifiedName}";
            _ = sb.AppendLine("if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) != 4) return false;");
            _ = sb.AppendLine("reader.ReadBeginArray();");
            _ = sb.AppendLine($"int {count} = reader.ReadSize();");
            int minimum = tuple.Properties.Count(p => !p.IsTypeNullable);
            _ = sb.AppendLine($"if ({count} != -1 && ({count} < {minimum} || {count} > {tuple.Properties.Count})) return false;");
            int position = 0;
            foreach (SerializablePropertyMetadata prop in tuple.Properties)
            {
                if (prop.IsTypeNullable)
                {
                    _ = sb.AppendLine($"if ({count} >= 0 && {count} <= {position}) return true;");
                    _ = sb.AppendLine("if (!reader.Buffer.IsEmpty && reader.Buffer[0] == 0xFF) { reader.SkipDataItem(); return true; }");
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
                string expected = prop.IsIndefinite && prop.IsList ? "indefinite array" : shape switch
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
                    _ = sb.AppendLine($"if ({nullable}reader.GetCurrentDataItemType() is not ({pattern})) return false;");
                }
                if (prop.IsList && prop.IsIndefinite)
                {
                    _ = sb.AppendLine("if (reader.Buffer[0] != 0x9F) return false;");
                }
                _ = sb.AppendLine("reader.SkipDataItem();");
            }
            _ = sb.AppendLine($"if ({count} == -1)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("if (reader.Buffer.IsEmpty || reader.Buffer[0] != 0xFF) return false;");
            _ = sb.AppendLine("reader.SkipDataItem();");
            _ = sb.AppendLine("}");
        }

        private static bool EmitBuiltInReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            string? method = NormalizeTypeName(metadata.FullyQualifiedName) switch
            {
                "Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusData" => "PlutusWire.ReadData",
                "Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusBigInt" => "PlutusWire.ReadInteger",
                "Chrysalis.Codec.Types.Cardano.Core.Transaction.ITransactionBody" => "TransactionEraReader.ReadBody",
                "Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.ITransactionWitnessSet" => "global::Chrysalis.Codec.Types.Cardano.Core.Transaction.TransactionEraReader.ReadWitnesses",
                "Chrysalis.Codec.Types.Cardano.Core.Protocol.IProtocolParamUpdate" => "ProtocolUpdateWire.Read",
                "Chrysalis.Codec.Types.ICborPrimitive" => "PrimitiveWire.Read",
                _ => null
            };
            if (method is not null)
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

            if (probeGroups.ContainsKey("unknown"))
            {
                return false;
            }

            // Same-index collisions: members that share a leading index but differ in a deeper
            // field. Rather than dropping the whole union to fragile try/catch, resolve the
            // colliding members with a secondary structural probe on the first field whose CBOR
            // shape distinguishes them. Only the index-discriminated case is handled here; a
            // collision under any other top-level discriminator still falls back.
            if (probeGroups.Values.Any(g => g.Count > 1))
            {
                bool allIdx = probeGroups.Keys.All(k => k.StartsWith("idx:", StringComparison.Ordinal));
                if (!allIdx)
                {
                    return false;
                }

                foreach (List<SerializableTypeMetadata> colliding in probeGroups.Values.Where(g => g.Count > 1))
                {
                    if (FindSeparatingDepth(colliding) < 0)
                    {
                        return false;
                    }
                }

                EmitIdxBranchWithNesting(sb, metadata, probeGroups);
                return true;
            }

            bool hasTagChildren = probeGroups.Keys.Any(k => k.StartsWith("tag:", StringComparison.Ordinal));
            bool hasConstrChildren = probeGroups.ContainsKey("constr");
            bool hasIdxChildren = probeGroups.Keys.Any(k => k.StartsWith("idx:", StringComparison.Ordinal));
            bool hasArraySizeChildren = probeGroups.Keys.Any(k => k.StartsWith("array:", StringComparison.Ordinal));
            bool hasStateChildren = probeGroups.Keys.Any(k => !k.StartsWith("tag:", StringComparison.Ordinal) && !k.StartsWith("idx:", StringComparison.Ordinal) && !k.StartsWith("array:", StringComparison.Ordinal) && k != "constr");

            // Index patterns cannot be emitted by a data-item-state switch.
            if (hasIdxChildren && (hasTagChildren || hasConstrChildren || hasArraySizeChildren || hasStateChildren))
            {
                return false;
            }

            if (hasArraySizeChildren && !hasTagChildren && !hasConstrChildren && !hasIdxChildren && !hasStateChildren)
            {
                // The size switch can select only exact lengths. A trailing nullable field
                // lets its reader accept a shorter tuple, possibly shared by another case.
                if (children.Any(child => child.Properties.Any(prop => prop.IsTypeNullable)))
                {
                    return false;
                }
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
                _ = sb.AppendLine($"throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"semantic tag\");");
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
                _ = sb.AppendLine($"        _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union tag\", tag)");
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
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union kind\", state)");
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
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union index\", _idx)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _idxResult;");
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
                    ? sb.AppendLine($"    {idxValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data, out bytesConsumed),")
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
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){member.FullyQualifiedName}.Read(_d, out _bc),");
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
            int maxDepth = members.Min(m => m.Properties.Count);
            for (int depth = 1; depth < maxDepth; depth++)
            {
                List<string> keys = [.. members.Select(m => ClassifyProbeAtDepth(m, depth))];
                if (keys.Any(k => k == "unknown"))
                {
                    continue;
                }
                if (keys.Distinct().Count() == keys.Count)
                {
                    return depth;
                }
            }
            return -1;
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
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered array size\", _size)");
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
            _ = sb.AppendLine($"    _ => throw CborError.Decode(data, 0, \"{metadata.FullyQualifiedName}\", \"registered union kind\", state)");
            _ = sb.AppendLine("};");
            _ = sb.AppendLine($"return _stateResult;");
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
                if (child.Properties[0].IsMap)
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
