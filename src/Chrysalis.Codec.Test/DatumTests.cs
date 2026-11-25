using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.Test;

// Test-specific types for SundaeSwap datum testing

/// <summary>
/// Represents a single asset as a list [PolicyId, AssetName]
/// </summary>
[CborSerializable]
[CborList]
[CborIndefinite]
public partial record AssetClass(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] byte[] AssetName
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

/// <summary>
/// MultisigScript for SundaeSwap testing
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record MultisigScript : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

[CborSerializable]
[CborConstr(0)]
[CborIndefinite]
public partial record Signature(
    [CborOrder(0)] byte[] KeyHash
) : MultisigScript;

[CborSerializable]
[CborConstr(1)]
public partial record AllOf(
    [CborOrder(0)] CborList<MultisigScript> Scripts
) : MultisigScript;

[CborSerializable]
[CborConstr(2)]
public partial record AnyOf(
    [CborOrder(0)] CborList<MultisigScript> Scripts
) : MultisigScript;

[CborSerializable]
[CborConstr(3)]
public partial record AtLeast(
    [CborOrder(0)] ulong Required,
    [CborOrder(1)] CborList<MultisigScript> Scripts
) : MultisigScript;

[CborSerializable]
[CborConstr(4)]
public partial record Before(
    [CborOrder(0)] PosixTime Time
) : MultisigScript;

[CborSerializable]
[CborConstr(5)]
public partial record After(
    [CborOrder(0)] PosixTime Time
) : MultisigScript;

[CborSerializable]
[CborConstr(6)]
public partial record Script(
    [CborOrder(0)] byte[] ScriptHash
) : MultisigScript;

/// <summary>
/// SundaeSwap V2 liquidity pool datum structure
/// Constructor 0 datum with 8 fields
/// </summary>
[CborSerializable]
[CborConstr(0)]
[CborIndefinite]
public partial record SundaeSwapPoolDatum(
    [CborOrder(0)]
    byte[] Identifier,  // Pool unique identifier (28 bytes)

    [CborOrder(1)]
    [CborIndefinite]
    List<AssetClass> Assets,  // Tuple of two assets [(PolicyId, AssetName), (PolicyId, AssetName)]

    [CborOrder(2)]
    ulong CirculatingLp,  // Total LP tokens in circulation

    [CborOrder(3)]
    ulong BidFeesPerTenThousand,  // Basis points for A->B swaps (60 = 0.6%)

    [CborOrder(4)]
    ulong AskFeesPerTenThousand,  // Basis points for B->A swaps (60 = 0.6%)

    [CborOrder(5)]
    [CborIndefinite]
    ICborOption<MultisigScript> FeeManager,  // Optional fee manager script

    [CborOrder(6)]
    ulong MarketOpen,  // UNIX timestamp when trading is allowed (0 = open)
    [CborOrder(7)]
    ulong ProtocolFees  // ADA set aside for protocol fees
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}

[PlutusData(0)]
public partial record X46Collections(
    [CborOrder(0)] List<byte[]> Items,
    [CborOrder(1)] Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> Pairs
) : CborRecord;

public class PlutusConstrTests
{
    [Fact]
    public void PlutusConstr_Create_Empty_NoDoubleNesting()
    {
        // Constr(0, []) should produce Tag(121) + empty array
        // D879 = Tag(121), 80 = 0-element definite array
        PlutusConstr constr = PlutusConstr.Create(0, CborList.Create<IPlutusData>([]));
        string hex = Convert.ToHexString(constr.Raw.ToArray());
        Assert.Equal("D87980", hex);
    }

    [Fact]
    public void PlutusConstr_Create_WithFields_NoDoubleNesting()
    {
        // Constr(0, [42]) should produce Tag(121) + 1-element array + int 42
        // D879 = Tag(121), 81 = 1-element array, 182A = int 42
        PlutusConstr constr = PlutusConstr.Create(0, CborList.Create<IPlutusData>([PlutusInt64.Create(42)]));
        string hex = Convert.ToHexString(constr.Raw.ToArray());
        Assert.Equal("D87981182A", hex);
    }

