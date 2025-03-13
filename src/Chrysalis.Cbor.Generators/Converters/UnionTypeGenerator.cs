using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Chrysalis.Cbor.Generators.Models;
using Chrysalis.Cbor.Generators.Utils;

namespace Chrysalis.Cbor.Generators.Converters
{
    /// <summary>
    /// Generates CBOR read/write code fragments for union types (i.e. types marked with [CborUnion]).
    /// </summary>
    public class UnionTypeGenerator() : ICborTypeGenerator
    {
        public string GenerateSerializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Union)
                throw new InvalidOperationException("UnionTypeGenerator only handles union types.");

            StringBuilder writer = new();

            // For a union type, we need to check which variant we have and serialize it accordingly
            writer.AppendLine("// Determine the concrete type and call its Write method");
            writer.AppendLine("Type dataType = data.GetType();");

            // We need to check each variant type
            bool isFirst = true;
            foreach (var variantTypeRef in spec.UnionCases)
            {
                string condition = isFirst ? "if" : "else if";
                isFirst = false;

                writer.AppendLine($"{condition} (dataType == typeof({variantTypeRef.FullyQualifiedName}))");
                writer.AppendLine("{");
                writer.AppendLine($"    {variantTypeRef.FullyQualifiedName}.Write(writer, ({variantTypeRef.FullyQualifiedName})data);");
                writer.AppendLine("}");
            }

            // Add a default case for unexpected variants
            writer.AppendLine("else");
            writer.AppendLine("{");
            writer.AppendLine("    throw new InvalidOperationException($\"Unknown union variant: {dataType}\");");
            writer.AppendLine("}");

            return writer.ToString();
        }

        public string GenerateDeserializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Union)
                throw new InvalidOperationException("UnionTypeGenerator only handles union types.");

            StringBuilder writer = new();

            // For union deserialization, we need to try each variant type
            writer.AppendLine("// We need to try each variant type");
            writer.AppendLine("// First, get the encoded CBOR data at the current position");
            writer.AppendLine("var encodedValue = reader.ReadEncodedValue();");
            writer.AppendLine("var valueBytes = encodedValue.ToArray();");

            // Try each variant type
            writer.AppendLine("Exception lastException = null;");

            foreach (var variantTypeRef in spec.UnionCases)
            {
                writer.AppendLine($"// Try to deserialize as {variantTypeRef.Name}");
                writer.AppendLine("try");
                writer.AppendLine("{");
                writer.AppendLine("    // Create a new reader with a copy of the encoded data");
                writer.AppendLine("    var variantReader = new CborReader(valueBytes);");
                writer.AppendLine($"    // Try to deserialize as {variantTypeRef.Name}");
                writer.AppendLine($"    var result = {variantTypeRef.FullyQualifiedName}.Read(variantReader);");
                writer.AppendLine("    // If we get here, the deserialization succeeded");
                writer.AppendLine("    return result;");
                writer.AppendLine("}");
                writer.AppendLine("catch (Exception ex)");
                writer.AppendLine("{");
                writer.AppendLine("    // This variant didn't work, save the exception and try the next one");
                writer.AppendLine("    lastException = ex;");
                writer.AppendLine("}");
            }

            // If we get here, none of the variants worked
            writer.AppendLine("// None of the variants worked");
            writer.AppendLine("throw new InvalidOperationException(\"Could not deserialize union type, none of the variants matched\", lastException);");

            return writer.ToString();
        }
    }
}