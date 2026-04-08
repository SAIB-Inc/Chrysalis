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
    [CborOrder(0)] CborIndefList<MultisigScript> Scripts
) : MultisigScript;

[CborSerializable]
[CborConstr(2)]
public partial record AnyOf(
    [CborOrder(0)] CborIndefList<MultisigScript> Scripts
) : MultisigScript;

[CborSerializable]
[CborConstr(3)]
public partial record AtLeast(
    [CborOrder(0)] ulong Required,
    [CborOrder(1)] CborIndefList<MultisigScript> Scripts
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

public class PlutusConstrTests
{
    [Fact]
    public void PlutusConstr_Create_Empty_NoDoubleNesting()
    {
        // Constr(0, []) should produce Tag(121) + empty array
        // D879 = Tag(121), 80 = 0-element definite array
        PlutusConstr constr = PlutusConstr.Create(0, CborDefList<IPlutusData>.Create([]));
        string hex = Convert.ToHexString(constr.Raw.ToArray());
        Assert.Equal("D87980", hex);
    }

    [Fact]
    public void PlutusConstr_Create_WithFields_NoDoubleNesting()
    {
        // Constr(0, [42]) should produce Tag(121) + 1-element array + int 42
        // D879 = Tag(121), 81 = 1-element array, 182A = int 42
        PlutusConstr constr = PlutusConstr.Create(0, CborDefList<IPlutusData>.Create([PlutusInt64.Create(42)]));
        string hex = Convert.ToHexString(constr.Raw.ToArray());
        Assert.Equal("D87981182A", hex);
    }

    [Fact]
    public void PlutusConstr_Create_HighTag_NoDoubleNesting()
    {
        // Constr(7, [42]) should produce Tag(1280) + 1-element array + int 42
        // D90500 = Tag(1280), 81 = 1-element array, 182A = int 42
        PlutusConstr constr = PlutusConstr.Create(7, CborDefList<IPlutusData>.Create([PlutusInt64.Create(42)]));
        string hex = Convert.ToHexString(constr.Raw.ToArray());
        Assert.Equal("D9050081182A", hex);
    }

    [Fact]
    public void PlutusConstr_Create_RoundTrip()
    {
        // Create, then read back and verify fields
        PlutusConstr constr = PlutusConstr.Create(0, CborDefList<IPlutusData>.Create([
            PlutusInt64.Create(1),
            PlutusInt64.Create(2),
            PlutusBoundedBytes.Create(new byte[] { 0xCA, 0xFE })
        ]));

        // Read back
        PlutusConstr readBack = PlutusConstr.Read(constr.Raw);
        CborDefList<IPlutusData> fields = Assert.IsType<CborDefList<IPlutusData>>(readBack.Fields);
        Assert.Equal(3, fields.Value.Count);
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