    [Fact]
    public void PlutusConstr_Create_HighTag_NoDoubleNesting()
    {
        // Constr(7, [42]) should produce Tag(1280) + 1-element array + int 42
        // D90500 = Tag(1280), 81 = 1-element array, 182A = int 42
        PlutusConstr constr = PlutusConstr.Create(7, CborList.Create<IPlutusData>([PlutusInt64.Create(42)]));
        string hex = Convert.ToHexString(constr.Raw.ToArray());
        Assert.Equal("D9050081182A", hex);
    }

    [Fact]
    public void PlutusConstr_Create_RoundTrip()
    {
        // Create, then read back and verify fields
        PlutusConstr constr = PlutusConstr.Create(0, CborList.Create<IPlutusData>([
            PlutusInt64.Create(1),
            PlutusInt64.Create(2),
            PlutusBoundedBytes.Create(new byte[] { 0xCA, 0xFE })
        ]));

        // Read back
        PlutusConstr readBack = PlutusConstr.Read(constr.Raw);
        CborList<IPlutusData> fields = readBack.Fields;
        Assert.False(fields.IsIndefinite);
        Assert.False(fields.HasSetTag);
        Assert.Equal(3, fields.Count);
    }
}

public class PlutusBigIntTests
{
    // cardano-node 11.1.3 PV11 serialiseData output (plutigo v0.12.3, commit c4a50c15),
    // verified against CostModels/manifest.json before reading.
    internal static readonly Lazy<Dictionary<string, string>> NodeRows = new(() =>
    {
        string root = Path.Combine(AppContext.BaseDirectory, "TestData", "CostModels");
        const string name = "plutigo-v0.12.3/node_ground_truth_pv11.json.gz";
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, name));
        Assert.Equal(manifest.RootElement.EnumerateArray().Single(entry => entry.GetProperty("path").GetString() == name).GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using MemoryStream compressed = new(bytes);
        using GZipStream gzip = new(compressed, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        return document.RootElement.GetProperty("serialise").EnumerateArray().ToDictionary(entry => entry.GetProperty("id").GetString()!, entry => entry.GetProperty("bytes").GetString()!, StringComparer.Ordinal);
    });

    [Theory]
    [InlineData("SER-int-18446744073709551616", false, 0x01, 0x00, 9)]
    [InlineData("SER-int--18446744073709551617", true, 0x01, 0x00, 9)]
    [InlineData("SER-int-2^512", false, 0xFF, 0xFF, 64)]
    [InlineData("SER-int-2^513", false, 0x01, 0x00, 65)]
    [InlineData("SER-int-2^521neg", true, 0xFF, 0xFF, 65)]
    [InlineData("SER-int-2^521", false, 0x01, 0x00, 66)]
    [InlineData("SER-int-2^4097", false, 0x01, 0x00, 513)]
    public void X38BigIntegerWritesTheNodeForm(string row, bool negative, int first, int rest, int length)
    {
        byte[] magnitude = [(byte)first, .. Enumerable.Repeat((byte)rest, length - 1)];
        string expected = NodeRows.Value[row];
        IPlutusBigInt built = negative ? PlutusBigNint.Create(magnitude) : PlutusBigUint.Create(magnitude);
        Assert.Equal(expected, Convert.ToHexStringLower(built.Raw.Span));
        Assert.Equal("d87981" + expected, Convert.ToHexStringLower(PlutusConstr.Create(0, CborList.Create<IPlutusData>([built])).Raw.Span));
        IPlutusData read = CborSerializer.Deserialize<IPlutusData>(Convert.FromHexString(expected));
        ReadOnlyMemory<byte> value = negative ? Assert.IsType<PlutusBigNint>(read).Value : Assert.IsType<PlutusBigUint>(read).Value;
        Assert.Equal(magnitude, value.ToArray());
        Assert.Equal(expected, Convert.ToHexStringLower(CborSerializer.Serialize(read)));
    }

