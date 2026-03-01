using System.Globalization;
using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class UnionEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            if (TryEmitListContainerVariantReader(sb, metadata, useExistingReader))
            {
                return sb;
            }

            if (useExistingReader)
            {
                if (TryEmitStructuralProbeReader(sb, metadata, useExistingReader: true))
                {
                    return sb;
                }

                _ = sb.AppendLine("ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue(true);");

                // Reader overload fallback: probe once on the captured encoded value and dispatch directly.
                // This avoids bouncing through the union's Read(ReadOnlyMemory<byte>) wrapper in hot paths.
                if (TryEmitUnionCaseDiscriminantReaderFromEncodedValue(sb, metadata, "encodedValue"))
                {
                    return sb;
                }

                if (TryEmitListArityProbeReaderFromEncodedValue(sb, metadata, "encodedValue"))
                {
                    return sb;
                }

                if (TryEmitListDiscriminantProbeReaderFromEncodedValue(sb, metadata, "encodedValue"))
                {
                    return sb;
                }

                if (TryEmitListIntegerDiscriminantCacheReaderFromEncodedValue(sb, metadata, "encodedValue"))
                {
                    return sb;
                }

                _ = sb.AppendLine($"return {metadata.FullyQualifiedName}.Read(encodedValue);");
                return sb;
            }

            if (TryEmitUnionCaseDiscriminantReader(sb, metadata))
            {
                return sb;
            }

            if (TryEmitListArityProbeReader(sb, metadata))
            {
                return sb;
            }

            // General discriminant probe: list-based unions that carry a stable constructor label.
            if (TryEmitListDiscriminantProbeReader(sb, metadata))
            {
                return sb;
            }

            // General discriminant probe with runtime cache for list unions that use an integer tag.
            if (TryEmitListIntegerDiscriminantCacheReader(sb, metadata))
            {
                return sb;
            }

            // General structural probe: children have distinct CBOR major types or tags
            if (TryEmitStructuralProbeReader(sb, metadata, useExistingReader: false))
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
                _ = sb.AppendLine($"{childType.FullyQualifiedName}.Write(writer, ({childType.FullyQualifiedName})data);");
                _ = sb.AppendLine($"break;");
            }
            _ = sb.AppendLine($"default:");
            _ = sb.AppendLine($"throw new Exception(\"Union serialization failed. {metadata.FullyQualifiedName} \");");
            _ = sb.AppendLine("}");
            return sb;
        }

        private static bool TryEmitListContainerVariantReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            if (!TryGetListContainerVariantPattern(
                metadata,
                out string? listItemType,
                out string? untaggedDefiniteType,
                out string? untaggedIndefiniteType,
                out Dictionary<int, (string? DefiniteType, string? IndefiniteType)> taggedVariants))
            {
                return false;
            }

            if (!useExistingReader)
            {
                _ = sb.AppendLine("var reader = new CborReader(data, CborConformanceMode.Lax);");
            }

            bool hasUntaggedVariant = untaggedDefiniteType is not null || untaggedIndefiniteType is not null;
            bool hasTaggedVariant = taggedVariants.Count > 0;

            if (hasTaggedVariant)
            {
                _ = sb.AppendLine("bool hasTag = false;");
                _ = sb.AppendLine("int tag = 0;");
                _ = sb.AppendLine("if (reader.PeekState() == CborReaderState.Tag)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("    hasTag = true;");
                _ = sb.AppendLine("    tag = (int)reader.ReadTag();");
                _ = sb.AppendLine("}");
                if (hasUntaggedVariant)
                {
                    _ = sb.AppendLine("else if (reader.PeekState() != CborReaderState.StartArray)");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array or tag\");");
                    _ = sb.AppendLine("}");
                }
                else
                {
                    _ = sb.AppendLine("else");
                    _ = sb.AppendLine("{");
                    _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected tag\");");
                    _ = sb.AppendLine("}");
                }
            }
            else
            {
                _ = sb.AppendLine("if (reader.PeekState() != CborReaderState.StartArray)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array\");");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine("int? arrayLength = reader.ReadStartArray();");
            _ = sb.AppendLine("bool isIndefiniteArray = !arrayLength.HasValue;");
            _ = sb.AppendLine($"List<{listItemType}> listItems = arrayLength.HasValue ? new List<{listItemType}>(arrayLength.Value) : new List<{listItemType}>();");
            _ = sb.AppendLine("while (reader.PeekState() != CborReaderState.EndArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    {listItemType} listItem = global::Chrysalis.Cbor.Serialization.Utils.GenericSerializationUtil.Read<{listItemType}>(reader);");
            _ = sb.AppendLine("    listItems.Add(listItem);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("reader.ReadEndArray();");

            if (hasTaggedVariant)
            {
                _ = sb.AppendLine("if (hasTag)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("    return tag switch");
                _ = sb.AppendLine("    {");
                foreach (KeyValuePair<int, (string? DefiniteType, string? IndefiniteType)> taggedVariant in taggedVariants.OrderBy(kvp => kvp.Key))
                {
                    string taggedDefiniteExpr = taggedVariant.Value.DefiniteType is not null
                        ? $"({metadata.FullyQualifiedName})new {taggedVariant.Value.DefiniteType}(listItems)"
                        : $"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected indefinite array for tag {taggedVariant.Key}\")";
                    string taggedIndefiniteExpr = taggedVariant.Value.IndefiniteType is not null
                        ? $"({metadata.FullyQualifiedName})new {taggedVariant.Value.IndefiniteType}(listItems)"
                        : $"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected definite array for tag {taggedVariant.Key}\")";
                    _ = sb.AppendLine($"        {taggedVariant.Key} => isIndefiniteArray ? {taggedIndefiniteExpr} : {taggedDefiniteExpr},");
                }
                _ = sb.AppendLine($"        _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown tag \" + tag)");
                _ = sb.AppendLine("    };");
                _ = sb.AppendLine("}");
            }

            if (!hasUntaggedVariant)
            {
                _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected tagged variant\");");
                return true;
            }

            string untaggedDefiniteExpr = untaggedDefiniteType is not null
                ? $"({metadata.FullyQualifiedName})new {untaggedDefiniteType}(listItems)"
                : $"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected indefinite untagged array\")";
            string untaggedIndefiniteExpr = untaggedIndefiniteType is not null
                ? $"({metadata.FullyQualifiedName})new {untaggedIndefiniteType}(listItems)"
                : $"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected definite untagged array\")";
            _ = sb.AppendLine($"return isIndefiniteArray ? {untaggedIndefiniteExpr} : {untaggedDefiniteExpr};");

            return true;
        }

        private static bool TryEmitListDiscriminantProbeReaderFromEncodedValue(
            StringBuilder sb,
            SerializableTypeMetadata metadata,
            string encodedValueVariable)
        {
            Dictionary<int, SerializableTypeMetadata>? cases = TryGetListDiscriminantCases(metadata);
            if (cases is null || cases.Count == 0)
            {
                return false;
            }

            _ = sb.AppendLine($"var probeReader = new CborReader({encodedValueVariable}, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (probeReader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("probeReader.ReadStartArray();");
            _ = sb.AppendLine("if (probeReader.PeekState() is not CborReaderState.UnsignedInteger and not CborReaderState.NegativeInteger)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected integer discriminant\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int discriminant = probeReader.ReadInt32();");
            _ = sb.AppendLine("return discriminant switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, SerializableTypeMetadata> entry in cases.OrderBy(kvp => kvp.Key))
            {
                _ = sb.AppendLine($"    {entry.Key} => ({metadata.FullyQualifiedName}){entry.Value.FullyQualifiedName}.Read({encodedValueVariable}),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown discriminant \" + discriminant)");
            _ = sb.AppendLine("};");

            return true;
        }

        private static bool TryGetListContainerVariantPattern(
            SerializableTypeMetadata metadata,
            out string? listItemType,
            out string? untaggedDefiniteType,
            out string? untaggedIndefiniteType,
            out Dictionary<int, (string? DefiniteType, string? IndefiniteType)> taggedVariants)
        {
            listItemType = null;
            untaggedDefiniteType = null;
            untaggedIndefiniteType = null;
            taggedVariants = [];

            if (metadata.ChildTypes.Count < 2)
            {
                return false;
            }

            foreach (SerializableTypeMetadata child in metadata.ChildTypes)
            {
                if (child.SerializationType != SerializationType.Container || child.Properties.Count != 1)
                {
                    return false;
                }

                SerializablePropertyMetadata prop = child.Properties[0];
                if (!prop.IsList || prop.ListItemTypeFullName is null)
                {
                    return false;
                }

                if (listItemType is null)
                {
                    listItemType = prop.ListItemTypeFullName;
                }
                else if (!string.Equals(listItemType, prop.ListItemTypeFullName, StringComparison.Ordinal))
                {
                    return false;
                }

                bool isIndefiniteVariant = prop.IsIndefinite;

                if (child.CborTag is null)
                {
                    if (isIndefiniteVariant)
                    {
                        if (untaggedIndefiniteType is not null)
                        {
                            return false;
                        }

                        untaggedIndefiniteType = child.FullyQualifiedName;
                    }
                    else
                    {
                        if (untaggedDefiniteType is not null)
                        {
                            return false;
                        }

                        untaggedDefiniteType = child.FullyQualifiedName;
                    }
                }
                else
                {
                    int tag = child.CborTag.Value;
                    if (!taggedVariants.TryGetValue(tag, out (string? DefiniteType, string? IndefiniteType) tagVariant))
                    {
                        tagVariant = (null, null);
                    }

                    if (isIndefiniteVariant)
                    {
                        if (tagVariant.IndefiniteType is not null)
                        {
                            return false;
                        }

                        tagVariant.IndefiniteType = child.FullyQualifiedName;
                    }
                    else
                    {
                        if (tagVariant.DefiniteType is not null)
                        {
                            return false;
                        }

                        tagVariant.DefiniteType = child.FullyQualifiedName;
                    }

                    taggedVariants[tag] = tagVariant;
                }
            }

            return listItemType is not null &&
                   (untaggedDefiniteType is not null ||
                    untaggedIndefiniteType is not null ||
                    taggedVariants.Count > 0);
        }

        private static bool TryEmitUnionCaseDiscriminantReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Dictionary<int, SerializableTypeMetadata>? cases = TryGetUnionCaseDiscriminantCases(metadata);
            if (cases is null || cases.Count == 0)
            {
                return false;
            }

            _ = sb.AppendLine("var reader = new CborReader(data, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (reader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("reader.ReadStartArray();");
            _ = sb.AppendLine("if (reader.PeekState() is not CborReaderState.UnsignedInteger and not CborReaderState.NegativeInteger)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected integer discriminant\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int discriminant = reader.ReadInt32();");
            _ = sb.AppendLine("return discriminant switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, SerializableTypeMetadata> entry in cases.OrderBy(kvp => kvp.Key))
            {
                _ = sb.AppendLine($"    {entry.Key} => ({metadata.FullyQualifiedName}){entry.Value.FullyQualifiedName}.Read(data),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown discriminant \" + discriminant)");
            _ = sb.AppendLine("};");

            return true;
        }

        private static bool TryEmitUnionCaseDiscriminantReaderFromEncodedValue(
            StringBuilder sb,
            SerializableTypeMetadata metadata,
            string encodedValueVariable)
        {
            Dictionary<int, SerializableTypeMetadata>? cases = TryGetUnionCaseDiscriminantCases(metadata);
            if (cases is null || cases.Count == 0)
            {
                return false;
            }

            _ = sb.AppendLine($"var probeReader = new CborReader({encodedValueVariable}, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (probeReader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("probeReader.ReadStartArray();");
            _ = sb.AppendLine("if (probeReader.PeekState() is not CborReaderState.UnsignedInteger and not CborReaderState.NegativeInteger)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected integer discriminant\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int discriminant = probeReader.ReadInt32();");
            _ = sb.AppendLine("return discriminant switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, SerializableTypeMetadata> entry in cases.OrderBy(kvp => kvp.Key))
            {
                _ = sb.AppendLine($"    {entry.Key} => ({metadata.FullyQualifiedName}){entry.Value.FullyQualifiedName}.Read({encodedValueVariable}),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown discriminant \" + discriminant)");
            _ = sb.AppendLine("};");

            return true;
        }

        private static Dictionary<int, SerializableTypeMetadata>? TryGetUnionCaseDiscriminantCases(SerializableTypeMetadata metadata)
        {
            Dictionary<int, SerializableTypeMetadata> cases = [];
            foreach (SerializableTypeMetadata child in metadata.ChildTypes)
            {
                if (child.UnionCaseDiscriminant is null || child.SerializationType != SerializationType.List)
                {
                    return null;
                }

                SerializablePropertyMetadata? firstField = child.Properties
                    .Where(p => p.Order is not null)
                    .OrderBy(p => p.Order)
                    .FirstOrDefault();
                if (firstField is null || firstField.Order != 0 || !IsIntegerType(firstField.PropertyTypeFullName))
                {
                    return null;
                }

                int discriminant = child.UnionCaseDiscriminant.Value;
                if (cases.ContainsKey(discriminant))
                {
                    return null;
                }

                cases.Add(discriminant, child);
            }

            return cases.Count > 0 ? cases : null;
        }

        private static bool TryEmitListArityProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Dictionary<int, SerializableTypeMetadata>? cases = TryGetListArityCases(metadata);
            if (cases is null || cases.Count == 0)
            {
                return false;
            }

            _ = sb.AppendLine("var reader = new CborReader(data, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (reader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int? arrayLength = reader.ReadStartArray();");
            _ = sb.AppendLine("if (!arrayLength.HasValue)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected definite array for arity dispatch\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("return arrayLength.Value switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, SerializableTypeMetadata> entry in cases.OrderBy(kvp => kvp.Key))
            {
                _ = sb.AppendLine($"    {entry.Key} => ({metadata.FullyQualifiedName}){entry.Value.FullyQualifiedName}.Read(data),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown array arity \" + arrayLength.Value)");
            _ = sb.AppendLine("};");

            return true;
        }

        private static bool TryEmitListArityProbeReaderFromEncodedValue(
            StringBuilder sb,
            SerializableTypeMetadata metadata,
            string encodedValueVariable)
        {
            Dictionary<int, SerializableTypeMetadata>? cases = TryGetListArityCases(metadata);
            if (cases is null || cases.Count == 0)
            {
                return false;
            }

            _ = sb.AppendLine($"var probeReader = new CborReader({encodedValueVariable}, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (probeReader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int? arrayLength = probeReader.ReadStartArray();");
            _ = sb.AppendLine("if (!arrayLength.HasValue)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected definite array for arity dispatch\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("return arrayLength.Value switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, SerializableTypeMetadata> entry in cases.OrderBy(kvp => kvp.Key))
            {
                _ = sb.AppendLine($"    {entry.Key} => ({metadata.FullyQualifiedName}){entry.Value.FullyQualifiedName}.Read({encodedValueVariable}),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown array arity \" + arrayLength.Value)");
            _ = sb.AppendLine("};");

            return true;
        }

        private static Dictionary<int, SerializableTypeMetadata>? TryGetListArityCases(SerializableTypeMetadata metadata)
        {
            Dictionary<int, SerializableTypeMetadata> cases = [];
            foreach (SerializableTypeMetadata child in metadata.ChildTypes)
            {
                if (child.SerializationType != SerializationType.List)
                {
                    return null;
                }

                if (!TryGetDefiniteListArity(child, out int arity))
                {
                    return null;
                }

                if (cases.ContainsKey(arity))
                {
                    return null;
                }

                cases.Add(arity, child);
            }

            return cases.Count > 0 ? cases : null;
        }

        private static bool TryGetDefiniteListArity(SerializableTypeMetadata metadata, out int arity)
        {
            arity = 0;
            List<int> orders =
            [
                .. metadata.Properties
                    .Where(p => p.Order is not null)
                    .Select(p => p.Order!.Value)
                    .OrderBy(v => v)
            ];
            if (orders.Count != metadata.Properties.Count)
            {
                return false;
            }

            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i] != i)
                {
                    return false;
                }
            }

            arity = orders.Count;
            return true;
        }

        private static bool TryEmitListDiscriminantProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Dictionary<int, SerializableTypeMetadata>? cases = TryGetListDiscriminantCases(metadata);
            if (cases is null || cases.Count == 0)
            {
                return false;
            }

            _ = sb.AppendLine("var reader = new CborReader(data, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (reader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("reader.ReadStartArray();");
            _ = sb.AppendLine("if (reader.PeekState() is not CborReaderState.UnsignedInteger and not CborReaderState.NegativeInteger)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected integer discriminant\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int discriminant = reader.ReadInt32();");
            _ = sb.AppendLine("return discriminant switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<int, SerializableTypeMetadata> entry in cases.OrderBy(kvp => kvp.Key))
            {
                _ = sb.AppendLine($"    {entry.Key} => ({metadata.FullyQualifiedName}){entry.Value.FullyQualifiedName}.Read(data),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown discriminant \" + discriminant)");
            _ = sb.AppendLine("};");

            return true;
        }

        private static Dictionary<int, SerializableTypeMetadata>? TryGetListDiscriminantCases(SerializableTypeMetadata metadata)
        {
            Dictionary<int, SerializableTypeMetadata> cases = [];
            foreach (SerializableTypeMetadata child in metadata.ChildTypes)
            {
                if (child.SerializationType != SerializationType.List)
                {
                    return null;
                }

                SerializablePropertyMetadata? firstField = child.Properties
                    .Where(p => p.Order is not null)
                    .OrderBy(p => p.Order)
                    .FirstOrDefault();
                if (firstField is null || firstField.Order != 0)
                {
                    return null;
                }

                if (!TryExtractDiscriminantValue(firstField.PropertyTypeFullName, out int value))
                {
                    return null;
                }

                if (cases.ContainsKey(value))
                {
                    return null;
                }
                cases[value] = child;
            }

            return cases;
        }

        private static bool TryExtractDiscriminantValue(string propertyTypeFullName, out int value)
        {
            const string ValueTypePrefix = "Value";
            value = default;
            string typeName = propertyTypeFullName.Split('.').Last();
            if (!typeName.StartsWith(ValueTypePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string suffix = typeName.Substring(ValueTypePrefix.Length);
            return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryEmitListIntegerDiscriminantCacheReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (!TryGetListIntegerDiscriminantChildren(metadata, out List<SerializableTypeMetadata> children))
            {
                return false;
            }

            string cacheType = "global::Chrysalis.Cbor.Serialization.Utils.UnionDispatchCache";

            _ = sb.AppendLine("var reader = new CborReader(data, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (reader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("reader.ReadStartArray();");
            _ = sb.AppendLine("if (reader.PeekState() is not CborReaderState.UnsignedInteger and not CborReaderState.NegativeInteger)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected integer discriminant\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int discriminant = reader.ReadInt32();");
            _ = sb.AppendLine($"if ({cacheType}.TryGet<{metadata.FullyQualifiedName}>(discriminant, out var cachedDispatch))");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    try");
            _ = sb.AppendLine("    {");
            _ = sb.AppendLine("        return cachedDispatch(data);");
            _ = sb.AppendLine("    }");
            _ = sb.AppendLine("    catch (Exception)");
            _ = sb.AppendLine("    {");
            _ = sb.AppendLine($"        {cacheType}.Remove<{metadata.FullyQualifiedName}>(discriminant);");
            _ = sb.AppendLine("    }");
            _ = sb.AppendLine("}");

            foreach (SerializableTypeMetadata childType in children)
            {
                _ = sb.AppendLine("try");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    var result = ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read(data);");
                _ = sb.AppendLine($"    {cacheType}.Set<{metadata.FullyQualifiedName}>(discriminant, static dispatchData => ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read(dispatchData));");
                _ = sb.AppendLine("    return result;");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("catch (Exception)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown discriminant \" + discriminant);");
            return true;
        }

        private static bool TryEmitListIntegerDiscriminantCacheReaderFromEncodedValue(
            StringBuilder sb,
            SerializableTypeMetadata metadata,
            string encodedValueVariable)
        {
            if (!TryGetListIntegerDiscriminantChildren(metadata, out List<SerializableTypeMetadata> children))
            {
                return false;
            }

            string cacheType = "global::Chrysalis.Cbor.Serialization.Utils.UnionDispatchCache";

            _ = sb.AppendLine($"var probeReader = new CborReader({encodedValueVariable}, CborConformanceMode.Lax);");
            _ = sb.AppendLine("if (probeReader.PeekState() != CborReaderState.StartArray)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected array state\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("probeReader.ReadStartArray();");
            _ = sb.AppendLine("if (probeReader.PeekState() is not CborReaderState.UnsignedInteger and not CborReaderState.NegativeInteger)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected integer discriminant\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("int discriminant = probeReader.ReadInt32();");
            _ = sb.AppendLine($"if ({cacheType}.TryGet<{metadata.FullyQualifiedName}>(discriminant, out var cachedDispatch))");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    try");
            _ = sb.AppendLine("    {");
            _ = sb.AppendLine($"        return cachedDispatch({encodedValueVariable});");
            _ = sb.AppendLine("    }");
            _ = sb.AppendLine("    catch (Exception)");
            _ = sb.AppendLine("    {");
            _ = sb.AppendLine($"        {cacheType}.Remove<{metadata.FullyQualifiedName}>(discriminant);");
            _ = sb.AppendLine("    }");
            _ = sb.AppendLine("}");

            foreach (SerializableTypeMetadata childType in children)
            {
                _ = sb.AppendLine("try");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    var result = ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read({encodedValueVariable});");
                _ = sb.AppendLine($"    {cacheType}.Set<{metadata.FullyQualifiedName}>(discriminant, static dispatchData => ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read(dispatchData));");
                _ = sb.AppendLine("    return result;");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("catch (Exception)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unknown discriminant \" + discriminant);");
            return true;
        }

        private static bool TryGetListIntegerDiscriminantChildren(SerializableTypeMetadata metadata, out List<SerializableTypeMetadata> children)
        {
            children = [];
            foreach (SerializableTypeMetadata child in metadata.ChildTypes)
            {
                if (child.SerializationType != SerializationType.List)
                {
                    children = [];
                    return false;
                }

                SerializablePropertyMetadata? firstField = child.Properties
                    .Where(p => p.Order is not null)
                    .OrderBy(p => p.Order)
                    .FirstOrDefault();
                if (firstField is null || firstField.Order != 0)
                {
                    children = [];
                    return false;
                }

                if (!IsIntegerType(firstField.PropertyTypeFullName))
                {
                    children = [];
                    return false;
                }

                children.Add(child);
            }

            return children.Count > 0;
        }

        private static bool IsIntegerType(string propertyTypeFullName)
        {
            string cleanType = propertyTypeFullName.Replace("?", "");
            return cleanType is
                "int" or
                "uint" or
                "long" or
                "ulong" or
                "System.Int32" or
                "System.UInt32" or
                "System.Int64" or
                "System.UInt64" or
                "global::System.Int32" or
                "global::System.UInt32" or
                "global::System.Int64" or
                "global::System.UInt64";
        }

        /// <summary>
        /// Attempts to emit a structural probe reader. Returns true if successful, false if the union
        /// cannot be probed (children are not structurally distinguishable).
        /// </summary>
        private static bool TryEmitStructuralProbeReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            List<SerializableTypeMetadata> children = metadata.ChildTypes;
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
            bool hasNonTagChildren = probeGroups.Keys.Any(k => !k.StartsWith("tag:", StringComparison.Ordinal));

            if (!useExistingReader)
            {
                _ = sb.AppendLine($"var reader = new CborReader(data, CborConformanceMode.Lax);");
            }
            _ = sb.AppendLine($"var state = reader.PeekState();");

            if (hasTagChildren && hasNonTagChildren)
            {
                EmitTagBranch(sb, metadata, probeGroups, useExistingReader);
                EmitStateBranch(sb, metadata, probeGroups, useExistingReader);
            }
            else if (hasTagChildren)
            {
                EmitTagOnlyBranch(sb, metadata, probeGroups, useExistingReader);
            }
            else
            {
                EmitStateOnlyBranch(sb, metadata, probeGroups, useExistingReader);
            }

            return true;
        }

        private static void EmitTagBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool useExistingReader)
        {
            string dispatchInput = useExistingReader ? "reader" : "data";
            _ = sb.AppendLine($"if (state == CborReaderState.Tag)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    var tag = (int)reader.PeekTag();");
            _ = sb.AppendLine($"    return tag switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {tagValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read({dispatchInput}),");
            }
            _ = sb.AppendLine($"        _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected tag\")");
            _ = sb.AppendLine("    };");
            _ = sb.AppendLine("}");
        }

        private static void EmitStateBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool useExistingReader)
        {
            string dispatchInput = useExistingReader ? "reader" : "data";
            _ = sb.AppendLine($"return state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => !g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read({dispatchInput}),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected state \" + state)");
            _ = sb.AppendLine("};");
        }

        private static void EmitTagOnlyBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool useExistingReader)
        {
            string dispatchInput = useExistingReader ? "reader" : "data";
            _ = sb.AppendLine($"if (state == CborReaderState.Tag)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    var tag = (int)reader.PeekTag();");
            _ = sb.AppendLine($"    return tag switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {tagValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read({dispatchInput}),");
            }
            _ = sb.AppendLine($"        _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected tag\")");
            _ = sb.AppendLine("    };");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected tag\");");
        }

        private static void EmitStateOnlyBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups, bool useExistingReader)
        {
            string dispatchInput = useExistingReader ? "reader" : "data";
            _ = sb.AppendLine($"return state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read({dispatchInput}),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected state \" + state)");
            _ = sb.AppendLine("};");
        }

        private static string GetProbeKey(SerializableTypeMetadata child)
        {
            // CborConstr types use specific CBOR tags (121+index for 0-6, 1280+index-7 for 7+)
            if (child.SerializationType == SerializationType.Constr && child.CborIndex is not null && child.CborIndex >= 0)
            {
                int tag = Emitter.ResolveTag(child.CborIndex);
                return $"tag:{tag}";
            }

            // Explicit CborTag attribute
            if (child.CborTag is not null)
            {
                return $"tag:{child.CborTag}";
            }

            // By SerializationType
            return child.SerializationType switch
            {
                SerializationType.List => "array",
                SerializationType.Map => "map",
                SerializationType.Constr => "array",
                SerializationType.Container => GetContainerProbeKey(child),
                SerializationType.Union => "unknown",
                _ => "unknown"
            };
        }

        private static string GetContainerProbeKey(SerializableTypeMetadata child)
        {
            if (child.Properties.Count == 1)
            {
                string propType = child.Properties[0].PropertyTypeFullName.Replace("?", "");
                if (propType is "int" or "long" or "uint" or "ulong")
                {
                    return "integer";
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
                if (propType.StartsWith("Dictionary<", StringComparison.Ordinal)
                    || propType.StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal))
                {
                    return "map";
                }
                if (propType.StartsWith("List<", StringComparison.Ordinal)
                    || propType.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal))
                {
                    return "array";
                }
                if (propType.StartsWith("global::System.Collections.Generic.Dictionary<", StringComparison.Ordinal))
                {
                    return "map";
                }
                if (propType.StartsWith("global::System.Collections.Generic.List<", StringComparison.Ordinal))
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
                "array" => "CborReaderState.StartArray",
                "map" => "CborReaderState.StartMap",
                "integer" => "CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger",
                "text" => "CborReaderState.TextString",
                "bytes" => "CborReaderState.ByteString",
                "boolean" => "CborReaderState.Boolean",
                _ => throw new InvalidOperationException($"Unexpected probe key: {probeKey}")
            };
        }

        private static StringBuilder EmitTryCatchReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = sb.AppendLine("Exception? lastError = null;");
            foreach (SerializableTypeMetadata childType in metadata.ChildTypes)
            {
                _ = sb.AppendLine($"try");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"return ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read(data);");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine($"catch (Exception ex)");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("lastError = ex;");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}\", lastError);");

            return sb;
        }
    }
}
