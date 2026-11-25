using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    [Theory]
    [InlineData("LazyList", "_size", "_list", null)]
    [InlineData("LazyMap", "_size", "_map", null)]
    [InlineData("EagerList", "EagerListValueArraySize", "EagerListValueTempList", "if (!EagerListValueIsIndefinite)")]
    [InlineData("EagerMap", "EagerMapValueMapSize", "EagerMapValueTempMap", "if (EagerMapValueMapIsIndefinite)")]
    [InlineData("ClassMap", "mapSize", "resultMap", null)]
    public void CollectionCapacityIsClampedAfterReadingAndCheckingTheHeader(
        string model, string size, string target, string? shapeCheck)
    {
        const string source = """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace CapacityProbe;
            [CborSerializable] public readonly partial record struct LazyList : ICborType
            {
                public partial List<int> Value { get; }
            }
            [CborSerializable] public readonly partial record struct LazyMap : ICborType
            {
                public partial Dictionary<int, bool> Value { get; }
            }
            [CborSerializable, CborList]
            public partial record EagerList([CborOrder(0), CborIndefinite] List<int> Value) : CborRecord;
            [CborSerializable, CborList]
            public partial record EagerMap([CborOrder(0), CborDefinite] Dictionary<int, bool> Value) : CborRecord;
            [CborSerializable, CborMap]
            public partial record ClassMap([CborProperty(0)] int Value) : CborRecord;
            """;
        GeneratorDriver driver = GenerateWithoutErrors("CapacityProbe", source);
        string generated = GeneratedText(driver, $"CapacityProbe.{model}");
        string assignment = Assert.Single(generated.Split('\n'), line => line.Contains($"{target} = ", StringComparison.Ordinal));
        Assert.Contains($"global::System.Math.Clamp({size}, 0, 256)", assignment, StringComparison.Ordinal);
        int readSize = generated.IndexOf($"int {size} = {(size == "_size" ? "_r" : "reader")}.ReadSize();", StringComparison.Ordinal);
        int constructor = generated.IndexOf(assignment, StringComparison.Ordinal);
        Assert.InRange(readSize, 0, constructor - 1);
        if (shapeCheck is not null)
        {
            Assert.InRange(generated.IndexOf(shapeCheck, StringComparison.Ordinal), readSize + 1, constructor - 1);
        }
    }
}
