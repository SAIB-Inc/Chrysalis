using System.Collections;
using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Xunit;

namespace Chrysalis.Tx.Test;

/// <summary>Preserves the transaction callers' behavior across the switch to typed codec errors.</summary>
public class CodecErrorCompatibilityTests
{
    /// <summary>Policy lookup does not turn an invalid multi-asset value into a missing policy.</summary>
    [Fact]
    public void InvalidMultiAssetIsNotAMissingPolicy()
    {
        MultiAssetOutput assets = MultiAssetOutput.Read(Convert.FromHexString("A141AA01"));
        Assert.Null(assets.TokenBundleByPolicyId("not hex"));
        _ = Assert.ThrowsAny<CborDecodeException>(() => assets.TokenBundleByPolicyId("AA"));
    }

    /// <summary>A payload the CBOR codec can write but Plutus cannot read is still skipped.</summary>
    [Fact]
    public void TemplateBuilderSkipsANonPlutusRedeemer()
    {
        NonPlutusPayload payload = new(new byte[] { 0x1C });
        _ = Assert.ThrowsAny<CborDecodeException>(() => CborSerializer.Deserialize<IPlutusData>(CborSerializer.Serialize(payload)));
        Type builder = typeof(TransactionTemplateBuilder<int>);
        Type contextType = builder.GetNestedType("BuildContext", BindingFlags.NonPublic)!.MakeGenericType(typeof(int));
        object context = Activator.CreateInstance(contextType, nonPublic: true)!;
        MethodInfo process = builder.GetMethod("ProcessRedeemer", BindingFlags.Static | BindingFlags.NonPublic)!;
        _ = process.Invoke(null, [context, new Redeemer<NonPlutusPayload>(RedeemerTag.Spend, 0, payload, ExUnits.Create(0, 0)), 0UL]);
        IDictionary redeemers = Assert.IsAssignableFrom<IDictionary>(contextType.GetProperty("Redeemers")!.GetValue(context));
        Assert.Empty(redeemers);
    }

}

/// <summary>A preserved encoded payload that cannot be decoded as Plutus data.</summary>
/// <param name="Raw">The original bytes to serialize, without invoking a writer.</param>
public sealed record NonPlutusPayload(ReadOnlyMemory<byte> Raw) : ICborType
{
    /// <inheritdoc />
    public int ConstrIndex => 0;

    /// <inheritdoc />
    public bool IsIndefinite => false;
}
