using System.Globalization;
using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class UnionEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            if (useExistingReader)
            {
                _ = sb.AppendLine("var data = reader.ReadEncodedValue(true);");
                _ = sb.AppendLine($"return {metadata.FullyQualifiedName}.Read(data);");
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
                _ = sb.AppendLine($"{childType.FullyQualifiedName}.Write(writer, ({childType.FullyQualifiedName})data);");
                _ = sb.AppendLine($"break;");
            }
            _ = sb.AppendLine($"default:");
            _ = sb.AppendLine($"throw new Exception(\"Union serialization failed. {metadata.FullyQualifiedName} \");");
            _ = sb.AppendLine("}");
            return sb;
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
        private static bool TryEmitStructuralProbeReader(StringBuilder sb, SerializableTypeMetadata metadata)
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

            _ = sb.AppendLine($"var reader = new CborReader(data, CborConformanceMode.Lax);");
            _ = sb.AppendLine($"var state = reader.PeekState();");

            if (hasTagChildren && hasNonTagChildren)
            {
                EmitTagBranch(sb, metadata, probeGroups);
                EmitStateBranch(sb, metadata, probeGroups);
            }
            else if (hasTagChildren)
            {
                EmitTagOnlyBranch(sb, metadata, probeGroups);
            }
            else
            {
                EmitStateOnlyBranch(sb, metadata, probeGroups);
            }

            return true;
        }

        private static void EmitTagBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"if (state == CborReaderState.Tag)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    var tag = (int)reader.PeekTag();");
            _ = sb.AppendLine($"    return tag switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {tagValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data),");
            }
            _ = sb.AppendLine($"        _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected tag\")");
            _ = sb.AppendLine("    };");
            _ = sb.AppendLine("}");
        }

        private static void EmitStateBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"return state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups.Where(g => !g.Key.StartsWith("tag:", StringComparison.Ordinal)))
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data),");
            }
            _ = sb.AppendLine($"    _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected state \" + state)");
            _ = sb.AppendLine("};");
        }

        private static void EmitTagOnlyBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"if (state == CborReaderState.Tag)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"    var tag = (int)reader.PeekTag();");
            _ = sb.AppendLine($"    return tag switch");
            _ = sb.AppendLine("    {");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                int tagValue = int.Parse(group.Key.Substring(4), CultureInfo.InvariantCulture);
                _ = sb.AppendLine($"        {tagValue} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data),");
            }
            _ = sb.AppendLine($"        _ => throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: unexpected tag\")");
            _ = sb.AppendLine("    };");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName}: expected tag\");");
        }

        private static void EmitStateOnlyBranch(StringBuilder sb, SerializableTypeMetadata metadata, Dictionary<string, List<SerializableTypeMetadata>> probeGroups)
        {
            _ = sb.AppendLine($"return state switch");
            _ = sb.AppendLine("{");
            foreach (KeyValuePair<string, List<SerializableTypeMetadata>> group in probeGroups)
            {
                string statePattern = GetCborReaderStatePattern(group.Key);
                _ = sb.AppendLine($"    {statePattern} => ({metadata.FullyQualifiedName}){group.Value[0].FullyQualifiedName}.Read(data),");
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
