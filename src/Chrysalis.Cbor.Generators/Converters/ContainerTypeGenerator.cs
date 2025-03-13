using System;
using System.Linq;
using System.Text;
using Chrysalis.Cbor.Generators.Models;
using Chrysalis.Cbor.Generators.Utils;

namespace Chrysalis.Cbor.Generators.Converters
{
    /// <summary>
    /// Generates CBOR read/write code fragments for types that simply pass through to their inner value.
    /// </summary>
    public class ContainerTypeGenerator : ICborTypeGenerator
    {
        public string GenerateSerializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Container)
                throw new InvalidOperationException("PassThroughTypeGenerator only handles pass-through types.");

            StringBuilder writer = new();

            // Get the contained property (there should be just one)
            PropertyGenerationSpec? containerProp = spec.Properties.FirstOrDefault()
                ?? throw new InvalidOperationException("Pass-through type must have at least one property.");

            // Check if the type has a tag and write it
            if (spec.Tag.HasValue)
            {
                writer.AppendLine($"// Write the tag if specified");
                writer.AppendLine($"writer.WriteTag((CborTag){spec.Tag.Value});");
                writer.AppendLine();
            }

            // Check for null
            writer.AppendLine($"if (data.{containerProp.Name} == null)");
            writer.AppendLine("{");
            writer.AppendLine("    writer.WriteNull();");
            writer.AppendLine("    return;");
            writer.AppendLine("}");
            writer.AppendLine();

            // Determine how to serialize the contained value based on its type
            string containerType = containerProp.PropertyType.FullyQualifiedName;

            writer.AppendLine("// Serialize the inner value directly");
            if (CborPrimitiveUtil.IsPrimitive(containerType))
            {
                // If it's a primitive or collection, use the primitive util
                writer.AppendLine(CborPrimitiveUtil.GetWriteCall($"data.{containerProp.Name}", containerType));
            }
            else
            {
                // If it's a custom type, call its Write method
                writer.AppendLine($"{containerType}.Write(writer, data.{containerProp.Name});");
            }

            return writer.ToString();
        }

        public string GenerateDeserializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Container)
                throw new InvalidOperationException("PassThroughTypeGenerator only handles pass-through types.");

            StringBuilder writer = new();

            // Get the contained property
            PropertyGenerationSpec? containerProp = spec.Properties.FirstOrDefault()
                ?? throw new InvalidOperationException("Pass-through type must have at least one property.");

            // Check for tag
            if (spec.Tag.HasValue)
            {
                writer.AppendLine($"// Read and validate the tag if present");
                writer.AppendLine($"var tag = reader.ReadTag();");
                writer.AppendLine($"if (tag != {spec.Tag.Value})");
                writer.AppendLine($"    throw new Exception($\"Expected tag {spec.Tag.Value}, but got {{tag}}\");");
                writer.AppendLine();
            }

            // Check for null
            writer.AppendLine("// Check for null");
            writer.AppendLine("if (reader.PeekState() == CborReaderState.Null)");
            writer.AppendLine("{");
            writer.AppendLine("    reader.ReadNull();");
            writer.AppendLine("    return null;");
            writer.AppendLine("}");
            writer.AppendLine();

            // Determine how to deserialize the contained value based on its type
            string containerType = containerProp.PropertyType.FullyQualifiedName;
            writer.AppendLine("// Deserialize the inner value directly");

            if (CborPrimitiveUtil.IsPrimitive(containerType))
            {
                if (CborPrimitiveUtil.IsStandardCollection(containerType))
                {
                    // For collections, we need special handling
                    writer.AppendLine($"{containerType} value = new {containerType}();");
                    writer.AppendLine(CborPrimitiveUtil.GetReadCall("value", containerType));
                }
                else
                {
                    // For simple primitives
                    writer.AppendLine($"{containerType} value = {CborPrimitiveUtil.GetReadValueCall(containerType)};");
                }
            }
            else
            {
                // For custom types, call their Read method
                writer.AppendLine($"{containerType} value = {containerType}.Read(reader);");
            }

            // Create and return the result
            writer.AppendLine($"return new {spec.TypeRef.FullyQualifiedName}(value);");

            return writer.ToString();
        }
    }
}