    [Fact]
    public void X38NegativeBigIntegerOf64BytesStaysDefinite()
    {
        // PlutusCore/Data.hs:186: encodeBs writes 64 bytes or fewer as one definite byte string.
        byte[] magnitude = [.. Enumerable.Repeat((byte)0xFF, 64)];
        PlutusBigNint built = PlutusBigNint.Create(magnitude);
        Assert.Equal("c35840" + new string('f', 128), Convert.ToHexStringLower(built.Raw.Span));
        Assert.Equal(magnitude, Assert.IsType<PlutusBigNint>(CborSerializer.Deserialize<IPlutusData>(built.Raw)).Value.ToArray());
    }

    [Fact]
    public void X38ReadBigIntegerKeepsItsBytes()
    {
        // X32WireForms.json:12 (conway.cddl:346-362): 3-byte magnitude in 1+2 chunks, never emitted by Create.
        PlutusBigUint read = Assert.IsType<PlutusBigUint>(CborSerializer.Deserialize<IPlutusData>(Convert.FromHexString("c25f4182420102ff")));
        Assert.Equal("820102", Convert.ToHexStringLower(read.Value.Span));
        Assert.Equal("c25f4182420102ff", Convert.ToHexStringLower(CborSerializer.Serialize(read)));
        Assert.Equal("d87981c25f4182420102ff", Convert.ToHexStringLower(PlutusConstr.Create(0, CborList.Create<IPlutusData>([read])).Raw.Span));
    }
}

public class PlutusConstrBytesTests
{
    [Theory]
    [InlineData("SER-bytes-63", 63)]
    [InlineData("SER-bytes-64", 64)]
    [InlineData("SER-bytes-65", 65)]
    [InlineData("SER-bytes-129", 129)]
    [InlineData("SER-bytes-1000", 1000)]
    public void X46ConstrByteStringsWriteTheNodeForm(string row, int length)
    {
        byte[] bytes = [.. Enumerable.Repeat((byte)0xAB, length)];
        string node = PlutusBigIntTests.NodeRows.Value[row];
        string signature = $"d8799f{node}ff";
        Assert.Equal(signature, Convert.ToHexStringLower(CborSerializer.Serialize(new Signature(bytes))));
        Types.Plutus.Address.VerificationKey key = Types.Plutus.Address.VerificationKey.Create(bytes);
        Assert.Equal($"d87981{node}", Convert.ToHexStringLower(key.Raw.Span));
        Assert.Equal(bytes, key.VerificationKeyHash.ToArray());
        Assert.Equal($"d8798281{node}a1{node}{node}",
            Convert.ToHexStringLower(CborSerializer.Serialize(new X46Collections([bytes], new() { [bytes] = bytes }))));
        Signature read = CborSerializer.Deserialize<Signature>(Convert.FromHexString(signature));
        Assert.Equal(bytes, read.KeyHash);
        Assert.Equal(signature, Convert.ToHexStringLower(CborSerializer.Serialize(read)));
    }

    [Fact]
    public void X46ReadDatumKeepsItsDefiniteBytes()
    {
        // The old writer emitted one definite 65-byte string, which the reader still accepts.
        string input = "d8799f5841" + string.Concat(Enumerable.Repeat("ab", 65)) + "ff";
        Signature read = CborSerializer.Deserialize<Signature>(Convert.FromHexString(input));
        Assert.Equal(65, read.KeyHash.Length);
        Assert.Equal(input, Convert.ToHexStringLower(CborSerializer.Serialize(read)));
        Assert.Equal($"d87a8181{input}", Convert.ToHexStringLower(CborSerializer.Serialize(new AllOf(CborList.Create<MultisigScript>([read])))));
    }
}

