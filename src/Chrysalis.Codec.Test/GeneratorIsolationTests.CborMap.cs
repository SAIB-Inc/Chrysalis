using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    [Fact]
    public void SyntheticCborMapNullableGenericNestedListValueUnionAndPrimitiveCoexist()
    {
        Assembly assembly = CompileCollectionProbe("CborMapProbe", """
            [CborSerializable, CborMap]
            public readonly partial record struct NullableMapBox : ICborType
            {
                [CborProperty(0)] public partial CborMap<int, string?>? Optional { get; }
            }

            [CborSerializable, CborMap]
            public readonly partial record struct GenericMapBox<TKey, TValue> : ICborType where TKey : notnull
            {
                [CborProperty(0)] public partial CborMap<TKey, TValue> Items { get; }
            }

            [CborSerializable]
            public readonly partial record struct NestedMapBox : ICborType
            {
                public partial CborMap<int, CborMap<int, ulong>> Value { get; }
            }

            [CborSerializable, CborUnion]
            public partial interface IMapOrListUnion : ICborType;

            [CborSerializable]
            public readonly partial record struct MapOfListCase : IMapOrListUnion
            {
                public partial CborMap<int, CborList<int>> Value { get; }
            }

            [CborSerializable]
            public readonly partial record struct ListCase : IMapOrListUnion
            {
                public partial CborList<int> Value { get; }
            }

            [CborSerializable]
            public readonly partial record struct PrimitiveMapHolder : ICborType
            {
                public partial CborMap Value { get; }
            }
            """);

        // (a) Nullable CborMap<int, string?>? (no CS8639; absent, null, and non-null round-trips)
        Type nullableBox = assembly.GetType("CborMapProbe.NullableMapBox", throwOnError: true)!;
        PropertyInfo optProp = nullableBox.GetProperty("Optional")!;
        Assert.Null(optProp.GetValue(ReadBox(nullableBox, Convert.FromHexString("A0")).Model));
        Assert.Null(optProp.GetValue(ReadBox(nullableBox, Convert.FromHexString("A100F6")).Model));
        object nonNull = ReadBox(nullableBox, Convert.FromHexString("A100A101626869")).Model;
        CborMap<int, string?> optMap = Assert.IsType<CborMap<int, string?>>(optProp.GetValue(nonNull));
        Assert.Equal("hi", optMap[1]);

        // (b) Open generic GenericMapBox<int, ulong>
        Type genericBox = assembly.GetType("CborMapProbe.GenericMapBox`2", throwOnError: true)!
            .MakeGenericType(typeof(int), typeof(ulong));
        object genModel = ReadBox(genericBox, Convert.FromHexString("A100A10207")).Model;
        CborMap<int, ulong> genItems = Assert.IsType<CborMap<int, ulong>>(genericBox.GetProperty("Items")!.GetValue(genModel));
        Assert.Equal(7UL, genItems[2]);

        // (c) Nested CborMap<int, CborMap<int, ulong>>
        Type nestedBox = assembly.GetType("CborMapProbe.NestedMapBox", throwOnError: true)!;
        object nestedModel = ReadBox(nestedBox, Convert.FromHexString("A101A10209")).Model;
        CborMap<int, CborMap<int, ulong>> nestedMap =
            Assert.IsType<CborMap<int, CborMap<int, ulong>>>(nestedBox.GetProperty("Value")!.GetValue(nestedModel));
        Assert.Equal(9UL, nestedMap[1][2]);

        // (d, f) CborMap<int, CborList<int>> container & union dispatch on major type 5 (kills control-m5 & control-m6!)
        Type unionType = assembly.GetType("CborMapProbe.IMapOrListUnion", throwOnError: true)!;
        Type mapCase = assembly.GetType("CborMapProbe.MapOfListCase", throwOnError: true)!;
        Type listCase = assembly.GetType("CborMapProbe.ListCase", throwOnError: true)!;
        object resolvedMap = ReadBox(unionType, Convert.FromHexString("A101820304")).Model;
        Assert.Equal(mapCase, resolvedMap.GetType());
        CborMap<int, CborList<int>> mapOfList = Assert.IsType<CborMap<int, CborList<int>>>(mapCase.GetProperty("Value")!.GetValue(resolvedMap));
        Assert.Equal([3, 4], [.. mapOfList[1]]);
        // Direct container rejects array as "map" (kills control-m6!)
        CborDecodeException wrongMajor = Assert.Throws<CborDecodeException>(() =>
        {
            try { _ = ReadBox(mapCase, Convert.FromHexString("820304")); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
        });
        Assert.Equal("map", wrongMajor.Expected);
        object resolvedList = ReadBox(unionType, Convert.FromHexString("820304")).Model;
        Assert.Equal(listCase, resolvedList.GetType());

        // (g) Coexistence with non-generic CborMap : ICborPrimitive
        Type primHolder = assembly.GetType("CborMapProbe.PrimitiveMapHolder", throwOnError: true)!;
        Assert.Equal(typeof(CborMap), primHolder.GetProperty("Value")!.PropertyType);
    }

    [Theory]
    [InlineData("PlutusData(0)", "byte[]")]
    [InlineData("PlutusData(0)", "ReadOnlyMemory<byte>")]
    [InlineData("CborSerializable, CborConstr(0)", "byte[]")]
    [InlineData("CborSerializable, CborConstr(0)", "ReadOnlyMemory<byte>")]
    public void PlutusCborMapWithBytesEmitsChcb004(string encoding, string keyType)
    {
        string source = $$"""
            using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace CborMapChcb004Probe;
            [{{encoding}}]
            public readonly partial record struct BadPlutusMap : ICborType
            {
                [CborOrder(0)] public partial CborMap<{{keyType}}, int> Items { get; }
            }
            """;
        CSharpCompilation compilation = CreateCompilation("CborMapChcb004Probe", source);
        GeneratorDriverRunResult result = CreateDriver().RunGenerators(compilation).GetRunResult();
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "CHCB004");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    private static byte[] SerializeICbor(Type type, object model) =>
        (byte[])typeof(CborSerializer).GetMethod("Serialize")!.MakeGenericMethod(type).Invoke(null, [model])!;

    [Fact]
    public void OuterNullabilityPreservesInnerNullableCollectionTypeArguments()
    {
        Assembly assembly = CompileCollectionProbe("OuterNullabilityProbe", """
            [CborSerializable]
            public readonly partial record struct BoxListMap : ICborType
            {
                public partial List<CborMap<int, int?>> Value { get; }
            }

            [CborSerializable]
            public readonly partial record struct BoxDictMap : ICborType
            {
                public partial Dictionary<int, CborMap<int, int?>> Value { get; }
            }
            """);

        Type boxListType = assembly.GetType("OuterNullabilityProbe.BoxListMap", throwOnError: true)!;
        byte[] listWire = Convert.FromHexString("81A10105");
        (object listModel, int listConsumed) = ReadBox(boxListType, listWire);
        Assert.Equal(listWire.Length, listConsumed);
        List<CborMap<int, int?>> decodedList =
            Assert.IsType<List<CborMap<int, int?>>>(boxListType.GetProperty("Value")!.GetValue(listModel));
        Assert.Equal(5, Assert.Single(decodedList)[1]);
        object createdList = boxListType.GetMethod("Create")!.Invoke(null,
            [new List<CborMap<int, int?>> { CborMap<int, int?>.Create([new(1, 5)]) }])!;
        Assert.Equal(listWire, SerializeICbor(boxListType, createdList));

        Type boxDictType = assembly.GetType("OuterNullabilityProbe.BoxDictMap", throwOnError: true)!;
        byte[] dictWire = Convert.FromHexString("A102A10105");
        (object dictModel, int dictConsumed) = ReadBox(boxDictType, dictWire);
        Assert.Equal(dictWire.Length, dictConsumed);
        Dictionary<int, CborMap<int, int?>> decodedDict =
            Assert.IsType<Dictionary<int, CborMap<int, int?>>>(boxDictType.GetProperty("Value")!.GetValue(dictModel));
        Assert.Equal(5, decodedDict[2][1]);
        object createdDict = boxDictType.GetMethod("Create")!.Invoke(null,
            [new Dictionary<int, CborMap<int, int?>> { [2] = CborMap<int, int?>.Create([new(1, 5)]) }])!;
        Assert.Equal(dictWire, SerializeICbor(boxDictType, createdDict));
    }

    [Fact]
    public void ExternalCardanoCoreDictionaryUsesPublicCborMapKeyComparer()
    {
        const string ns = "Chrysalis.Codec.Types.Cardano.Core.ExtensionModels";
        const string decl = """
            [CborSerializable]
            public readonly partial record struct ExtLedgerDictBox : ICborType
            {
                public partial Dictionary<uint, uint> Value { get; }
            }
            """;
        Assembly assembly = CompileCollectionProbe(ns, decl);

        string source = $$"""
            #nullable enable
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace {{ns}};
            {{decl}}
            """;
        GeneratorDriverRunResult genResult = CreateDriver().RunGenerators(CreateCompilation(ns, source)).GetRunResult();
        string generated = genResult.Results.Single().GeneratedSources
            .Single(s => s.HintName == $"{ns}.ExtLedgerDictBox.Serializer.g.cs")
            .SourceText.ToString();
        Assert.Contains("global::Chrysalis.Codec.Types.CborMap<uint, uint>.KeyComparer", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("LedgerKeyComparers", generated, StringComparison.Ordinal);

        Type extType = assembly.GetType($"{ns}.ExtLedgerDictBox", throwOnError: true)!;
        byte[] wire = Convert.FromHexString("A10102");
        (object model, int consumed) = ReadBox(extType, wire);
        Assert.Equal(wire.Length, consumed);
        Dictionary<uint, uint> dict = Assert.IsType<Dictionary<uint, uint>>(extType.GetProperty("Value")!.GetValue(model));
        Assert.Equal(2u, dict[1u]);
        object created = extType.GetMethod("Create")!.Invoke(null, [new Dictionary<uint, uint> { [1u] = 2u }])!;
        Assert.Equal(wire, SerializeICbor(extType, created));
    }
}
