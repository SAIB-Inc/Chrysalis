using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Models.Cbor;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Checks the configured server's live reads without any signing key.</summary>
[Trait("Category", "Integration")]
[Collection("Live")]
public sealed class LiveReadTests
{
    /// <summary>The preprod protocol version and all three cost models are present.</summary>
    /// <returns>A task for the parameter read.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network)]
    public async Task ReadsCurrentParameters()
    {
        using UtxoRpc provider = LiveSettings.CreateProvider();
        ProtocolParams parameters = await provider.GetParametersAsync();
        Assert.True(parameters.ProtocolVersion?.Major >= 10);
        Assert.NotEmpty(parameters.CostModelsForScriptLanguage!.PlutusV1!.Value);
        Assert.NotEmpty(parameters.CostModelsForScriptLanguage.PlutusV2!.Value);
        Assert.NotEmpty(parameters.CostModelsForScriptLanguage.PlutusV3!.Value);
    }

    /// <summary>The first three wallet outputs can be looked up byte for byte.</summary>
    /// <returns>A task for address and reference reads.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network, LiveSettings.Addresses)]
    public async Task FindsWalletOutputsAgainByReference()
    {
        using UtxoRpc provider = LiveSettings.CreateProvider();
        await RequireSnapshotReferencesAsync(provider, LiveSettings.WalletAddresses());
    }

    internal static async Task RequireSnapshotReferencesAsync(UtxoRpc provider, List<string> addresses)
    {
        // Calls do not share a tip: refresh only a disappeared reference, never a byte mismatch.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            List<ResolvedInput> outputs = await provider.GetUtxosAsync(addresses);
            Assert.NotEmpty(outputs);
            bool disappeared = false;
            for (int i = 0; i < Math.Min(3, outputs.Count); i++)
            {
                ResolvedInput expected = outputs[i];
                ResolvedInput? actual = await provider.GetUtxoByOutRefAsync(
                    Convert.ToHexStringLower(expected.Outref.TransactionId.Span), expected.Outref.Index);
                if (actual is null) { disappeared = true; break; }
                Assert.Equal(CborSerializer.Serialize(expected.Output), CborSerializer.Serialize(actual.Output));
            }
            if (!disappeared) { return; }
        }
        Assert.Fail("An output disappeared before lookup in three consecutive snapshots.");
    }

    /// <summary>Dolos meets the absence rule: unknown references and transactions return null.</summary>
    /// <returns>A task for two unknown-object probes.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network)]
    public async Task UnknownObjectsAreAbsent()
    {
        // Server-dependent: ReadUtxos must return a subset or NOT_FOUND, ReadTx NOT_FOUND (plan 3.5).
        using UtxoRpc provider = LiveSettings.CreateProvider();
        string unknown = new('0', 64);
        Assert.Null(await provider.GetUtxoByOutRefAsync(unknown, 0));
        Assert.Null(await provider.GetTransactionMetadataAsync(unknown));
    }

    /// <summary>A cancelled live read retains cancellation rather than becoming a server error.</summary>
    /// <returns>A task for the cancelled call.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network)]
    public async Task HonoursCancellation()
    {
        using UtxoRpc provider = LiveSettings.CreateProvider();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParametersAsync(cancellation.Token));
    }
}
