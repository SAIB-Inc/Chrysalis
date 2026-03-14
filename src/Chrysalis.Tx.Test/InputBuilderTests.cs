using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Xunit;

namespace Chrysalis.Tx.Test;

public class InputBuilderTests
{
    private static readonly byte[] FakeAddress = new byte[57];

    private static TransactionInput MakeInput(byte hashByte, ulong index)
    {
        byte[] hash = new byte[32];
        hash[0] = hashByte;
        return TransactionInput.Create(hash, index);
    }

    private static AlonzoTransactionOutput MakeOutput(ulong lovelace) =>
        AlonzoTransactionOutput.Create(
            Address.Create(FakeAddress),
            Lovelace.Create(lovelace),
            null);

    [Fact]
    public void PaymentKeyInputHasNoWitnessRequirements()
    {
        InputBuilderResult result = new InputBuilder(MakeInput(0, 0), MakeOutput(5_000_000))
            .PaymentKey();

        Assert.Empty(result.Requirements.ScriptHashes);
        Assert.Empty(result.Requirements.ScriptWitnesses);
        Assert.Empty(result.Requirements.Datums);
        Assert.Null(result.Requirements.RedeemerData);
        Assert.False(result.Requirements.HasPlutusScripts);
    }

    [Fact]
    public void PlutusScriptRefInputTracksWitnessRequirements()
    {
        string scriptHash = "AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD";
        IPlutusData redeemer = PlutusInt.Create(0);
        IPlutusData datum = PlutusInt.Create(1);

        InputBuilderResult result = new InputBuilder(MakeInput(1, 0), MakeOutput(10_000_000))
            .PlutusScriptRef(scriptHash, redeemer, datum, "11223344");

        Assert.Single(result.Requirements.ScriptRefHashes);
        Assert.Single(result.Requirements.Datums);
        Assert.NotNull(result.Requirements.RedeemerData);
        Assert.Single(result.Requirements.RequiredSigners);
        Assert.True(result.Requirements.HasPlutusScripts);
    }

    [Fact]
    public void PlutusScriptRefInputTracksRefHash()
    {
        string scriptHash = "AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD";

        IPlutusData redeemer = PlutusInt.Create(0);

        InputBuilderResult result = new InputBuilder(MakeInput(2, 0), MakeOutput(5_000_000))
            .PlutusScriptRef(scriptHash, redeemer);

        Assert.Empty(result.Requirements.ScriptHashes);
        Assert.Single(result.Requirements.ScriptRefHashes);
        Assert.NotNull(result.Requirements.RedeemerData);
    }
}
