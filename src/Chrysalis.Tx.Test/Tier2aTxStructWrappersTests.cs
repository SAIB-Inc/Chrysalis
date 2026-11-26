using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Plutus.Types;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Xunit;

namespace Chrysalis.Tx.Test;

public class Tier2aTxStructWrappersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("82024101")]
    [InlineData("8200820180")]
    public void TxBuilderResolvesReferenceAndInlineScripts(string? hex)
    {
        IScript? script = string.IsNullOrEmpty(hex) ? null : CborSerializer.Deserialize<IScript>(Convert.FromHexString(hex));
        byte[] hash = script?.Hash() ?? new byte[28];
        string hashHex = Convert.ToHexString(hash);
        ResolvedInput reference = Input(1, hash, Encoded(hex));
        ResolvedInput spend = Input(0, hash, null);
        TxBuilder builder = new TxBuilder(new CliSnapshotProvider(ProtocolParams.Read(new byte[] { 0x80 }), [])).AddReferenceInput(reference);
        Func<string, IScript?> findRef = typeof(TxBuilder).GetMethod("ResolveScriptFromReferenceInputs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, IScript?>>(builder);
        Func<ResolvedInput, (IScript Script, bool IsReference)> findSpend = typeof(TxBuilder).GetMethod("ResolveScriptForInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<ResolvedInput, (IScript Script, bool IsReference)>>(builder);
        if (hex is null)
        {
            Assert.Null(findRef(hashHex));
            _ = Assert.Throws<InvalidOperationException>(() => findSpend(spend));
        }
        else if (hex.Length == 0)
        {
            _ = Assert.Throws<CborDecodeException>(() => findRef(hashHex));
            _ = Assert.Throws<CborDecodeException>(() => findSpend(spend));
        }
        else
        {
            Assert.Equal(hash, findRef(hashHex)?.Hash());
            (IScript resolved, bool isReference) = findSpend(spend);
            Assert.True(isReference);
            Assert.Equal(hash, resolved.Hash());
        }

        TxBuilder inlineBuilder = new(new CliSnapshotProvider(ProtocolParams.Read(new byte[] { 0x80 }), []));
        findSpend = typeof(TxBuilder).GetMethod("ResolveScriptForInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<ResolvedInput, (IScript Script, bool IsReference)>>(inlineBuilder);
        if (script is not null)
        {
            (IScript resolved, bool isReference) = findSpend(reference);
            Assert.False(isReference);
            Assert.Equal(hash, resolved.Hash());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("82024101")]
    [InlineData("8200820180")]
    public void ScriptContextAndLookupDistinguishAbsentEmptyAndValid(string? hex)
    {
        CborEncodedValue? encoded = Encoded(hex);
        IScript? script = string.IsNullOrEmpty(hex) ? null : CborSerializer.Deserialize<IScript>(Convert.FromHexString(hex));
        byte[] hash = script?.Hash() ?? new byte[28];
        Func<CborEncodedValue?, PlutusDataConstr> toData = typeof(ScriptContextBuilder)
            .GetMethod("ScriptRefToPlutusData", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Func<CborEncodedValue?, PlutusDataConstr>>();
        PlutusDataConstr data = toData(encoded);
        Assert.Equal(script is null ? 1 : 0, (int)data.Tag);
        if (script is null) { Assert.Empty(data.Fields); }
        else { Assert.Equal(hash, Assert.IsType<PlutusDataByteString>(Assert.Single(data.Fields)).Value.ToArray()); }

        if (script is MultiSigScript) { return; } // Lookup's native-script behavior is out of scope.
        PostAlonzoTransactionWitnessSet witnesses = PostAlonzoTransactionWitnessSet.Read(new byte[] { 0xA0 });
        DataLookupTable lookup = DataLookupTable.FromTransaction(witnesses, [Input(0, hash, encoded)]);
        if (script is null) { Assert.Null(lookup.GetScript(hash)); }
        else
        {
            Assert.Equal(2, lookup.GetScript(hash)?.Version);
            Assert.Equal(new byte[] { 0x01 }, lookup.GetScript(hash)?.ScriptBytes);
        }
    }

    private static CborEncodedValue? Encoded(string? hex) => hex switch
    {
        null => null,
        "" => default(CborEncodedValue),
        _ => new CborEncodedValue(Convert.FromHexString(hex))
    };

    private static ResolvedInput Input(ulong index, byte[] hash, CborEncodedValue? script) => new(
        TransactionInput.Create(new byte[32], index),
        PostAlonzoTransactionOutput.Create(new Address((byte[])[0x70, .. hash]), Lovelace.Create(5), scriptRef: script));
}
