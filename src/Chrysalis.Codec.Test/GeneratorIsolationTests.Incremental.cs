using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    private const string IncrementalImports = """
        global using System;
        using Chrysalis.Codec.Serialization;
        using Chrysalis.Codec.Serialization.Attributes;
        using Chrysalis.Codec.Types;

        """;

    [Fact]
    public void GlobalNamespaceModelCompiles()
    {
        CSharpCompilation input = CreateCompilation("GlobalModel", IncrementalImports + """
            [CborSerializable, CborList]
            public partial record Value([CborOrder(0)] int Item) : CborRecord;
            """);

        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public partial record Value", GeneratedText(driver, "Value"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedEditCachesEverySourceAndRefreshesDiagnosticLocation()
    {
        SyntaxTree original = ParseIncremental("Model.cs", """
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IEmpty : ICborType;
            [CborSerializable, CborList] public partial record Value([CborOrder(0)] int Item) : CborRecord;
            """);
        CSharpCompilation input = IncrementalCompilation("CacheProbe", original);
        GeneratorDriver before = TrackingDriver().RunGenerators(input);
        SyntaxTree edited = original.WithChangedText(original.GetText().Replace(0, 0, "// unrelated\n"));
        GeneratorDriver after = before.RunGenerators(input.ReplaceSyntaxTree(original, edited));

        Assert.Equal(GeneratedText(before, "Snapshot.Value"), GeneratedText(after, "Snapshot.Value"));
        Assert.Equal(GeneratedText(before, "Snapshot.IEmpty"), GeneratedText(after, "Snapshot.IEmpty"));
        Assert.Equal([IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached], SourceReasons(after));
        Diagnostic warning = Assert.Single(after.GetRunResult().Diagnostics, diagnostic => diagnostic.Id == "CHCB001");
        Assert.Same(edited, warning.Location.SourceTree);
        Assert.Equal("IEmpty", edited.GetText().ToString(warning.Location.SourceSpan));
    }

    [Fact]
    public void EditingCasesUpdatesTheirUnionWithoutRegeneratingAnIndependentModel()
    {
        SyntaxTree root = ParseIncremental("Root.cs", """
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IChoice<T> : ICborType;
            [CborSerializable, CborList] public partial record Standalone([CborOrder(0)] int Item) : CborRecord;
            """);
        SyntaxTree a = ParseIncremental("A.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record A([CborOrder(0)] int Item) : CborRecord, IChoice<int>;
            """, includeGlobals: false);
        SyntaxTree b = ParseIncremental("B.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record B([CborOrder(0)] string Item) : CborRecord, IChoice<int>;
            """, includeGlobals: false);
        CSharpCompilation input = IncrementalCompilation("JoinProbe", root, a);
        GeneratorDriver first = TrackingDriver().RunGenerators(input);
        GeneratorDriver added = first.RunGenerators(input.AddSyntaxTrees(b));
        GeneratorDriver removed = added.RunGenerators(input);

        Assert.Contains("Snapshot.A.Read", GeneratedText(first, "Snapshot.IChoice`T`"), StringComparison.Ordinal);
        Assert.Contains("Snapshot.B.Read", GeneratedText(added, "Snapshot.IChoice`T`"), StringComparison.Ordinal);
        Assert.Equal(GeneratedText(first, "Snapshot.IChoice`T`"), GeneratedText(removed, "Snapshot.IChoice`T`"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(added, "Snapshot.Standalone"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(removed, "Snapshot.Standalone"));
    }

    [Fact]
    public void SemanticMarkersFindAliasesAndPlutusButIgnoreLookalikes()
    {
        CSharpCompilation input = CreateCompilation("MarkerProbe", IncrementalImports + """
            using Marker = Chrysalis.Codec.Serialization.Attributes.CborSerializableAttribute;
            using Datum = Chrysalis.Codec.Serialization.Attributes.PlutusDataAttribute;
            namespace Snapshot
            {
                [Marker, CborList] public partial record Alias([CborOrder(0)] int Item) : CborRecord;
                [Datum(0)] public partial record Constr([CborOrder(0)] int Item) : CborRecord;
                [Marker, Datum(1)] public partial record Dual([CborOrder(0)] int Item) : CborRecord;
            }
            namespace Lookalike
            {
                public sealed class CborSerializableAttribute : Attribute;
                [CborSerializable] public partial record Unrelated;
            }
            """);
        GeneratorDriver driver = CreateDriver().RunGenerators(input);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.Contains("public partial record Alias", GeneratedText(driver, "Snapshot.Alias"), StringComparison.Ordinal);
        Assert.Contains("WriteSemanticTag((ulong)121)", GeneratedText(driver, "Snapshot.Constr"), StringComparison.Ordinal);
        Assert.Contains("WriteSemanticTag((ulong)122)", GeneratedText(driver, "Snapshot.Dual"), StringComparison.Ordinal);
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,
            source => source.HintName.StartsWith("Lookalike.", StringComparison.Ordinal));
    }

    [Fact]
    public void EditingUnrelatedModelCachesUnionWithTupleDependencies()
    {
        SyntaxTree root = ParseIncremental("Root.cs", """
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IChoice : ICborType;
            [CborSerializable, CborList] public partial record A([CborOrder(0)] Outer Item) : CborRecord, IChoice;
            [CborSerializable, CborList] public partial record B([CborOrder(0)] string Item) : CborRecord, IChoice;
            [CborSerializable, CborList] public partial record Outer([CborOrder(0)] Inner Item) : CborRecord;
            """);
        SyntaxTree inner = ParseIncremental("Inner.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record Inner([CborOrder(0)] int Item) : CborRecord;
            """, includeGlobals: false);
        SyntaxTree standalone = ParseIncremental("Standalone.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record Standalone([CborOrder(0)] int Item) : CborRecord;
            """, includeGlobals: false);
        CSharpCompilation input = IncrementalCompilation("DependencyProbe", root, inner, standalone);
        GeneratorDriver first = TrackingDriver().RunGenerators(input);
        CSharpCompilation unrelatedEdit = ChangePropertyType(input, standalone);
        GeneratorDriver unrelated = first.RunGenerators(unrelatedEdit);
        GeneratorDriver nestedEdit = unrelated.RunGenerators(ChangePropertyType(unrelatedEdit, inner));

        Assert.Equal(IncrementalStepRunReason.Modified, SourceReason(unrelated, "Snapshot.Standalone"));
        Assert.Equal(GeneratedText(first, "Snapshot.IChoice"), GeneratedText(unrelated, "Snapshot.IChoice"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(unrelated, "Snapshot.IChoice"));
        Assert.Contains("Snapshot.Inner.Item\", \"integer\"", GeneratedText(unrelated, "Snapshot.IChoice"), StringComparison.Ordinal);
        Assert.Contains("Snapshot.Inner.Item\", \"boolean\"", GeneratedText(nestedEdit, "Snapshot.IChoice"), StringComparison.Ordinal);
        Assert.Equal(IncrementalStepRunReason.Modified, SourceReason(nestedEdit, "Snapshot.IChoice"));

        static CSharpCompilation ChangePropertyType(CSharpCompilation input, SyntaxTree tree) => input.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(tree.ToString().Replace("int Item", "bool Item", StringComparison.Ordinal), ParseOptions, tree.FilePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PartialCaseJoinsUnionInBothFileOrders(bool markedFirst)
    {
        SyntaxTree root = ParseIncremental("Root.cs", """
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IChoice : ICborType;
            """);
        SyntaxTree marked = ParseIncremental("Case.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record Case([CborOrder(0)] int Item) : CborRecord;
            """, includeGlobals: false);
        SyntaxTree helper = ParseIncremental("Case.Helpers.cs", """
            namespace Snapshot;
            public partial record Case : IChoice;
            """, includeGlobals: false);
        CSharpCompilation input = IncrementalCompilation("PartialCaseProbe", root, markedFirst ? marked : helper, markedFirst ? helper : marked);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);

        Assert.Contains("Snapshot.Case.Read", GeneratedText(driver, "Snapshot.IChoice"), StringComparison.Ordinal);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SplitMarkersKeepThePositionalPayloadInBothFileOrders(bool payloadFirst)
    {
        SyntaxTree payload = ParseIncremental("Z.Payload.cs", """
            namespace Snapshot;
            [CborSerializable] public partial record Split([CborOrder(0)] int Item) : CborRecord;
            [CborSerializable, CborList] public partial record Independent([CborOrder(0)] int Item) : CborRecord;
            """);
        SyntaxTree marker = ParseIncremental("A.Marker.cs", """
            namespace Snapshot;
            [PlutusDataAttribute(1)] public partial record Split;
            """, includeGlobals: false);
        CSharpCompilation input = IncrementalCompilation("SplitMarkersProbe", payloadFirst ? payload : marker, payloadFirst ? marker : payload);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);

        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.Equal(5, driver.GetRunResult().Results.Single().GeneratedSources.Length);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string split = GeneratedText(driver, "Snapshot.Split");
        Assert.Contains("WriteSemanticTag((ulong)122)", split, StringComparison.Ordinal);
        Assert.Contains("writer.WriteInt32(item);", split, StringComparison.Ordinal);
        Assert.Contains("public partial record Independent", GeneratedText(driver, "Snapshot.Independent"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void PartialRecordsCollectMembersAcrossParts(bool isStruct, bool positional, bool markerFirst)
    {
        string kind = isStruct ? "readonly partial record struct" : "partial record class";
        string baseType = isStruct ? "ICborType" : "CborRecord";
        string storage = isStruct ? "partial " : "";
        string parameters = positional ? "([CborOrder(0)] int Item)" : "";
        string item = !positional ? $"[CborOrder(0)] public {storage}int Item {{ get; }}"
            : isStruct ? "public partial int Item { get; } public int Initial { get; } = Item;" : "";
        string ctor = isStruct ? "" : positional ? "public Split(int item, int next) : this(item) => Next = next;"
            : "public Split(int item, int next) => (Item, Next) = (item, next);";
        SyntaxTree payload = ParseIncremental(markerFirst ? "Z.Payload.cs" : "A.Payload.cs", $$"""
            namespace Snapshot;
            [CborSerializable, CborConstr(1)] public {{kind}} Split{{parameters}} : {{baseType}} { {{item}} }
            """);
        // The marker-only part need not repeat the readonly modifier from the payload part.
        string markerKind = isStruct ? "partial record struct" : kind;
        SyntaxTree marker = ParseIncremental(markerFirst ? "A.Marker.cs" : "Z.Marker.cs", $$"""
            namespace Snapshot;
            [PlutusDataAttribute(1)] public {{markerKind}} Split;
            """, includeGlobals: false);
        SyntaxTree tail = ParseIncremental("M.Tail.cs", $$"""
            using TailOrder = Snapshot.FieldOrder;
            namespace Snapshot;
            public static class FieldOrder { public const int Next = 1; }
            public {{kind}} Split { [CborOrder(TailOrder.Next)] public {{storage}}int Next { get; } {{ctor}} }
            """, includeGlobals: false);
        SyntaxTree[] parts = markerFirst ? [marker, tail, payload] : [payload, tail, marker];
        CSharpCompilation input = IncrementalCompilation("PartialMembersProbe", parts);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);

        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string split = GeneratedText(driver, "Snapshot.Split");
        Assert.Contains("Create(int item, int next)", split, StringComparison.Ordinal);
        Assert.Contains("WriteSemanticTag((ulong)122)", split, StringComparison.Ordinal);
        Assert.Contains("writer.WriteInt32(item);", split, StringComparison.Ordinal);
        Assert.Contains("writer.WriteInt32(next);", split, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPartialPartPreservesCreateSignatureAndBytes()
    {
        SyntaxTree model = ParseIncremental("Z.Model.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public readonly partial record struct Split : ICborType
            {
                [CborOrder(1)] public partial int Second { get; }
                [CborOrder(0)] public partial string First { get; }
            }
            public static class Consumer { public static Split Make() => Split.Create(7, "seven"); }
            """);
        SyntaxTree empty = ParseIncremental("A.Empty.cs", """
            namespace Snapshot;
            public partial record struct Split;
            """, includeGlobals: false);
        CSharpCompilation input = IncrementalCompilation("EmptyPartProbe", model);
        GeneratorDriver baseline = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation baselineOutput, out _);
        Assert.DoesNotContain(baselineOutput.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string original = GeneratedText(baseline, "Snapshot.Split");
        Assert.Contains("Create(int second, string first)", original, StringComparison.Ordinal);

        SyntaxTree[][] orders = [[empty, model], [model, empty]];
        foreach (SyntaxTree[] parts in orders)
        {
            GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input.RemoveAllSyntaxTrees().AddSyntaxTrees(parts), out Compilation output, out _);
            Assert.Empty(driver.GetRunResult().Diagnostics);
            Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Equal(original, GeneratedText(driver, "Snapshot.Split"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialClassKeepsDirectPropertiesButNotNestedHelperProperties(bool splitField)
    {
        string ctor = splitField ? "Split(Detail item, Other tail) => (Item, Tail) = (item, tail);" : "Split(Detail item) => Item = item;";
        string make = splitField ? "Make(Detail item, Other tail) => Split.Create(item, tail);" : "Make(Detail item) => Split.Create(item);";
        SyntaxTree model = ParseIncremental("A.Model.cs", $$"""
            namespace Snapshot;
            [CborSerializable, CborList] public partial record Detail([CborOrder(0)] int Value) : CborRecord;
            [CborSerializable, CborList] public partial record Other([CborOrder(0)] int Value) : CborRecord;
            public class Storage
            {
                public ReadOnlyMemory<byte> Raw { get; set; }
                public bool IsIndefinite { get; set; }
                public int ConstrIndex { get; set; }
            }
            [CborSerializable, CborList] public partial class Split : Storage
            {
                public Detail Item{ get; }
                public {{ctor}}
            }
            public static class Consumer { public static Split {{make}} }
            """);
        string tail = splitField ? "public Other Tail{ get; }" : "";
        SyntaxTree helper = ParseIncremental("Z.Helper.cs", $$"""
            namespace Snapshot;
            public partial class Split
            {
                private sealed class CacheEntry { public int Expires{ get; } }
                {{tail}}
            }
            """, includeGlobals: false);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(IncrementalCompilation("NestedHelperProbe", helper, model),
            out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string serializer = GeneratedText(driver, "Snapshot.Split");
        Assert.DoesNotContain("Expires", serializer, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateMarkersAndNamesDoNotStopOtherModels()
    {
        SyntaxTree first = ParseIncremental("A.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record Duplicate([CborOrder(0)] int Item) : CborRecord;
            [CborSerializable, CborList] file partial record SameName([CborOrder(0)] int Item) : CborRecord;
            [CborSerializable, CborList] public partial record Independent([CborOrder(0)] int Item) : CborRecord;
            """);
        SyntaxTree second = ParseIncremental("B.cs", """
            namespace Snapshot;
            [CborSerializable] public partial record Duplicate;
            [CborSerializable, CborList] file partial record SameName([CborOrder(0)] bool Item) : CborRecord;
            """, includeGlobals: false);
        GeneratorDriver driver = CreateDriver().RunGenerators(IncrementalCompilation("DuplicateProbe", first, second));
        GeneratorDriver reversed = CreateDriver().RunGenerators(IncrementalCompilation("DuplicateProbe", second, first));

        // The duplicate marker is a compiler error, but must not stop the generator for the whole project.
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.Empty(reversed.GetRunResult().Diagnostics);
        Assert.Equal(7, driver.GetRunResult().Results.Single().GeneratedSources.Length);
        Assert.Equal(7, reversed.GetRunResult().Results.Single().GeneratedSources.Length);
        Assert.Equal(GeneratedText(driver, "Snapshot.SameName"), GeneratedText(reversed, "Snapshot.SameName"));
        Assert.Contains("public partial record Independent", GeneratedText(driver, "Snapshot.Independent"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AliasProbe", "Snapshot", "IChoice")]
    [InlineData("Chrysalis.Network", "Chrysalis.Network.Cbor.Handshake", "HandshakeMessage")]
    public void ExternAliasedInterfaceIsNotACaseOfLocalUnion(string assemblyName, string ns, string union)
    {
        CSharpCompilation foreign = CreateCompilation("OldContract", $"namespace {ns}; public interface {union};");
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = foreign.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        MetadataReference reference = MetadataReference.CreateFromImage(image.ToArray(),
            MetadataReferenceProperties.Assembly.WithAliases(["old"]));
        CSharpCompilation input = CreateCompilation(assemblyName, "extern alias old;\n" + IncrementalImports + $$"""
            namespace {{ns}};
            [CborSerializable, CborUnion] public partial interface {{union}} : ICborType;
            [CborSerializable, CborList] public partial record Local([CborOrder(0)] int Item) : CborRecord, {{union}};
            [CborSerializable, CborList] public partial record Foreign([CborOrder(0)] int Item) : CborRecord, old::{{ns}}.{{union}};
            """).AddReferences(reference);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);

        string serializer = GeneratedText(driver, ns + "." + union);
        Assert.Contains(ns + ".Local.Read", serializer, StringComparison.Ordinal);
        Assert.DoesNotContain(ns + ".Foreign.Read", serializer, StringComparison.Ordinal);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AddingAndRemovingDeclaredReaderUpdatesOnlyTheUnion()
    {
        SyntaxTree root = ParseIncremental("Root.cs", """
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IChoice : ICborType;
            [CborSerializable, CborList] public partial record Case([CborOrder(0)] int Item) : CborRecord, IChoice;
            """);
        SyntaxTree reader = ParseIncremental("Reader.cs", """
            namespace Snapshot;
            public partial interface IChoice
            {
                static IChoice Read(ReadOnlyMemory<byte> data, out int count) => Case.Read(data, out count);
            }
            """, includeGlobals: false);
        CSharpCompilation input = IncrementalCompilation("DeclaredReaderProbe", root);
        GeneratorDriver first = TrackingDriver().RunGenerators(input);
        GeneratorDriver added = first.RunGenerators(input.AddSyntaxTrees(reader));
        GeneratorDriver removed = added.RunGenerators(input);

        string before = GeneratedText(first, "Snapshot.IChoice");
        string declared = GeneratedText(added, "Snapshot.IChoice");
        Assert.Contains("out int bytesConsumed", before, StringComparison.Ordinal);
        Assert.DoesNotContain("out int bytesConsumed", declared, StringComparison.Ordinal);
        Assert.Contains("return Read(data, out _);", declared, StringComparison.Ordinal);
        Assert.Equal(before, GeneratedText(removed, "Snapshot.IChoice"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(added, "Snapshot.Case"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(removed, "Snapshot.Case"));
    }

    [Fact]
    public void UnrelatedEditCachesCborRegistrationStep()
    {
        SyntaxTree original = ParseIncremental("Model.cs", """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record Value([CborOrder(0)] int Item) : CborRecord;
            """);
        CSharpCompilation input = IncrementalCompilation("RegistrationCacheProbe", original);
        GeneratorDriver before = TrackingDriver().RunGenerators(input);
        SyntaxTree edited = original.WithChangedText(original.GetText().Replace(0, 0, "// unrelated\n"));
        GeneratorDriver after = before.RunGenerators(input.ReplaceSyntaxTree(original, edited));

        Assert.True(after.GetRunResult().Results.Single().TrackedSteps.TryGetValue("CborRegistration", out ImmutableArray<IncrementalGeneratorRunStep> steps));
        Assert.Equal(IncrementalStepRunReason.Cached, Assert.Single(steps.SelectMany(static s => s.Outputs)).Reason);
        Assert.Equal(RegistrationText(before), RegistrationText(after));
    }

    [Fact]
    public void AutoRegistrationBoundariesHandleExclusionsConstraintsAndShadowing()
    {
        CSharpCompilation foreign = CreateCompilation("ForeignGenericLib", IncrementalImports + """
            namespace Snapshot;
            [CborSerializable, CborList] public partial record ForeignBox<T>([CborOrder(0)] T Value) : CborRecord;
            """);
        using MemoryStream foreignPe = new();
        Microsoft.CodeAnalysis.Emit.EmitResult foreignEmit = foreign.Emit(foreignPe);
        Assert.True(foreignEmit.Success, string.Join(Environment.NewLine, foreignEmit.Diagnostics));
        MetadataReference aliasedRef = MetadataReference.CreateFromImage(foreignPe.ToArray(),
            MetadataReferenceProperties.Assembly.WithAliases(["old"]));

        const string source = """
            #nullable disable
            extern alias old;
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;

            namespace Snapshot
            {
                [Obsolete("old")] [CborSerializable, CborList] public partial record OldModel([CborOrder(0)] int Item) : CborRecord;
                [CborSerializable, CborUnion] public abstract partial record AbstractUnion<T> : CborRecord;
                [CborSerializable, CborList] public partial record AbstractCase<T>([CborOrder(0)] T Value) : AbstractUnion<T>;

                public static class Consumers
                {
                    private sealed class PrivateArg;

                    [Obsolete("test")]
                    public static void Touch(
                        Some<byte[]> a,
                        CborList<byte[]> b,
                        CborMap<int?, int> c,
                        CborList<OldModel> d,
                        AbstractUnion<int> e,
                        old::Snapshot.ForeignBox<int> f)
                    {
                        _ = typeof(CborList<PrivateArg>);
                    }
                }
            }

            namespace Codec
            {
                [CborSerializable, CborList] public partial record Shadowed([CborOrder(0)] int Item) : CborRecord;
            }
            """;

        CSharpCompilation compilation = CreateCompilation("BoundaryProbe", source).AddReferences(aliasedRef);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        string reg = RegistrationText(driver);

        Assert.DoesNotContain("PrivateArg", reg, StringComparison.Ordinal);
        Assert.DoesNotContain("ForeignBox", reg, StringComparison.Ordinal);
        Assert.DoesNotContain("OldModel", reg, StringComparison.Ordinal);
        Assert.DoesNotContain("CborMap<int?", reg, StringComparison.Ordinal);
        Assert.Contains("RegisterStruct<global::Chrysalis.Codec.Types.Some<byte[]>>", reg, StringComparison.Ordinal);
        Assert.Contains("RegisterStruct<global::Chrysalis.Codec.Types.CborList<byte[]>>", reg, StringComparison.Ordinal);
        Assert.Contains("Register<global::Snapshot.AbstractUnion<int>>", reg, StringComparison.Ordinal);
        Assert.Contains("Register<global::Codec.Shadowed>", reg, StringComparison.Ordinal);
        Assert.DoesNotContain("ICborCodecRegistrar", GeneratedText(driver, "Snapshot.AbstractUnion`T`"), StringComparison.Ordinal);
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);

        const string inaccessibleSource = """
            namespace Snapshot;
            [CborSerializable, CborList] file partial record FileOnly([CborOrder(0)] int Item) : CborRecord;
            public partial class Outer
            {
                [CborSerializable, CborList] private partial record PrivateNested([CborOrder(0)] int Item) : CborRecord;
            }
            [CborSerializable, CborList] public partial record PublicSibling([CborOrder(0)] int Item) : CborRecord;
            """;
        GeneratorDriver inaccessibleDriver = CreateDriver().RunGenerators(CreateCompilation("InaccessibleProbe", IncrementalImports + inaccessibleSource));
        string inaccessibleReg = RegistrationText(inaccessibleDriver);
        Assert.DoesNotContain("FileOnly", inaccessibleReg, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateNested", inaccessibleReg, StringComparison.Ordinal);
        Assert.Contains("Register<global::Snapshot.PublicSibling>", inaccessibleReg, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUnionsAreExcludedFromAutoRegistrationWithoutCascadingErrors()
    {
        const string badOfficialSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace Snapshot;
            [CborSerializable] public readonly partial record struct BadContainer : ICborType;
            [CborSerializable, CborUnion(typeof(BadContainer)), Union] public readonly partial struct BadUnion : ICborType;
            [CborSerializable, CborList] public partial record GoodModel([CborOrder(0)] int Item) : CborRecord;
            """;
        GeneratorDriver badOfficialDriver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation("InvalidOfficialProbe", badOfficialSource), out Compilation badOfficialOutput, out _);
        _ = Assert.Single(badOfficialDriver.GetRunResult().Diagnostics, static d => d.Id == "CHCB007");
        string officialReg = RegistrationText(badOfficialDriver);
        Assert.DoesNotContain("BadUnion", officialReg, StringComparison.Ordinal);
        Assert.Contains("Register<global::Snapshot.GoodModel>", officialReg, StringComparison.Ordinal);
        Assert.DoesNotContain(badOfficialOutput.GetDiagnostics(), static d => d.Id == "CS0117");

        const string badOpenGenericSource = """
            #nullable enable
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IBadGeneric<T> : ICborType
            {
                public static IBadGeneric<T> Read(ReadOnlyMemory<byte> data) => throw new NotImplementedException();
            }
            public static class Caller { public static Type Ref() => typeof(IBadGeneric<int>); }
            """;
        GeneratorDriver badGenericDriver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation("InvalidGenericProbe", badOpenGenericSource), out Compilation badGenericOutput, out _);
        _ = Assert.Single(badGenericDriver.GetRunResult().Diagnostics, static d => d.Id == "CHCB003");
        Assert.DoesNotContain(badGenericDriver.GetRunResult().Results.Single().GeneratedSources,
            static s => s.HintName == "ChrysalisCborTypeRegistration.g.cs");
        Assert.DoesNotContain(badGenericOutput.GetDiagnostics(), static d => d.Id is "CS0123" or "CS0117");
    }
    private static CSharpGeneratorDriver TrackingDriver() => CSharpGeneratorDriver.Create(
        generators: [new CodeGen.CborSerializerCodeGen().AsSourceGenerator()],
        parseOptions: ParseOptions, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static IncrementalStepRunReason[] SourceReasons(GeneratorDriver driver) =>
        [.. SourceOutputs(driver).Select(output => output.Reason)];

    private static IncrementalStepRunReason SourceReason(GeneratorDriver driver, string type) => Assert.Single(SourceOutputs(driver),
        output => (output.Value.GetType().GetProperty("TypeName")?.GetValue(output.Value) as string) == type).Reason;

    private static IEnumerable<(object Value, IncrementalStepRunReason Reason)> SourceOutputs(GeneratorDriver driver)
    {
        Assert.True(driver.GetRunResult().Results.Single().TrackedSteps.TryGetValue("CborSources", out ImmutableArray<IncrementalGeneratorRunStep> steps),
            "The generator must expose the per-type source step.");
        return steps.SelectMany(step => step.Outputs);
    }

    private static string GeneratedText(GeneratorDriver driver, string type) => driver.GetRunResult().Results.Single()
        .GeneratedSources.Single(source => source.HintName == type + ".Serializer.g.cs").SourceText.ToString();
    private static string RegistrationText(GeneratorDriver driver) => driver.GetRunResult().Results.Single()
        .GeneratedSources.Single(source => source.HintName == "ChrysalisCborTypeRegistration.g.cs").SourceText.ToString();


    private static SyntaxTree ParseIncremental(string path, string source, bool includeGlobals = true) =>
        CSharpSyntaxTree.ParseText((includeGlobals ? IncrementalImports : IncrementalImports[(IncrementalImports.IndexOf('\n', StringComparison.Ordinal) + 1)..]) + source, ParseOptions, path);

    private static CSharpCompilation IncrementalCompilation(string name, params SyntaxTree[] trees) =>
        CSharpCompilation.Create(name, trees, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
