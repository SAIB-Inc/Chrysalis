using System.Reflection;
using Chrysalis.Codec.Serialization;
using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void EagerListFieldTrackingUsesBitmaskUpToSixtyFourSlotsAndBoolArrayAbove(int slots, bool useBitmask)
    {
        string parameters = string.Join(", ", Enumerable.Range(0, slots).Select(i =>
            $"[CborOrder({i})] {(i == slots - 1 ? "int" : "int?")} P{i}"));
        string source = $$"""
            #nullable enable
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace FieldTrackingProbe;
            [CborSerializable, CborList]
            public partial record Model{{slots}}({{parameters}}) : CborRecord;
            """;

        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation($"FieldTracking{slots}", source), out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        string typeName = $"FieldTrackingProbe.Model{slots}";
        string generated = GeneratedText(driver, typeName);
        if (useBitmask)
        {
            Assert.Contains("ulong fieldsRead = 0;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("new bool[", generated, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"bool[] fieldsRead = new bool[{slots}];", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("ulong fieldsRead", generated, StringComparison.Ordinal);
        }

        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        Type modelType = Assembly.Load(image.ToArray()).GetType(typeName, throwOnError: true)!;
        MethodInfo read = modelType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>), typeof(int).MakeByRefType()])!;

        byte[] full = BuildSparseListPayload(slots, includeRequiredTail: true);
        object?[] fullArgs = [new ReadOnlyMemory<byte>(full), 0];
        object decoded = read.Invoke(null, fullArgs)!;
        Assert.Equal(2 + slots, (int)fullArgs[1]!);
        Assert.Equal(1, modelType.GetProperty("P0")!.GetValue(decoded));
        Assert.Equal(1, modelType.GetProperty("P31")!.GetValue(decoded));
        Assert.Null(modelType.GetProperty("P1")!.GetValue(decoded));
        Assert.Equal(23, modelType.GetProperty($"P{slots - 1}")!.GetValue(decoded));

        byte[] missingLast = BuildSparseListPayload(slots, includeRequiredTail: false);
        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() =>
            read.Invoke(null, [new ReadOnlyMemory<byte>(missingLast), 0]));
        CborDecodeException error = Assert.IsType<CborDecodeException>(failure.InnerException);
        int expectedOffset = 2 + slots - 1;
        string expectedPath = $"{typeName}.P{slots - 1}";
        Assert.Equal(expectedOffset, error.Offset);
        Assert.Equal(expectedPath, error.TypePath);
        Assert.Equal("required field", error.Expected);
        Assert.Equal("missing", error.Actual);
        Assert.Equal($"CBOR {expectedPath} at byte {expectedOffset}: expected required field; actual missing.", error.Message);
    }

    private static byte[] BuildSparseListPayload(int slots, bool includeRequiredTail)
    {
        int count = includeRequiredTail ? slots : slots - 1;
        byte[] payload = new byte[2 + count + 1];
        payload[0] = 0x98;
        payload[1] = checked((byte)count);
        for (int i = 0; i < count; i++)
        {
            payload[2 + i] = i is 0 or 31 ? (byte)0x01 : i == slots - 1 ? (byte)0x17 : (byte)0xF6;
        }
        payload[^1] = 0xAA;
        return payload;
    }
}