public class DatumTests
{
    [Theory]
    [InlineData("d8799f581c64f35d26b237ad58e099041bc14c687ea7fdc58969d7d5b66e2540ef9f9f4040ff9f581cc48cbb3d5e57ed56e276bc45f99ab39abe94e6cd7ac39fb402da47ad480014df105553444dffff1b000000c1972014fa183c183cd8799fd8799f581c0bc4df2c05da7920fe0825b68f83fd96d84f215da6ef360f7057ad83ffff001a402fc340ff")]
    public void SundaeSwapPoolDatumDecodingTest(string cborHex)
    {
        // Convert hex to bytes
        byte[] cborBytes = Convert.FromHexString(cborHex);
        // Deserialize the CBOR data
        SundaeSwapPoolDatum poolDatum = CborSerializer.Deserialize<SundaeSwapPoolDatum>(cborBytes);
        // Verify pool identifier
        Assert.NotNull(poolDatum);
        Assert.Equal(28, poolDatum.Identifier.Length);
        Assert.Equal("64F35D26B237AD58E099041BC14C687EA7FDC58969D7D5B66E2540EF", Convert.ToHexString(poolDatum.Identifier).ToUpperInvariant());

        // Verify assets
        Assert.Equal(2, poolDatum.Assets.Count);

        // First asset should be ADA (empty policy and name)
        AssetClass asset1 = poolDatum.Assets[0];
        Assert.Empty(asset1.PolicyId);
        Assert.Empty(asset1.AssetName);

        // Second asset should be SDAM token
        AssetClass asset2 = poolDatum.Assets[1];
        Assert.Equal("C48CBB3D5E57ED56E276BC45F99AB39ABE94E6CD7AC39FB402DA47AD", Convert.ToHexString(asset2.PolicyId).ToUpperInvariant());
        Assert.Equal("0014DF105553444D", Convert.ToHexString(asset2.AssetName).ToUpperInvariant());
        // Verify other pool parameters
        Assert.Equal(831464150266UL, poolDatum.CirculatingLp);
        Assert.Equal(60UL, poolDatum.BidFeesPerTenThousand); // 0.6%
        Assert.Equal(60UL, poolDatum.AskFeesPerTenThousand); // 0.6%
        Assert.Equal(0UL, poolDatum.MarketOpen); // Market is open
        Assert.Equal(1076872000UL, poolDatum.ProtocolFees);
        // Verify fee manager
        Some<MultisigScript> feeManager = Assert.IsType<Some<MultisigScript>>(poolDatum.FeeManager);
        Signature signature = Assert.IsType<Signature>(feeManager.Value);
        Assert.Equal("0BC4DF2C05DA7920FE0825B68F83FD96D84F215DA6EF360F7057AD83", Convert.ToHexString(signature.KeyHash).ToUpperInvariant());
    }
    [Theory]
    [InlineData("d8799f581c64f35d26b237ad58e099041bc14c687ea7fdc58969d7d5b66e2540ef9f9f4040ff9f581cc48cbb3d5e57ed56e276bc45f99ab39abe94e6cd7ac39fb402da47ad480014df105553444dffff1b000000c1972014fa183c183cd8799fd8799f581c0bc4df2c05da7920fe0825b68f83fd96d84f215da6ef360f7057ad83ffff001a402fc340ff")]
    public void SundaeSwapPoolDatumRoundTripTest(string originalHex)
    {
        // Convert hex to bytes
        byte[] originalBytes = Convert.FromHexString(originalHex);

        // Deserialize the CBOR data
        SundaeSwapPoolDatum poolDatum = CborSerializer.Deserialize<SundaeSwapPoolDatum>(originalBytes);
        IPlutusData plutusDataDatum = CborSerializer.Deserialize<IPlutusData>(originalBytes);

        // Re-serialize
        byte[] reserializedBytes = CborSerializer.Serialize(poolDatum);
        string reserializedHex = Convert.ToHexString(reserializedBytes).ToUpperInvariant();
        string plutusDataHex = Convert.ToHexString(CborSerializer.Serialize(plutusDataDatum)).ToUpperInvariant();

        // They should match
        ArgumentNullException.ThrowIfNull(originalHex);
        Assert.Equal(originalHex.ToUpperInvariant(), reserializedHex);
        Assert.Equal(originalHex.ToUpperInvariant(), plutusDataHex);
    }
}
