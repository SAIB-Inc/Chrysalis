using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    private static readonly string[] EncodedListPrefixes = ["83", "9F"];
    private static readonly string[] EncodedMapPrefixes = ["A2", "BF"];
    private const string EncodedListDecl = """
        [CborSerializable]
        public readonly partial record struct EncodedListBox : ICborType
        {
            public partial List<CborEncodedValue> Value { get; }
        }
        """;

    private static Assembly CompileCollectionProbe(string name, string declarations)
    {
        string source = $$"""
            #nullable enable
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace {{name}};
            {{declarations}}
            """;
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation(name, source), out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(image.ToArray());
    }

    private static (object Model, int Consumed) ReadBox(Type type, ReadOnlyMemory<byte> input)
    {
        MethodInfo read = type.GetMethod("Read", [typeof(ReadOnlyMemory<byte>), typeof(int).MakeByRefType()])!;
        object?[] args = [input, 0];
        object model = read.Invoke(null, args)!;
        return (model, (int)args[1]!);
    }

    private static CborDecodeException AssertGetterDecodeError(Type type, byte[] wire)
    {
        (object model, int consumed) = ReadBox(type, wire);
        Assert.Equal(wire.Length, consumed);
        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => type.GetProperty("Value")!.GetValue(model));
        return Assert.IsType<CborDecodeException>(failure.InnerException);
    }

    [Fact]
    public void SyntheticEncodedListBorrowsDefiniteEntries()
    {
        Type boxType = CompileCollectionProbe("EncodedListBorrowProbe", EncodedListDecl)
            .GetType("EncodedListBorrowProbe.EncodedListBox", throwOnError: true)!;
        PropertyInfo prop = boxType.GetProperty("Value")!;

        foreach (string open in EncodedListPrefixes)
        {
            string close = open == "9F" ? "FF" : "";
            byte[] wire = Convert.FromHexString(open + "421122" + "D818D818423344" + "C1D8185F41AA41BBFF" + close);
            byte[] buf = [0xAA, 0xBB, .. wire, 0xEE];
            ReadOnlyMemory<byte> input = buf.AsMemory(2, wire.Length + 1);

            (object model, int consumed) = ReadBox(boxType, input);
            Assert.Equal(wire.Length, consumed);
            Assert.Equal(0xEE, input.Span[consumed]);
            List<CborEncodedValue> items = (List<CborEncodedValue>)prop.GetValue(model)!;
            Assert.Equal(3, items.Count);
            Assert.True(items[0].Value.Equals(input.Slice(2, 2)));
            Assert.True(items[1].Value.Equals(input.Slice(9, 2)));

            buf.AsSpan().Fill(0x77);
            Assert.Equal(Convert.FromHexString("7777"), items[0].Value.ToArray());
            Assert.Equal(Convert.FromHexString("7777"), items[1].Value.ToArray());
            Assert.Equal(Convert.FromHexString("AABB"), items[2].Value.ToArray());
        }
    }

    [Fact]
    public void SyntheticEncodedListPreservesOwnedChunksAndSlotErrors()
    {
        Type boxType = CompileCollectionProbe("EncodedListControlProbe", EncodedListDecl)
            .GetType("EncodedListControlProbe.EncodedListBox", throwOnError: true)!;
        PropertyInfo prop = boxType.GetProperty("Value")!;

        foreach (string open in EncodedListPrefixes)
        {
            string close = open == "9F" ? "FF" : "";
            byte[] wire = Convert.FromHexString(open + "421122" + "D8185F41AA41BBFF" + "C1D8185F4041CCFF" + close);
            byte[] buf = [0xAA, 0xBB, .. wire, 0xEE];
            ReadOnlyMemory<byte> input = buf.AsMemory(2, wire.Length + 1);

            (object model, int consumed) = ReadBox(boxType, input);
            Assert.Equal(wire.Length, consumed);
            List<CborEncodedValue> items = (List<CborEncodedValue>)prop.GetValue(model)!;
            buf.AsSpan().Fill(0x77);
            Assert.Equal(Convert.FromHexString("AABB"), items[1].Value.ToArray());
            Assert.Equal(Convert.FromHexString("CC"), items[2].Value.ToArray());
        }

        CborDecodeException thirdTag = AssertGetterDecodeError(boxType, Convert.FromHexString("81D818D818D81841AA"));
        Assert.Equal("EncodedListControlProbe.EncodedListBox.Value.item", thirdTag.TypePath);
        Assert.Equal("Chrysalis.Codec.Types.CborEncodedValue", thirdTag.Expected);
        Assert.Equal("semantic tag", thirdTag.Actual);
        Assert.Equal(6, thirdTag.Offset);

        CborDecodeException wrongKind = AssertGetterDecodeError(boxType, Convert.FromHexString("81D8186161"));
        Assert.Equal("EncodedListControlProbe.EncodedListBox.Value.item", wrongKind.TypePath);
        Assert.Equal("text string", wrongKind.Actual);
        Assert.Equal(4, wrongKind.Offset);

        _ = Assert.Throws<TargetInvocationException>(() => ReadBox(boxType, Convert.FromHexString("8142AA")));
    }

    [Fact]
    public void SyntheticEncodedMapsCompileBorrowDefiniteKeepOwnedChunksAndPinSlotErrors()
    {
        Assembly assembly = CompileCollectionProbe("EncodedMapProbe", """
            [CborSerializable]
            public readonly partial record struct EncodedKeyMapBox : ICborType
            {
                public partial Dictionary<CborEncodedValue, int> Value { get; }
            }
            [CborSerializable]
            public readonly partial record struct EncodedValMapBox : ICborType
            {
                public partial Dictionary<int, CborEncodedValue> Value { get; }
            }
            [CborSerializable]
            public readonly partial record struct EncodedBothMapBox : ICborType
            {
                public partial Dictionary<CborEncodedValue, CborEncodedValue> Value { get; }
            }
            """);
        Type keyBox = assembly.GetType("EncodedMapProbe.EncodedKeyMapBox", throwOnError: true)!;
        Type valBox = assembly.GetType("EncodedMapProbe.EncodedValMapBox", throwOnError: true)!;
        Type bothBox = assembly.GetType("EncodedMapProbe.EncodedBothMapBox", throwOnError: true)!;

        foreach (string open in EncodedMapPrefixes)
        {
            string close = open == "BF" ? "FF" : "";

            byte[] kWire = Convert.FromHexString(open + "D818D818421122" + "01" + "D8185F41AA41BBFF" + "02" + close);
            byte[] kBuf = [0xAA, 0xBB, .. kWire, 0xEE];
            ReadOnlyMemory<byte> kIn = kBuf.AsMemory(2, kWire.Length + 1);
            (object kModel, int kc) = ReadBox(keyBox, kIn);
            Assert.Equal(kWire.Length, kc);
            KeyValuePair<CborEncodedValue, int>[] kEntries = [.. (Dictionary<CborEncodedValue, int>)keyBox.GetProperty("Value")!.GetValue(kModel)!];
            Assert.True(kEntries[0].Key.Value.Equals(kIn.Slice(6, 2)));
            Assert.Equal(1, kEntries[0].Value);
            Assert.Equal(2, kEntries[1].Value);
            kBuf.AsSpan().Fill(0x55);
            Assert.Equal(Convert.FromHexString("5555"), kEntries[0].Key.Value.ToArray());
            Assert.Equal(Convert.FromHexString("AABB"), kEntries[1].Key.Value.ToArray());

            byte[] vWire = Convert.FromHexString(open + "01" + "D818423344" + "02" + "C1D8185F41CC41DDFF" + close);
            byte[] vBuf = [0xAA, 0xBB, .. vWire, 0xEE];
            ReadOnlyMemory<byte> vIn = vBuf.AsMemory(2, vWire.Length + 1);
            (object vModel, int vc) = ReadBox(valBox, vIn);
            Assert.Equal(vWire.Length, vc);
            Dictionary<int, CborEncodedValue> vMap = (Dictionary<int, CborEncodedValue>)valBox.GetProperty("Value")!.GetValue(vModel)!;
            Assert.True(vMap[1].Value.Equals(vIn.Slice(5, 2)));
            vBuf.AsSpan().Fill(0x66);
            Assert.Equal(Convert.FromHexString("6666"), vMap[1].Value.ToArray());
            Assert.Equal(Convert.FromHexString("CCDD"), vMap[2].Value.ToArray());

            byte[] bWire = Convert.FromHexString(open + "4101" + "D818427788" + "D8185F4102FF" + "5F41EEFF" + close);
            byte[] bBuf = [0xAA, 0xBB, .. bWire, 0xEE];
            ReadOnlyMemory<byte> bIn = bBuf.AsMemory(2, bWire.Length + 1);
            (object bModel, int bc) = ReadBox(bothBox, bIn);
            Assert.Equal(bWire.Length, bc);
            KeyValuePair<CborEncodedValue, CborEncodedValue>[] bEntries = [.. (Dictionary<CborEncodedValue, CborEncodedValue>)bothBox.GetProperty("Value")!.GetValue(bModel)!];
            Assert.True(bEntries[0].Key.Value.Equals(bIn.Slice(2, 1)));
            Assert.True(bEntries[0].Value.Value.Equals(bIn.Slice(6, 2)));
            bBuf.AsSpan().Fill(0x44);
            Assert.Equal(Convert.FromHexString("02"), bEntries[1].Key.Value.ToArray());
            Assert.Equal(Convert.FromHexString("EE"), bEntries[1].Value.Value.ToArray());
        }

        CborDecodeException keyThird = AssertGetterDecodeError(keyBox, Convert.FromHexString("A1D818D818D81841AA01"));
        Assert.Equal("EncodedMapProbe.EncodedKeyMapBox.Value.entry", keyThird.TypePath);
        Assert.Equal("Chrysalis.Codec.Types.CborEncodedValue", keyThird.Expected);
        Assert.Equal("semantic tag", keyThird.Actual);
        Assert.Equal(6, keyThird.Offset);

        CborDecodeException valThird = AssertGetterDecodeError(valBox, Convert.FromHexString("A101D818D818D81841AA"));
        Assert.Equal("EncodedMapProbe.EncodedValMapBox.Value.entry", valThird.TypePath);
        Assert.Equal("Chrysalis.Codec.Types.CborEncodedValue", valThird.Expected);
        Assert.Equal("semantic tag", valThird.Actual);
        Assert.Equal(7, valThird.Offset);

        CborDecodeException keyWrong = AssertGetterDecodeError(keyBox, Convert.FromHexString("A1616101"));
        Assert.Equal("EncodedMapProbe.EncodedKeyMapBox.Value.entry", keyWrong.TypePath);
        Assert.Equal("text string", keyWrong.Actual);
        Assert.Equal(2, keyWrong.Offset);

        CborDecodeException valWrong = AssertGetterDecodeError(valBox, Convert.FromHexString("A1016161"));
        Assert.Equal("EncodedMapProbe.EncodedValMapBox.Value.entry", valWrong.TypePath);
        Assert.Equal("text string", valWrong.Actual);
        Assert.Equal(3, valWrong.Offset);
    }
}
