using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    static GeneratorIsolationTests() => VerifySourceGenerators.Initialize();

    [Fact]
    public Task RecordSnapshot() => VerifySource("""
        [CborSerializable]
        public readonly partial record struct Value : ICborType
        {
            public partial int Item { get; }
        }
        public readonly struct ValueValidator : ICborValidator<Value>
        {
            public bool Validate(Value value) => value.Item >= 0;
        }
        [CborSerializable]
        public partial record ClassValue(int Item) : CborRecord;
        """);

    [Fact]
    public Task ListSnapshot() => VerifySource("""
        [CborSerializable, CborList]
        public readonly partial record struct Value : ICborType
        {
            [CborOrder(0)] public partial int Number { get; }
            [CborOrder(1)] public partial string Text { get; }
        }
        """);

    [Fact]
    public Task MapSnapshot() => VerifySource("""
        [CborSerializable, CborMap]
        public readonly partial record struct Value : ICborType
        {
            [CborProperty(0)] public partial int Number { get; }
            [CborProperty(1)] public partial string Text { get; }
        }
        [CborSerializable, CborMap]
        public partial record ClassValue([CborProperty(0)] int Number) : CborRecord;
        """);

    [Fact]
    public Task ConstructorSnapshot() => VerifySource("""
        [CborSerializable, CborConstr(2)]
        public readonly partial record struct Value : ICborType
        {
            [CborOrder(0)] public partial int Number { get; }
        }
        [CborSerializable, CborConstr(2)]
        public partial record ClassValue([CborOrder(0)] int Number) : CborRecord;
        """);

    [Fact]
    public Task InterfaceUnionSnapshot() => VerifySource("""
        [CborSerializable, CborUnion]
        public partial interface IValue : ICborType;
        [CborSerializable]
        public readonly partial record struct Number : IValue
        {
            public partial int Value { get; }
        }
        [CborSerializable]
        public readonly partial record struct Text : IValue
        {
            public partial string Value { get; }
        }
        [CborSerializable, CborUnion]
        public abstract partial record Message : CborRecord;
        [CborSerializable, CborList, CborIndex(0)]
        public partial record Count([CborOrder(0)] int Kind, [CborOrder(1)] int Value) : Message;
        [CborSerializable, CborList, CborIndex(0)]
        public partial record TextMessage([CborOrder(0)] int Kind, [CborOrder(1)] string Value) : Message;
        [CborSerializable, CborList, CborIndex(1)]
        public partial record Flag([CborOrder(0)] int Kind, [CborOrder(1)] bool Value) : Message;
        public readonly struct CountValidator : ICborValidator<Count>
        {
            public bool Validate(Count value) => value.Value >= 0;
        }
        """);

    [Fact]
    public Task GenericUnionSnapshot() => VerifySource("""
        [CborSerializable, CborUnion]
        public partial interface IValue<T> : ICborType;
        [CborSerializable]
        public readonly partial record struct Values<T> : IValue<T>
        {
            public partial List<T> Value { get; }
        }
        [CborSerializable, CborTag(258)]
        public readonly partial record struct TaggedValues<T> : IValue<T>
        {
            public partial List<T> Value { get; }
        }
        """);

    [Fact]
    public Task NullableSnapshot() => VerifySource("""
        [CborSerializable, CborList]
        public readonly partial record struct Value : ICborType
        {
            [CborOrder(0), CborNullable] public partial int? Number { get; }
        }
        """);

    [Fact]
    public Task TagSnapshot() => VerifySource("""
        [CborSerializable, CborTag(24)]
        public readonly partial record struct Value : ICborType
        {
            public partial byte[] Bytes { get; }
        }
        """);

    [Fact]
    public Task IndefiniteSnapshot() => VerifySource("""
        [CborSerializable, CborList, CborIndefinite]
        public readonly partial record struct Value : ICborType
        {
            [CborOrder(0)] public partial int Number { get; }
        }
        """);

    [Fact]
    public Task UnionHintSnapshot() => VerifySource("""
        [CborSerializable, CborList]
        public readonly partial record struct Value : ICborType
        {
            [CborOrder(0)] public partial int Kind { get; }
            [CborOrder(1), CborUnionHint(nameof(Kind), 0, typeof(Number))]
            public partial IValue Item { get; }
        }
        [CborSerializable, CborUnion]
        public partial interface IValue : ICborType;
        [CborSerializable]
        public readonly partial record struct Number : IValue
        {
            public partial int Item { get; }
        }
        """);

    private static Task VerifySource(string sample)
    {
        string source = """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace Snapshot;
            """ + "\n" + sample;
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation("GeneratorSnapshots", source), out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.GeneratedTrees);
        Assert.Empty(output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        VerifySettings settings = new();
        settings.UseDirectory("Snapshots");
        settings.DisableDiff();
        return Verifier.Verify(driver, settings);
    }
}
