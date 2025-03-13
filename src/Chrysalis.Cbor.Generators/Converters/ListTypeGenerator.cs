using System.Text;
using Chrysalis.Cbor.Generators.Models;
using Chrysalis.Cbor.Generators.Utils;

namespace Chrysalis.Cbor.Generators.Converters
{
    /// <summary>
    /// Generates CBOR read/write code fragments for custom list types (i.e. types marked with [CborList]).
    /// </summary>
    public class ListTypeGenerator : ICborTypeGenerator
    {
        public string GenerateSerializer(CborTypeGenerationSpec spec)
        {
            // This generator handles types serialized as CBOR arrays.
            if (spec.Category != CborTypeCategory.Array && spec.Category != CborTypeCategory.Constr)
                throw new InvalidOperationException("ListTypeGenerator only handles array types.");

            StringBuilder writer = new();

            // If it's a constructor type, write out the constructor tag.
            if (spec.Category == CborTypeCategory.Constr && spec.Constructor.HasValue)
            {
                // For constr types, write out the constructor tag.
                writer.AppendLine($"writer.WriteTag((CborTag){spec.Constructor.Value});");
            }

            // If the type has a tag, output the tag call first.
            if (spec.Tag.HasValue)
            {
                writer.AppendLine($"writer.WriteTag({spec.Tag.Value});");
            }

            // Use definite or indefinite array start based on the spec.
            if (spec.IsDefinite)
            {
                // Passing the number of constructor parameters as the array length.
                // for each property that is not null
                writer.AppendLine($"int nonNullCount = {spec.Properties.Count(p => p.PropertyType.IsValueType)};");
                foreach (var prop in spec.Properties.Where(p => !p.PropertyType.IsValueType))
                {
                    writer.AppendLine($"if (data.{prop.Name} != null) nonNullCount++;");
                }

                writer.AppendLine($"writer.WriteStartArray(nonNullCount);");
            }
            else
            {
                writer.AppendLine("writer.WriteStartArray(null);");
            }

            // Iterate over properties in order (assuming Order is set via [CborOrder])
            IOrderedEnumerable<PropertyGenerationSpec> sortedProps = spec.Properties.OrderBy(p => p.Order ?? 0);
            foreach (PropertyGenerationSpec prop in sortedProps)
            {
                // Generate the write call for each property.
                if (!prop.PropertyType.IsValueType)
                {
                    writer.AppendLine($"if (data.{prop.Name} != null)");
                    writer.AppendLine("{");
                    writer.AppendLine(GenerateWriteCallForProperty(prop));
                    writer.AppendLine("}");
                }
                else
                {
                    writer.AppendLine(GenerateWriteCallForProperty(prop));
                }

            }

            writer.AppendLine("writer.WriteEndArray();");

            return writer.ToString();
        }

        public string GenerateDeserializer(CborTypeGenerationSpec spec)
        {
            if (spec.Category != CborTypeCategory.Array && spec.Category != CborTypeCategory.Constr)
                throw new InvalidOperationException("ListTypeGenerator only handles array types.");

            StringBuilder writer = new();

            // Read and validate the tag.
            if (spec.Category == CborTypeCategory.Constr && spec.Constructor.HasValue)
            {
                writer.AppendLine($"var constrTag = reader.ReadTag();");
                writer.AppendLine($"if (constrTag != {spec.Constructor.Value}) throw new Exception(\"Unexpected constructor tag.\");");
            }

            // If a tag is present, read and validate it.
            if (spec.Tag.HasValue)
            {
                writer.AppendLine($"var tag = reader.ReadTag();");
                writer.AppendLine($"if (tag != {spec.Tag.Value}) throw new Exception(\"Unexpected t tag.\");");
            }

            // Start reading the array.
            if (spec.IsDefinite)
            {
                writer.AppendLine("reader.ReadStartArray();");
            }
            else
            {
                writer.AppendLine("reader.ReadStartArray(null);");
            }

            // Read each property in order.
            IOrderedEnumerable<PropertyGenerationSpec> sortedProps = spec.Properties.OrderBy(p => p.Order ?? 0);
            foreach (PropertyGenerationSpec? prop in sortedProps)
            {
                writer.AppendLine(GenerateReadCallForProperty(prop));
            }

            writer.AppendLine("reader.ReadEndArray();");

            // Create the object from the read properties.
            writer.AppendLine($"return new {spec.TypeRef.FullyQualifiedName}(");
            foreach (PropertyGenerationSpec prop in sortedProps)
            {
                // if last element, dont add comma
                if (prop == sortedProps.Last())
                    writer.AppendLine($"{prop.Name}: {prop.Name}");
                else
                    writer.AppendLine($"{prop.Name}: {prop.Name},");

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

            // For primitives and collections, use our utilities
            if (CborPrimitiveUtil.IsPrimitive(fullyQualifiedName))
            {
                return CborPrimitiveUtil.GetWriteCall($"data.{prop.Name}", fullyQualifiedName);
            }
            else
            {
                // For custom types, assume a static Write method exists
                return $"{fullyQualifiedName}.Write(writer, data.{prop.Name});";
            }
        }

        /// <summary>
        /// Generates the CBOR read call for a given property.
        /// </summary>
        private string GenerateReadCallForProperty(PropertyGenerationSpec prop)
        {
            string fullyQualifiedName = prop.PropertyType.FullyQualifiedName;

            // For primitives and collections, use our utilities
            if (CborPrimitiveUtil.IsPrimitive(fullyQualifiedName))
            {
                // Generates a read snippet assigning to a local variable.
                return CborPrimitiveUtil.GetReadCall($"var {prop.Name}", fullyQualifiedName);
            }
            else
            {
                // For custom types, assume the type has its own Read method
                return $"var {prop.Name} = {fullyQualifiedName}.Read(reader);";
            }
        }
    }
}