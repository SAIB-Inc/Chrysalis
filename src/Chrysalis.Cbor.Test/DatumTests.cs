using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Test;

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
) : CborBase;

/// <summary>
/// MultisigScript for SundaeSwap testing
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record MultisigScript : CborBase;

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
    Option<MultisigScript> FeeManager,  // Optional fee manager script

    [CborOrder(6)]
    ulong MarketOpen,  // UNIX timestamp when trading is allowed (0 = open)
    [CborOrder(7)]
    ulong ProtocolFees  // ADA set aside for protocol fees
) : CborBase;

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
        Assert.Equal("64f35d26b237ad58e099041bc14c687ea7fdc58969d7d5b66e2540ef", Convert.ToHexString(poolDatum.Identifier).ToLowerInvariant());
        
        // Verify assets
        Assert.Equal(2, poolDatum.Assets.Count);
        
        // First asset should be ADA (empty policy and name)
        AssetClass asset1 = poolDatum.Assets[0];
        Assert.Empty(asset1.PolicyId);
        Assert.Empty(asset1.AssetName);
        
        // Second asset should be SDAM token
        AssetClass asset2 = poolDatum.Assets[1];
        Assert.Equal("c48cbb3d5e57ed56e276bc45f99ab39abe94e6cd7ac39fb402da47ad", Convert.ToHexString(asset2.PolicyId).ToLowerInvariant());
        Assert.Equal("0014df105553444d", Convert.ToHexString(asset2.AssetName).ToLowerInvariant());
        // Verify other pool parameters
        Assert.Equal(831464150266UL, poolDatum.CirculatingLp);
        Assert.Equal(60UL, poolDatum.BidFeesPerTenThousand); // 0.6%
        Assert.Equal(60UL, poolDatum.AskFeesPerTenThousand); // 0.6%
        Assert.Equal(0UL, poolDatum.MarketOpen); // Market is open
        Assert.Equal(1076872000UL, poolDatum.ProtocolFees);
        // Verify fee manager
        Assert.IsType<Some<MultisigScript>>(poolDatum.FeeManager);
        Some<MultisigScript> feeManager = (poolDatum.FeeManager as Some<MultisigScript>)!;
        Assert.NotNull(feeManager);
        Assert.IsType<Signature>(feeManager.Value);
        Signature signature = (feeManager.Value as Signature)!;
        Assert.Equal("0bc4df2c05da7920fe0825b68f83fd96d84f215da6ef360f7057ad83", Convert.ToHexString(signature.KeyHash).ToLowerInvariant());
    }
    [Theory]
    [InlineData("d8799f581c64f35d26b237ad58e099041bc14c687ea7fdc58969d7d5b66e2540ef9f9f4040ff9f581cc48cbb3d5e57ed56e276bc45f99ab39abe94e6cd7ac39fb402da47ad480014df105553444dffff1b000000c1972014fa183c183cd8799fd8799f581c0bc4df2c05da7920fe0825b68f83fd96d84f215da6ef360f7057ad83ffff001a402fc340ff")]
    public void SundaeSwapPoolDatumRoundTripTest(string originalHex)
    {
        // Convert hex to bytes
        byte[] originalBytes = Convert.FromHexString(originalHex);

        // Deserialize the CBOR data
        SundaeSwapPoolDatum poolDatum = CborSerializer.Deserialize<SundaeSwapPoolDatum>(originalBytes);
        PlutusData plutusDataDatum = CborSerializer.Deserialize<PlutusData>(originalBytes);

        // Re-serialize
        byte[] reserializedBytes = CborSerializer.Serialize(poolDatum);
        string reserializedHex = Convert.ToHexString(reserializedBytes).ToLowerInvariant();
        string plutusDataHex = Convert.ToHexString(CborSerializer.Serialize(plutusDataDatum)).ToLowerInvariant();

        // They should match
        Assert.Equal(originalHex.ToLowerInvariant(), reserializedHex);
        Assert.Equal(originalHex.ToLowerInvariant(), plutusDataHex);
    }
}