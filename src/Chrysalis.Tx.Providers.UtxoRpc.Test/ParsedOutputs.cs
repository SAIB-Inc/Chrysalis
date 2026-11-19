using System.Numerics;
using Google.Protobuf.Collections;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Xunit;
using Spec = Utxorpc.V1Beta.Cardano;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Compares independently parsed output fields with the decoded ledger output.</summary>
internal static class ParsedOutputs
{
    internal static void Equal(Spec.TxOutput expected, ITransactionOutput actual)
    {
        Assert.True(expected.Address.Span.SequenceEqual(actual.Address().Span));
        Assert.Equal(Unsigned(expected.Coin), actual.Amount().Lovelace());
        Dictionary<string, TokenBundleOutput> policies = actual.Amount().MultiAsset();
        Assert.Equal(expected.Assets.Count, policies.Count);
        foreach (Spec.Multiasset group in expected.Assets)
        {
            Dictionary<ReadOnlyMemory<byte>, ulong> tokens = policies[Convert.ToHexString(group.PolicyId.Span)].Value;
            Assert.Equal(group.Assets.Count, tokens.Count);
            foreach (Spec.Asset asset in group.Assets)
            {
                Assert.Equal(Unsigned(asset.Quantity), actual.Amount().QuantityOf(group.PolicyId.Memory, asset.Name.Memory));
            }
        }

        IDatumOption? option = actual.DatumOption();
        ReadOnlyMemory<byte>? hash = actual is AlonzoTransactionOutput alonzo
            ? alonzo.DatumHash : option is DatumHashOption byHash ? byHash.DatumHash : (ReadOnlyMemory<byte>?)null;
        ReadOnlyMemory<byte>? inline = option is InlineDatumOption byData ? byData.Data.Value : (ReadOnlyMemory<byte>?)null;
        Spec.Datum? datum = expected.Datum;
        if (datum is not null && (!datum.OriginalCbor.IsEmpty || datum.Payload is not null))
        {
            ReadOnlyMemory<byte> bytes = Assert.IsType<ReadOnlyMemory<byte>>(inline);
            Assert.True(datum.OriginalCbor.Span.SequenceEqual(bytes.Span));
            Assert.True(actual.InlineDatumRaw() is { } raw && raw.Span.SequenceEqual(bytes.Span));
            Assert.Null(hash);
        }
        else if (datum is not null && !datum.Hash.IsEmpty)
        {
            Assert.True(datum.Hash.Span.SequenceEqual(Assert.IsType<ReadOnlyMemory<byte>>(hash).Span));
            Assert.Null(inline);
        }
        else
        {
            Assert.Null(hash);
            Assert.Null(inline);
        }

        ReadOnlyMemory<byte>? script = actual.ScriptRef();
        if (expected.Script is null || expected.Script.ScriptCase == Spec.Script.ScriptOneofCase.None)
        {
            Assert.Null(script);
            return;
        }

        IScript decoded = CborSerializer.Deserialize<IScript>(Assert.IsType<ReadOnlyMemory<byte>>(script));
        switch (expected.Script.ScriptCase)
        {
            case Spec.Script.ScriptOneofCase.PlutusV1:
                Assert.True(expected.Script.PlutusV1.Span.SequenceEqual(Assert.IsType<PlutusV1Script>(decoded).ScriptBytes.Span)); break;
            case Spec.Script.ScriptOneofCase.PlutusV2:
                Assert.True(expected.Script.PlutusV2.Span.SequenceEqual(Assert.IsType<PlutusV2Script>(decoded).ScriptBytes.Span)); break;
            case Spec.Script.ScriptOneofCase.PlutusV3:
                Assert.True(expected.Script.PlutusV3.Span.SequenceEqual(Assert.IsType<PlutusV3Script>(decoded).ScriptBytes.Span)); break;
            case Spec.Script.ScriptOneofCase.Native:
                NativeEqual(expected.Script.Native, Assert.IsType<MultiSigScript>(decoded).NativeScript); break;
            case Spec.Script.ScriptOneofCase.None:
            case Spec.Script.ScriptOneofCase.PlutusV4:
            default: Assert.Fail("Unsupported script in golden vector."); break;
        }
    }

    private static ulong Unsigned(Spec.BigInt value)
    {
        Assert.True(value.BigIntCase is Spec.BigInt.BigIntOneofCase.Int or Spec.BigInt.BigIntOneofCase.BigUInt);
        return value.BigIntCase == Spec.BigInt.BigIntOneofCase.Int
            ? checked((ulong)value.Int) : (ulong)new BigInteger(value.BigUInt.Span, isUnsigned: true, isBigEndian: true);
    }

    private static void NativeEqual(Spec.NativeScript expected, INativeScript actual)
    {
        switch (expected.NativeScriptCase)
        {
            case Spec.NativeScript.NativeScriptOneofCase.ScriptPubkeyHash:
                Assert.True(expected.ScriptPubkeyHash.Span.SequenceEqual(Assert.IsType<ScriptPubKey>(actual).KeyHash.Span)); break;
            case Spec.NativeScript.NativeScriptOneofCase.InvalidBefore:
                Assert.Equal(expected.InvalidBefore, Assert.IsType<InvalidBefore>(actual).Slot); break;
            case Spec.NativeScript.NativeScriptOneofCase.InvalidHereafter:
                Assert.Equal(expected.InvalidHereafter, Assert.IsType<InvalidHereafter>(actual).Slot); break;
            case Spec.NativeScript.NativeScriptOneofCase.ScriptAll:
                NativeListEqual(expected.ScriptAll.Items, [.. Assert.IsType<ScriptAll>(actual).Scripts]); break;
            case Spec.NativeScript.NativeScriptOneofCase.ScriptAny:
                NativeListEqual(expected.ScriptAny.Items, [.. Assert.IsType<ScriptAny>(actual).Scripts]); break;
            case Spec.NativeScript.NativeScriptOneofCase.ScriptNOfK:
                ScriptNOfK k = Assert.IsType<ScriptNOfK>(actual);
                Assert.Equal((int)expected.ScriptNOfK.K, k.N);
                NativeListEqual(expected.ScriptNOfK.Scripts, [.. k.Scripts]); break;
            case Spec.NativeScript.NativeScriptOneofCase.None:
            default: Assert.Fail("Unsupported native script."); break;
        }
    }

    private static void NativeListEqual(RepeatedField<Spec.NativeScript> expected, List<INativeScript> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++) { NativeEqual(expected[i], actual[i]); }
    }
}
