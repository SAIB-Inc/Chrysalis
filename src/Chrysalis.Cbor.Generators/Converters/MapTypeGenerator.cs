using System;
using System.Linq;
using System.Text;
using Chrysalis.Cbor.Generators.Models;
using Chrysalis.Cbor.Generators.Utils;

namespace Chrysalis.Cbor.Generators.Converters
{
    /// <summary>
    /// Generates CBOR read/write code fragments for map types (i.e. types marked with [CborMap]).
    /// </summary>
    public class MapTypeGenerator : ICborTypeGenerator
    {
        public string GenerateSerializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Map)
                throw new InvalidOperationException("MapTypeGenerator only handles map types.");

            StringBuilder writer = new();

            // Write tag if present.
            if (spec.Tag.HasValue)
            {
                writer.AppendLine($"\t writer.WriteTag((CborTag){spec.Tag.Value});");
            }

            if (spec.IsDefinite)
            {
                // Calculate map size: count value types + non-null reference types
                int valueTypeCount = spec.Properties.Count(p => p.PropertyType.IsValueType);

                // Keep the dynamic counting of non-null reference types, but exclude null check if the containing type
                // is marked as CborNullable (parent category is Nullable)
                if (spec.Category == CborTypeCategory.Nullable)
                {
                    // For nullable types, include all properties in the count
                    writer.AppendLine($"int mapSize = {spec.Properties.Count};");
                }
                else
                {
                    // For regular map types, count value types and only non-null reference types
                    writer.AppendLine($"int nonNullCount = {valueTypeCount};");
                    foreach (var prop in spec.Properties.Where(p => !p.PropertyType.IsValueType))
                    {
                        writer.AppendLine($"if (data.{prop.Name} != null) nonNullCount++;");
                    }
                    writer.AppendLine("writer.WriteStartMap(nonNullCount);");
                }
            }
            else
            {
                writer.AppendLine("writer.WriteStartMap(null);");
            }

            // Iterate over properties in order (based on [CborOrder])
            var sortedProps = spec.Properties.OrderBy(p => p.Order ?? 0);
            foreach (PropertyGenerationSpec prop in sortedProps)
            {
                // For non-nullable types, only write properties that aren't null
                if (!prop.PropertyType.IsValueType && spec.Category != CborTypeCategory.Nullable)
                {
                    writer.AppendLine($"if (data.{prop.Name} != null)");
                    writer.AppendLine("{");
                    writer.AppendLine($"    writer.WriteTextString(\"{prop.Key}\");");
                    writer.AppendLine("    " + GenerateWriteCallForProperty(prop));
                    writer.AppendLine("}");
                }
                else
                {
                    // For value types or nullable types, always write the property
                    writer.AppendLine($"writer.WriteTextString(\"{prop.Key}\");");

                    // If it's a reference type, still need to check for null when writing the value
                    if (!prop.PropertyType.IsValueType)
                    {
                        writer.AppendLine($"if (data.{prop.Name} != null)");
                        writer.AppendLine("{");
                        writer.AppendLine("    " + GenerateWriteCallForProperty(prop));
                        writer.AppendLine("}");
                        writer.AppendLine("else");
                        writer.AppendLine("{");
                        writer.AppendLine("    writer.WriteNull();");
                        writer.AppendLine("}");
                    }
                    else
                    {
                        writer.AppendLine(GenerateWriteCallForProperty(prop));
                    }
                }
            }

            writer.AppendLine("writer.WriteEndMap();");
            return writer.ToString();
        }

        public string GenerateDeserializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Map)
                throw new InvalidOperationException("MapTypeGenerator only handles map types.");

            StringBuilder writer = new();

            // If a tag is defined, read and verify it.
            if (spec.Tag.HasValue)
            {
                writer.AppendLine($"reader.ReadTag({spec.Tag.Value});");
            }

            writer.AppendLine("reader.ReadStartMap();");

            // Declare local variables to hold the property values.
            foreach (PropertyGenerationSpec prop in spec.Properties.OrderBy(p => p.Order ?? 0))
            {
                string alias = prop.PropertyType.FullyQualifiedName;
                writer.AppendLine($"{alias} {prop.Name} = default;");
            }

            writer.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            writer.AppendLine("{");
            writer.AppendLine("    string key = reader.ReadTextString();");
            writer.AppendLine("    switch(key)");
            writer.AppendLine("    {");
            foreach (PropertyGenerationSpec prop in spec.Properties.OrderBy(p => p.Order ?? 0))
            {
                writer.AppendLine($"        case \"{prop.Key}\":");

                string fullyQualifiedName = prop.PropertyType.FullyQualifiedName;

                // Check for null values
                writer.AppendLine("            if (reader.PeekState() == CborReaderState.Null)");
                writer.AppendLine("            {");
                writer.AppendLine("                reader.ReadNull();");
                writer.AppendLine($"                {prop.Name} = default;");
                writer.AppendLine("            }");
                writer.AppendLine("            else");
                writer.AppendLine("            {");

                if (CborPrimitiveUtil.IsPrimitive(fullyQualifiedName))
                {
                    // If it's a primitive or collection
                    if (CborPrimitiveUtil.IsStandardCollection(fullyQualifiedName))
                    {
                        // If it's a collection, we need special handling
                        writer.AppendLine("                " + CborPrimitiveUtil.GetReadCall(prop.Name, fullyQualifiedName));
                    }
                    else
                    {
                        // Regular primitive
                        writer.AppendLine("                " + CborPrimitiveUtil.GetReadCall(prop.Name, fullyQualifiedName));
                    }
                }
                else
                {
                    // Custom type with its own Read method
                    writer.AppendLine($"                {prop.Name} = {fullyQualifiedName}.Read(reader);");
                }

                writer.AppendLine("            }");
                writer.AppendLine("            break;");
            }
            writer.AppendLine("        default:");
            writer.AppendLine($"             throw new Exception(\"Invalid key found in CBOR map.\");");
            writer.AppendLine("    }");
            writer.AppendLine("}");
            writer.AppendLine("reader.ReadEndMap();");

            // Construct the object using a constructor with named arguments.
            writer.AppendLine($"return new {spec.TypeRef.FullyQualifiedName}(");
            var sorted = spec.Properties.OrderBy(p => p.Order ?? 0).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var prop = sorted[i];
                string separator = i < sorted.Count - 1 ? "," : "";
                writer.AppendLine($"    {prop.Name}: {prop.Name}{separator}");
            }
            writer.AppendLine(");");

            return writer.ToString();
        }

        /// <summary>
        /// Generates the CBOR write call for a given property.
        /// </summary>
        private string GenerateWriteCallForProperty(PropertyGenerationSpec prop)
        {
            string fullyQualifiedName = prop.PropertyType.FullyQualifiedName;

            if (CborPrimitiveUtil.IsPrimitive(fullyQualifiedName))
            {
                return CborPrimitiveUtil.GetWriteCall($"data.{prop.Name}", fullyQualifiedName);
            }
            else
            {
                return $"{fullyQualifiedName}.Write(writer, data.{prop.Name});";
            }
        }
    }
}