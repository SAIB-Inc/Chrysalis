using Chrysalis.Codec.CodeGen.Blueprint.Analysis;
using Chrysalis.Codec.CodeGen.Blueprint.Generation;
using Chrysalis.Codec.CodeGen.Blueprint.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Chrysalis.Codec.CodeGen.Blueprint;

/// <summary>
/// Incremental source generator that reads CIP-0057 blueprint JSON files
/// (added as AdditionalFiles) and generates C# types with Chrysalis CBOR attributes.
/// </summary>
[Generator]
public sealed class BlueprintSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Registers the generator pipeline to process blueprint JSON files.
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<AdditionalText> blueprintFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        IncrementalValuesProvider<BlueprintFile> parsed = blueprintFiles
            .Select(static (file, ct) =>
            {
                SourceText? text = file.GetText(ct);
                if (text == null)
                {
                    return null;
                }

                string json = text.ToString();
                if (!json.Contains("\"preamble\""))
                {
                    return null;
                }

                try
                {
                    return BlueprintParser.Parse(json);
                }
                catch (FormatException)
                {
                    return null;
                }
            })
            .Where(static bp => bp != null)!;

        context.RegisterSourceOutput(parsed, static (ctx, blueprint) =>
        {
            try
            {
                GenerateSource(ctx, blueprint!);
            }
            catch (Exception ex)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "BLUEPRINT001",
                        "Blueprint generation failed",
                        "Failed to generate types from blueprint: {0}",
                        "Chrysalis.Blueprint",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    Location.None,
                    ex.Message));
            }
        });
    }

    private static void ResolveNameCollisions(Dictionary<string, ResolvedType> resolvedTypes)
    {
        // Phase 1: Resolve type-level name collisions
        ResolveTypeCollisions(resolvedTypes);

        // Phase 2: Resolve constructor name collisions across different unions
        ResolveConstructorCollisions(resolvedTypes);

        // Phase 3: Fix fields where name matches enclosing record name (CS0542)
        FixFieldNameCollisions(resolvedTypes);
    }

    private static void ResolveTypeCollisions(Dictionary<string, ResolvedType> resolvedTypes)
    {
        Dictionary<string, List<string>> nameToKeys = [];
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            string name = kv.Value.TypeName;
            if (!nameToKeys.TryGetValue(name, out List<string>? list))
            {
                list = [];
                nameToKeys[name] = list;
            }

            list.Add(kv.Key);
        }

        foreach (KeyValuePair<string, List<string>> kv in nameToKeys)
        {
            if (kv.Value.Count <= 1)
            {
                continue;
            }

            // Disambiguate using sanitized definition key
            foreach (string defKey in kv.Value)
            {
                ResolvedType type = resolvedTypes[defKey];
                string sanitizedKey = SanitizeDefinitionKey(defKey);
                type.TypeName = sanitizedKey;
            }
        }

        // Handle remaining collisions with numeric suffix
        Dictionary<string, int> seen = [];
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            string name = kv.Value.TypeName;
            if (seen.TryGetValue(name, out int count))
            {
                kv.Value.TypeName = name + (count + 1);
                seen[name] = count + 1;
            }
            else
            {
                seen[name] = 1;
            }
        }
    }

    private static void ResolveConstructorCollisions(Dictionary<string, ResolvedType> resolvedTypes)
    {
        // Collect all emitted names: type names, interface names, constructor names
        Dictionary<string, List<(string DefKey, ResolvedType Parent, ResolvedConstructor Ctor)>> ctorNameToSources = [];

        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            if (type.Constructors == null)
            {
                continue;
            }

            foreach (ResolvedConstructor ctor in type.Constructors)
            {
                if (!ctorNameToSources.TryGetValue(ctor.Name, out List<(string, ResolvedType, ResolvedConstructor)>? list))
                {
                    list = [];
                    ctorNameToSources[ctor.Name] = list;
                }

                list.Add((kv.Key, type, ctor));
            }
        }

        // Also collect all type/interface names to detect ctor-vs-type collisions
        HashSet<string> emittedTypeNames = [];
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            switch (type.Category)
            {
                case TypeCategory.PrimitiveBytes:
                case TypeCategory.PrimitiveInteger:
                case TypeCategory.PrimitiveBool:
                case TypeCategory.PrimitiveData:
                case TypeCategory.Option:
                case TypeCategory.List:
                case TypeCategory.Ref:
                    break;
                case TypeCategory.SingleConstructor:
                case TypeCategory.Tuple:
                    _ = emittedTypeNames.Add(type.TypeName);
                    break;
                case TypeCategory.Union:
                    _ = emittedTypeNames.Add("I" + type.TypeName);
                    break;
                default:
                    break;
            }
        }

        foreach (KeyValuePair<string, List<(string DefKey, ResolvedType Parent, ResolvedConstructor Ctor)>> kv in ctorNameToSources)
        {
            bool hasCtorCollision = kv.Value.Count > 1;
            bool hasTypeCollision = emittedTypeNames.Contains(kv.Key);

            if (!hasCtorCollision && !hasTypeCollision)
            {
                continue;
            }

            // Prefix colliding constructors with their parent union's TypeName
            foreach ((string _, ResolvedType parent, ResolvedConstructor ctor) in kv.Value)
            {
                if (hasCtorCollision || hasTypeCollision)
                {
                    ctor.Name = parent.TypeName + ctor.Name;
                }
            }
        }
    }

    private static void FixFieldNameCollisions(Dictionary<string, ResolvedType> resolvedTypes)
    {
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;

            if (type.Constructors != null)
            {
                foreach (ResolvedConstructor ctor in type.Constructors)
                {
                    string recordName = type.Category == TypeCategory.Union ? ctor.Name : type.TypeName;
                    FixFieldNames(ctor.Fields, recordName);
                }
            }

            if (type.TupleFields != null)
            {
                FixFieldNames(type.TupleFields, type.TypeName);
            }
        }
    }

    private static void FixFieldNames(List<ResolvedField> fields, string enclosingName)
    {
        foreach (ResolvedField field in fields)
        {
            if (field.Name == enclosingName)
            {
                field.Name += "Value";
            }
        }
    }

    /// <summary>
    /// Converts a definition key like "Tuple$ByteArray_ByteArray" into a valid
    /// PascalCase C# identifier like "TupleByteArrayByteArray".
    /// </summary>
    private static string SanitizeDefinitionKey(string defKey)
    {
        // Replace all separators with spaces so ToPascalCase can capitalize each segment
        string normalized = defKey
            .Replace("/", " ")
            .Replace("$", " ")
            .Replace("<", " ")
            .Replace(">", "")
            .Replace(",", " ")
            .Replace("_", " ");

        return NamingConventions.SanitizeIdentifier(NamingConventions.ToPascalCase(normalized));
    }

    private static void GenerateSource(SourceProductionContext context, BlueprintFile blueprint)
    {
        string ns = NamingConventions.NamespaceFromTitle(blueprint.Preamble?.Title);
        string plutusVersion = blueprint.Preamble?.PlutusVersion ?? "v3";

        SchemaResolver resolver = new(blueprint.Definitions);
        Dictionary<string, ResolvedType> resolvedTypes = resolver.ResolveAll();

        // Resolve name collisions (e.g., two different Tuple definitions)
        ResolveNameCollisions(resolvedTypes);

        // Re-derive all field type strings now that names are final
        UpdateAllReferences(resolvedTypes, blueprint.Definitions);

        CSharpEmitter typeEmitter = new(ns);
        string typesSource = typeEmitter.EmitAllTypes(resolvedTypes);

        string safeTitle = NamingConventions.SanitizeIdentifier(
            NamingConventions.ToPascalCase(blueprint.Preamble?.Title ?? "Blueprint"));
        context.AddSource($"{safeTitle}.Types.g.cs", SourceText.From(typesSource, System.Text.Encoding.UTF8));

        // Emit CBOR serializers for the generated types
        try
        {
            MetadataConverter.EmitSerializers(context, ns, resolvedTypes);
        }
        catch (Exception serEx)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "BLUEPRINT002",
                    "Blueprint serializer generation failed",
                    "Failed to generate serializers: {0}",
                    "Chrysalis.Blueprint",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true),
                Location.None,
                serEx.Message));
        }

        if (blueprint.Validators.Count > 0)
        {
            ValidatorEmitter validatorEmitter = new(ns, plutusVersion);
            string validatorsSource = validatorEmitter.EmitAllValidators(blueprint.Validators);
            context.AddSource($"{safeTitle}.Validators.g.cs", SourceText.From(validatorsSource, System.Text.Encoding.UTF8));
        }
    }

    /// <summary>
    /// Re-derives all field type strings after collision resolution so references
    /// match the final type names (handles renamed tuples, list wrappers, etc.).
    /// </summary>
    private static void UpdateAllReferences(
        Dictionary<string, ResolvedType> resolvedTypes,
        Dictionary<string, SchemaNode> definitions)
    {
        // Phase 1: Fix OptionInnerType and ListElementType to point to actual resolved types
        // instead of stale copies from cycle detection
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            if (!definitions.TryGetValue(kv.Key, out SchemaNode? schema))
            {
                continue;
            }

            List<SchemaNode>? optionFields = schema.AnyOf != null && schema.AnyOf.Count >= 1
                ? schema.AnyOf[0].Fields : null;
            if (type.Category == TypeCategory.Option && type.OptionInnerType != null
                && optionFields != null && optionFields.Count == 1)
            {
                SchemaNode innerField = optionFields[0];
                if (innerField.Ref != null)
                {
                    string? innerDefKey = SchemaResolver.ResolveRef(innerField.Ref);
                    if (innerDefKey != null && resolvedTypes.TryGetValue(innerDefKey, out ResolvedType? innerResolved))
                    {
                        type.OptionInnerType = innerResolved;
                    }
                }
            }

            if (type.Category == TypeCategory.List && type.ListElementType != null
                && schema.ItemsSchema?.Ref != null)
            {
                string? elemDefKey = SchemaResolver.ResolveRef(schema.ItemsSchema.Ref);
                if (elemDefKey != null && resolvedTypes.TryGetValue(elemDefKey, out ResolvedType? elemResolved))
                {
                    type.ListElementType = elemResolved;
                }
            }
        }

        // Phase 2: Rebuild field type strings using final resolved type map
        Dictionary<string, string> defKeyToFinalType = [];
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            defKeyToFinalType[kv.Key] = SchemaResolver.GetCSharpTypeName(kv.Value);
        }

        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            if (!definitions.TryGetValue(kv.Key, out SchemaNode? schema))
            {
                continue;
            }

            if (type.Constructors != null && schema.AnyOf != null)
            {
                for (int ci = 0; ci < type.Constructors.Count && ci < schema.AnyOf.Count; ci++)
                {
                    UpdateConstructorFields(type.Constructors[ci], schema.AnyOf[ci], defKeyToFinalType);
                }
            }

            if (type.TupleFields != null && schema.Items != null)
            {
                for (int fi = 0; fi < type.TupleFields.Count && fi < schema.Items.Count; fi++)
                {
                    UpdateFieldFromSchema(type.TupleFields[fi], schema.Items[fi], defKeyToFinalType);
                }
            }
        }
    }

    private static void UpdateConstructorFields(
        ResolvedConstructor ctor, SchemaNode ctorSchema,
        Dictionary<string, string> defKeyToFinalType)
    {
        if (ctorSchema.Fields == null)
        {
            return;
        }

        for (int fi = 0; fi < ctor.Fields.Count && fi < ctorSchema.Fields.Count; fi++)
        {
            UpdateFieldFromSchema(ctor.Fields[fi], ctorSchema.Fields[fi], defKeyToFinalType);
        }
    }

    private static void UpdateFieldFromSchema(
        ResolvedField field, SchemaNode fieldSchema,
        Dictionary<string, string> defKeyToFinalType)
    {
        if (fieldSchema.Ref == null)
        {
            return;
        }

        string? defKey = SchemaResolver.ResolveRef(fieldSchema.Ref);
        if (defKey != null && defKeyToFinalType.TryGetValue(defKey, out string? finalType))
        {
            field.CSharpType = finalType;
            field.IsNullable = finalType.StartsWith("ICborOption<", StringComparison.Ordinal);
        }
    }
}
