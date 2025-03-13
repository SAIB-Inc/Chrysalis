using System;
using System.Text;
using Chrysalis.Cbor.Generators.Models;

namespace Chrysalis.Cbor.Generators.Converters
{
    /// <summary>
    /// Generates CBOR read/write code fragments for nullable types (types marked with [CborNullable]).
    /// </summary>
    public class NullableTypeGenerator(ICborTypeGenerator innerGenerator) : ICborTypeGenerator
    {
        public string GenerateSerializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Nullable)
                throw new InvalidOperationException("NullableTypeGenerator only handles nullable types.");

            StringBuilder writer = new();

            // Check for null first
            writer.AppendLine("// Handle null case");
            writer.AppendLine("if (data == null)");
            writer.AppendLine("{");
            writer.AppendLine("    writer.WriteNull();");
            writer.AppendLine("    return;");
            writer.AppendLine("}");
            writer.AppendLine();

            // If not null, use the inner generator
            writer.AppendLine("// Handle non-null case");
            writer.AppendLine(innerGenerator.GenerateSerializer(spec));

            return writer.ToString();
        }

        public string GenerateDeserializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Nullable)
                throw new InvalidOperationException("NullableTypeGenerator only handles nullable types.");

            StringBuilder writer = new();

            // Check for null first
            writer.AppendLine("// Check for null");
            writer.AppendLine("if (reader.PeekState() == CborReaderState.Null)");
            writer.AppendLine("{");
            writer.AppendLine("    reader.ReadNull();");
            writer.AppendLine("    return null;");
            writer.AppendLine("}");
            writer.AppendLine();

            // If not null, use the inner generator
            writer.AppendLine("// Handle non-null case");
            writer.AppendLine("return " + innerGenerator.GenerateDeserializer(spec));

            return writer.ToString();
        }
    }
}