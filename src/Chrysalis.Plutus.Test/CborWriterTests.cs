using System.Numerics;
using Chrysalis.Plutus.Cbor;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Xunit;

namespace Chrysalis.Plutus.Test;

/// <summary>
/// Verifies CborWriter.EncodePlutusData produces spec-compliant CBOR
/// matching the Cardano node's serialiseData builtin (Plutus Core Spec Appendix B).
/// </summary>
public class CborWriterTests
{
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    // --- Constructor encoding ---
    // Non-empty fields use indefinite-length arrays (0x9F...0xFF), matching Aiken/Cardano node.
    // Empty fields use definite-length (0x80).

    [Fact]
    public void Constr0_WithInteger_UsesIndefiniteArray()
    {
        // Constr 0 [I 42] → tag 121 + 9F 18 2A FF
        PlutusData data = new PlutusDataConstr(0, [new PlutusDataInteger(42)]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("D8799F182AFF", Hex(result));
    }

    [Fact]
    public void Constr1_Empty_UsesDefiniteArray()
    {
        // Constr 1 [] → tag 122 + 80
        PlutusData data = new PlutusDataConstr(1, []);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("D87A80", Hex(result));
    }

    [Fact]
    public void Constr6_UsesTag127()
    {
        // Constr 6 [I 1] → tag 127 + 9F 01 FF
        PlutusData data = new PlutusDataConstr(6, [new PlutusDataInteger(1)]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("D87F9F01FF", Hex(result));
    }

    [Fact]
    public void Constr7_UsesTag1280()
    {
        // Constr 7 [I 1] → tag 1280 + 9F 01 FF
        PlutusData data = new PlutusDataConstr(7, [new PlutusDataInteger(1)]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("D905009F01FF", Hex(result));
    }

    [Fact]
    public void Constr200_UsesTag102_WithIndefiniteFields()
    {
        // Constr 200 [B #CAFE] → tag 102 + definite [200, indef [42 CAFE FF]]
        PlutusData data = new PlutusDataConstr(200, [new PlutusDataByteString(new byte[] { 0xCA, 0xFE })]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("D8668218C89F42CAFEFF", Hex(result));
    }

    // --- List encoding (defaultEncodeList: empty definite 0x80, non-empty indefinite) ---

    [Fact]
    public void EmptyList_UsesDefiniteArray()
    {
        PlutusData data = new PlutusDataList([]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("80", Hex(result));
    }

    [Theory]
    [InlineData("820102", "820102")]
    [InlineData("9f0102ff", "9f0102ff")]
    [InlineData("9fff", "9fff")]
    [InlineData("d879820102", "d879820102")]
    [InlineData("d8799f0102ff", "d879820102")]
    public void DefiniteFormIsUnchanged(string inputHex, string expectedHex)
    {
        PlutusData data = CborReader.DecodePlutusData(Convert.FromHexString(inputHex));
        Assert.Equal(Convert.FromHexString(expectedHex), CborWriter.EncodePlutusDataDefinite(data));
    }

    [Fact]
    public void ListOfIntegers_UsesIndefiniteArray()
    {
        PlutusData data = new PlutusDataList([
            new PlutusDataInteger(1),
            new PlutusDataInteger(2),
            new PlutusDataInteger(3)
        ]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("9F010203FF", Hex(result));
    }

    // --- Map encoding (definite-length, unchanged) ---

    [Fact]
    public void SingleEntryMap_UsesDefiniteLength()
    {
        PlutusData data = new PlutusDataMap([(new PlutusDataInteger(1), new PlutusDataInteger(2))]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("A10102", Hex(result));
    }

    // --- ByteString encoding ---

    [Fact]
    public void ShortByteString_UsesDefiniteLength()
    {
        PlutusData data = new PlutusDataByteString(new byte[] { 0xCA, 0xFE });
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("42CAFE", Hex(result));
    }

    [Fact]
    public void ByteString64Bytes_UsesDefiniteLength()
    {
        byte[] payload = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            payload[i] = (byte)i;
        }

        PlutusData data = new PlutusDataByteString(payload);
        byte[] result = CborWriter.EncodePlutusData(data);

        // 58 40 = definite bytestring, length 64; no chunking
        Assert.Equal(2 + 64, result.Length);
        Assert.Equal(0x58, result[0]);
        Assert.Equal(0x40, result[1]);
    }

    [Fact]
    public void ByteString65Bytes_UsesChunkedIndefiniteLength()
    {
        byte[] payload = new byte[65];
        for (int i = 0; i < 65; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        PlutusData data = new PlutusDataByteString(payload);
        byte[] result = CborWriter.EncodePlutusData(data);

        // 5F (indef) + 5840 <64 bytes> + 41 <1 byte> + FF (break)
        Assert.Equal(1 + 2 + 64 + 1 + 1 + 1, result.Length);
        Assert.Equal(0x5F, result[0]);                 // indefinite start
        Assert.Equal(0x58, result[1]);                 // chunk 1 header
        Assert.Equal(0x40, result[2]);                 // chunk 1 length = 64
        Assert.Equal(0x41, result[3 + 64]);            // chunk 2 header (length 1)
        Assert.Equal(0xFF, result[^1]);                // break
    }

    [Fact]
    public void ByteString77Bytes_ChunkedAs64Plus13()
    {
        byte[] payload = new byte[77];
        for (int i = 0; i < 77; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        PlutusData data = new PlutusDataByteString(payload);
        byte[] result = CborWriter.EncodePlutusData(data);

        // 5F + 5840 <64 bytes> + 4D <13 bytes> + FF
        Assert.Equal(1 + 2 + 64 + 1 + 13 + 1, result.Length);
        Assert.Equal(0x5F, result[0]);
        Assert.Equal(0x4D, result[3 + 64]);            // 0x4D = major 2, length 13
        Assert.Equal(0xFF, result[^1]);
    }

    [Fact]
    public void ByteString128Bytes_ChunkedAs64Plus64()
    {
        byte[] payload = new byte[128];
        for (int i = 0; i < 128; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        PlutusData data = new PlutusDataByteString(payload);
        byte[] result = CborWriter.EncodePlutusData(data);

        // 5F + 5840 <64 bytes> + 5840 <64 bytes> + FF
        Assert.Equal(1 + 2 + 64 + 2 + 64 + 1, result.Length);
        Assert.Equal(0x5F, result[0]);
        Assert.Equal(0x58, result[1 + 2 + 64]);       // second chunk header
        Assert.Equal(0x40, result[1 + 2 + 64 + 1]);   // second chunk length = 64
        Assert.Equal(0xFF, result[^1]);
    }

    // --- Integer encoding (unchanged, verify still correct) ---

    [Fact]
    public void SmallInteger()
    {
        PlutusData data = new PlutusDataInteger(0);
        Assert.Equal("00", Hex(CborWriter.EncodePlutusData(data)));
    }

    [Fact]
    public void NegativeInteger()
    {
        PlutusData data = new PlutusDataInteger(-1);
        Assert.Equal("20", Hex(CborWriter.EncodePlutusData(data)));
    }

    // --- Spec example: Constr 0 [I 42] from docs/UPLC.md §6.3 ---

    [Fact]
    public void SpecExample_Constr0_I42()
    {
        // Non-empty: D8 79 9F 18 2A FF (indefinite array, matching Aiken)
        PlutusData data = new PlutusDataConstr(0, [new PlutusDataInteger(42)]);
        byte[] result = CborWriter.EncodePlutusData(data);
        Assert.Equal("D8799F182AFF", Hex(result));
    }

    public static TheoryData<string, byte[]?, BigInteger?, byte[]> SerialiseCases()
    {
        TheoryData<string, byte[]?, BigInteger?, byte[]> cases = [];
        int fixtureCount = 0;
        foreach (NodeBudgetTests.SerialiseCase entry in NodeBudgetTests.Fixture.Serialise)
        {
            cases.Add(entry.Id, entry.Flat, null, entry.Bytes);
            fixtureCount++;
        }
        Assert.Equal(89, fixtureCount);
        // Source rows from PlutusCore/Data.hs:149-160 and cborg Write.hs:150-177,678-689.
        cases.Add("source:Constr(-1)[]", null, -BigInteger.One, Convert.FromHexString("d866822080"));
        cases.Add("source:Constr(2^64)[]", null, BigInteger.One << 64, Convert.FromHexString("d86682c24901000000000000000080"));
        cases.Add("source:Constr(2^512)[]", null, BigInteger.One << 512, Convert.FromHexString("d86682c2584101" + new string('0', 128) + "80"));
        return cases;
    }

    [Theory]
    [MemberData(nameof(SerialiseCases))]
    public void SerialiseDataWritesTheNodesBytes(string id, byte[]? flat, BigInteger? sourceTag, byte[] expected)
    {
        _ = id;
        byte[] actual;
        if (flat is not null)
        {
            Term<DeBruijn> applied = new ApplyTerm<DeBruijn>(
                FlatDecoder.DecodeProgram(flat).Term,
                new ConstTerm<DeBruijn>(new DataConstant(new PlutusDataConstr(0, []))));
            (Term<DeBruijn> result, _) = Evaluator.EvaluateTerm(applied);
            actual = Assert.IsType<ByteStringConstant>(Assert.IsType<ConstTerm<DeBruijn>>(result).Value).Value.ToArray();
        }
        else
        {
            actual = CborWriter.EncodePlutusData(new PlutusDataConstr(sourceTag!.Value, []));
        }
        Assert.Equal(expected, actual);
    }

    private static byte[] PlutusScript()
    {
        using MemoryStream stream = (MemoryStream)Golden.Open("g1/auction_1-1.flat");
        return stream.ToArray();
    }

    [Fact]
    public void FlatConstantsUseTheNodesForm()
    {
        byte[] plutus = PlutusScript();
        Assert.Equal(plutus, FlatEncoder.EncodeProgram(FlatDecoder.DecodeProgram(plutus)));
    }

    [Fact]
    public void ApplyParameterWritesPlutusBytes()
    {
        byte[] plutus = PlutusScript();
        Program<DeBruijn> decoded = FlatDecoder.DecodeProgram(plutus);
        ApplyTerm<DeBruijn> outer = Assert.IsType<ApplyTerm<DeBruijn>>(decoded.Term);
        PlutusData parameter = Assert.IsType<DataConstant>(Assert.IsType<ConstTerm<DeBruijn>>(outer.Argument).Value).Value;
        ApplyTerm<DeBruijn> scriptApp = Assert.IsType<ApplyTerm<DeBruijn>>(outer.Function);
        PlutusData owned = Assert.IsType<DataConstant>(Assert.IsType<ConstTerm<DeBruijn>>(scriptApp.Argument).Value).Value;

        byte[] node = FlatBytes(CborWriter.EncodePlutusData(owned));
        byte[] def = FlatBytes(CborWriter.EncodePlutusDataDefinite(owned));
        Assert.NotEqual(node, def);

        byte[] encoded = FlatEncoder.EncodeProgram(new(decoded.Version, outer.Function));
        byte[] input = encoded.AsSpan().IndexOf(def) >= 0 ? encoded : Replace(encoded, node, def);
        _ = Once(input, def);

        byte[] paramDef = CborWriter.EncodePlutusDataDefinite(parameter);
        Assert.NotEqual(CborWriter.EncodePlutusData(parameter), paramDef);

        Assert.Equal(plutus, ScriptApplicator.ApplyParameter(input, paramDef));
    }

    private static byte[] FlatBytes(byte[] cbor)
    {
        BitWriter writer = new();
        FlatEncoder.EncodeByteString(writer, cbor);
        byte[] padded = writer.ToArray();
        Assert.Equal(1, padded[0]);
        return padded[1..];
    }

    private static byte[] Replace(byte[] source, byte[] oldSpan, byte[] newSpan)
    {
        int index = Once(source, oldSpan);
        byte[] result = new byte[source.Length - oldSpan.Length + newSpan.Length];
        source.AsSpan(0, index).CopyTo(result);
        newSpan.CopyTo(result.AsSpan(index));
        source.AsSpan(index + oldSpan.Length).CopyTo(result.AsSpan(index + newSpan.Length));
        return result;
    }

    private static int Once(byte[] source, byte[] span)
    {
        int first = source.AsSpan().IndexOf(span);
        Assert.True(first >= 0, "Expected data constant span not found.");
        Assert.True(source.AsSpan(first + 1).IndexOf(span) < 0, "Expected data constant span to appear only once.");
        return first;
    }
}
