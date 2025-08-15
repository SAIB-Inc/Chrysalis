using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private interface ICborSerializerEmitter
    {
        StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata);
        StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata);
    }

    private static partial class Emitter
    {
        public const string GenericSerializationUtilFullname = "global::Chrysalis.Cbor.Serialization.Utils.GenericSerializationUtil";
        public const string CborEncodeValueFullName = "global::Chrysalis.Cbor.Types.CborEncodedValue";

        public static void EmitSerializerAndMetadata(SourceProductionContext context, SerializableTypeMetadata metadata)
        {
            // Metadata emission
            context.AddSource($"{metadata?.FullyQualifiedName.Replace("<", "`").Replace(">", "`")}.Metadata.g.cs", metadata?.ToString()!);

            // Serializer emission
            StringBuilder sb = new();
            sb.AppendLine("// Automatically generated file");
            sb.AppendLine("#pragma warning disable CS0109, CS8669");
            sb.AppendLine();
            sb.AppendLine("using System.Formats.Cbor;");
            sb.AppendLine();

            if (metadata?.Namespace is not null)
            {
                sb.AppendLine($"namespace {metadata?.Namespace};");
            }

            sb.AppendLine($"public partial {metadata?.Keyword} {metadata?.Indentifier}");
            sb.AppendLine("{");

            // Type Mapping
            if (metadata?.SerializationType is SerializationType.Map)
            {
                bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;
                sb.AppendLine($"static Dictionary<{(isIntKey ? "int" : "string")}, Type> TypeMapping = new()");
                sb.AppendLine("{");

                List<SerializablePropertyMetadata> properties = [.. metadata.Properties];
                for (int i = 0; i < properties.Count; i++)
                {
                    SerializablePropertyMetadata prop = properties[i];
                    string? keyValue = isIntKey ? prop.PropertyKeyInt?.ToString() : prop.PropertyKeyString;

                    if (i == properties.Count - 1)
                    {
                        sb.AppendLine($"  {{{keyValue}, typeof({prop.PropertyTypeFullName})}}");
                    }
                    else
                    {
                        sb.AppendLine($"  {{{keyValue}, typeof({prop.PropertyTypeFullName})}},");
                    }
                }

                sb.AppendLine("};");
                sb.AppendLine();
            }

            // Property Key Mapping
            if (metadata?.SerializationType is SerializationType.Map)
            {
                bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;
                sb.AppendLine($"static Dictionary<string, {(isIntKey ? "int" : "string")}> KeyMapping = new()");
                sb.AppendLine("{");

                List<SerializablePropertyMetadata> properties = [.. metadata.Properties];
                for (int i = 0; i < properties.Count; i++)
                {
                    SerializablePropertyMetadata prop = properties[i];
                    string? keyValue = $"\"{prop.PropertyName}\"";
                    string? value = isIntKey ? prop.PropertyKeyInt?.ToString() : prop.PropertyKeyString;

                    if (i == properties.Count - 1)
                    {
                        sb.AppendLine($"  {{{keyValue}, {value}}}");
                    }
                    else
                    {
                        sb.AppendLine($"  {{{keyValue}, {value}}},");
                    }
                }

                sb.AppendLine("};");
                sb.AppendLine();
            }

            EmitSerializableTypeReader(sb, metadata!);

            sb.AppendLine();

            EmitSerializableTypeWriter(sb, metadata!);

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#pragma warning restore CS0109, CS8669");

            context.AddSource($"{metadata?.FullyQualifiedName.Replace("<", "`").Replace(">", "`")}.Serializer.g.cs", sb.ToString());
        }

        private static ICborSerializerEmitter GetEmitter(SerializableTypeMetadata metadata)
        {
            return metadata.SerializationType switch
            {
                SerializationType.Constr => new ConstructorEmitter(),
                SerializationType.Container => new ContainerEmitter(),
                SerializationType.List => new ListEmitter(),
                SerializationType.Map => new MapEmitter(),
                SerializationType.Union => new UnionEmitter(),
                _ => throw new NotSupportedException($"Serialization type {metadata.SerializationType} is not supported.")
            };
        }

        public static int ResolveTag(int? index)
        {
            if (index is null || index < 0)
            {
                return -1;
            }
            else
            {
                int finalIndex = index > 6 ? 1280 - 7 : 121;
                return finalIndex + (index ?? 0);
            }
        }

        private static bool IsPrimitiveType(string type) => type.Replace("?", "") switch
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
            "CborLabel" => true,
            "Chrysalis.Cbor.Types.CborLabel" => true,
            "global::Chrysalis.Cbor.Types.CborLabel" => true,
            _ => false
        };
    }
}
