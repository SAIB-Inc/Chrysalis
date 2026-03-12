using Chrysalis.Blueprint.CodeGen.Generation;
using Chrysalis.Blueprint.CodeGen.Models;

namespace Chrysalis.Blueprint.CodeGen.Analysis;

/// <summary>
/// Classifies a schema node into a C# type category and resolves its fields.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TypeClassifier"/> class.
/// </remarks>
internal sealed class TypeClassifier(SchemaResolver resolver)
{
    private readonly SchemaResolver _resolver = resolver;

    /// <summary>
    /// Classifies a definition into a resolved type with category and fields.
    /// </summary>
    public ResolvedType Classify(string defKey, SchemaNode schema)
    {
        string typeName = NamingConventions.TypeNameFromDefinitionKey(defKey, schema);

        if (schema.DataType == "bytes")
        {
            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = typeName,
                Category = TypeCategory.PrimitiveBytes,
                Schema = schema
            };
        }

        if (schema.DataType == "integer")
        {
            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = typeName,
                Category = TypeCategory.PrimitiveInteger,
                Schema = schema
            };
        }

        if (schema.DataType == "list")
        {
            return ClassifyList(defKey, typeName, schema);
        }

        if (schema.AnyOf != null && schema.AnyOf.Count > 0)
        {
            return ClassifyAnyOf(defKey, typeName, schema);
        }

        return new ResolvedType
        {
            DefinitionKey = defKey,
            TypeName = typeName,
            Category = TypeCategory.PrimitiveData,
            Schema = schema
        };
    }

    private ResolvedType ClassifyList(string defKey, string typeName, SchemaNode schema)
    {
        if (schema.Items != null && schema.Items.Count > 0)
        {
            List<ResolvedField> fields = [];
            for (int i = 0; i < schema.Items.Count; i++)
            {
                SchemaNode item = schema.Items[i];
                string fieldType = _resolver.GetCSharpTypeForSchema(item);
                string fieldName = item.Title != null
                    ? NamingConventions.ToPascalCase(item.Title)
                    : $"Field{i}";

                fields.Add(new ResolvedField
                {
                    Name = fieldName,
                    Order = i,
                    CSharpType = fieldType
                });
            }

            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = typeName,
                Category = TypeCategory.Tuple,
                Schema = schema,
                TupleFields = fields
            };
        }

        if (schema.ItemsSchema != null)
        {
            string elemType = _resolver.GetCSharpTypeForSchema(schema.ItemsSchema);
            ResolvedType elemResolved = new()
            {
                TypeName = elemType,
                Category = TypeCategory.Ref
            };

            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = typeName,
                Category = TypeCategory.List,
                Schema = schema,
                ListElementType = elemResolved
            };
        }

        return new ResolvedType
        {
            DefinitionKey = defKey,
            TypeName = typeName,
            Category = TypeCategory.List,
            Schema = schema
        };
    }

    private ResolvedType ClassifyAnyOf(string defKey, string typeName, SchemaNode schema)
    {
        List<SchemaNode> constructors = schema.AnyOf!;

        if (IsBoolPattern(schema))
        {
            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = typeName,
                Category = TypeCategory.PrimitiveBool,
                Schema = schema
            };
        }

        if (IsOptionPattern(schema))
        {
            SchemaNode someConstructor = constructors[0];
            ResolvedType? innerType = null;
            if (someConstructor.Fields != null && someConstructor.Fields.Count == 1)
            {
                SchemaNode innerField = someConstructor.Fields[0];
                string innerTypeName = _resolver.GetCSharpTypeForSchema(innerField);
                innerType = new ResolvedType
                {
                    TypeName = innerTypeName,
                    Category = TypeCategory.Ref
                };
            }

            return new ResolvedType
            {
                DefinitionKey = defKey,
                TypeName = typeName,
                Category = TypeCategory.Option,
                Schema = schema,
                OptionInnerType = innerType
            };
        }

        if (constructors.Count == 1)
        {
            return ClassifySingleConstructor(defKey, typeName, schema, constructors[0]);
        }

        return ClassifyUnion(defKey, typeName, schema, constructors);
    }

    private ResolvedType ClassifySingleConstructor(string defKey, string typeName, SchemaNode schema, SchemaNode ctor)
    {
        ResolvedConstructor rCtor = new()
        {
            Name = typeName,
            Index = ctor.Index ?? 0,
            Fields = ResolveFields(ctor)
        };

        return new ResolvedType
        {
            DefinitionKey = defKey,
            TypeName = typeName,
            Category = TypeCategory.SingleConstructor,
            Schema = schema,
            Constructors = [rCtor]
        };
    }

    private ResolvedType ClassifyUnion(string defKey, string typeName, SchemaNode schema, List<SchemaNode> constructors)
    {
        List<ResolvedConstructor> resolvedCtors = [];

        foreach (SchemaNode ctor in constructors)
        {
            string ctorName = ctor.Title != null
                ? NamingConventions.ToPascalCase(ctor.Title)
                : $"{typeName}Variant{ctor.Index ?? 0}";

            resolvedCtors.Add(new ResolvedConstructor
            {
                Name = ctorName,
                Index = ctor.Index ?? 0,
                Fields = ResolveFields(ctor)
            });
        }

        return new ResolvedType
        {
            DefinitionKey = defKey,
            TypeName = typeName,
            Category = TypeCategory.Union,
            Schema = schema,
            Constructors = resolvedCtors
        };
    }

    private List<ResolvedField> ResolveFields(SchemaNode ctor)
    {
        List<ResolvedField> fields = [];
        if (ctor.Fields == null)
        {
            return fields;
        }

        for (int i = 0; i < ctor.Fields.Count; i++)
        {
            SchemaNode field = ctor.Fields[i];
            string fieldType = _resolver.GetCSharpTypeForSchema(field);

            string fieldName = field.Title != null
                ? NamingConventions.ToPascalCase(field.Title)
                : $"Field{i}";

            bool isNullable = fieldType.StartsWith("ICborOption<", StringComparison.Ordinal);

            fields.Add(new ResolvedField
            {
                Name = fieldName,
                Order = i,
                CSharpType = fieldType,
                IsNullable = isNullable
            });
        }

        return fields;
    }

    private static bool IsBoolPattern(SchemaNode schema)
    {
        if (schema.AnyOf == null || schema.AnyOf.Count != 2 || schema.Title != "Bool")
        {
            return false;
        }

        SchemaNode first = schema.AnyOf[0];
        SchemaNode second = schema.AnyOf[1];
        return first.Title == "False" && second.Title == "True"
            && first.Index == 0 && second.Index == 1
            && (first.Fields == null || first.Fields.Count == 0)
            && (second.Fields == null || second.Fields.Count == 0);
    }

    private static bool IsOptionPattern(SchemaNode schema)
    {
        if (schema.AnyOf == null || schema.AnyOf.Count != 2)
        {
            return false;
        }

        SchemaNode first = schema.AnyOf[0];
        SchemaNode second = schema.AnyOf[1];

        bool isSomeNone = first.Title == "Some" && second.Title == "None"
            && first.Index == 0 && second.Index == 1
            && first.Fields != null && first.Fields.Count == 1
            && (second.Fields == null || second.Fields.Count == 0);

        if (isSomeNone)
        {
            return true;
        }

        return schema.Title != null
            && (schema.Title.StartsWith("Optional", StringComparison.Ordinal)
                || schema.Title == "Option");
    }
}
