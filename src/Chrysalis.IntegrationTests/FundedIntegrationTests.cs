using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.IntegrationTests;

/// <summary>Explicitly opted-in, fee-capped single self-transfer; never creates or exports a key.</summary>
public sealed class FundedIntegrationTests
{
    internal sealed record SelfTransfer(string Address, byte[] AddressBytes, JsonElement ParametersJson, ResolvedInput Selected, PostMaryTransaction Unsigned, ITransaction Signed, byte[] SignedBytes, string TxId, ulong Fee, int Index);

    /// <summary>Builds/signs/submits one small self-transfer and checks public CLI hash, fee and inclusion.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.WalletFile, Env.FundedOptIn, Env.CollateralTxIn])]
    [Trait("Category", "Integration")]
    public async Task FundedSelfTransferMatchesCliAndIsConfirmed()
    {
        Env.RequirePreprodOrDevnet();
        Env.RequireFundedOptIn();
        TransactionInput collateral = FundingSelection.ParseInputReference(Env.Required(Env.CollateralTxIn));
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        // A configured public address lets bad/stale collateral fail before reservation or key access.
        // Without it the wallet address must first be derived from the key; CreatePlan still checks
        // the fresh, unfiltered wallet snapshot before selecting any spending input.
        if (Environment.GetEnvironmentVariable(Env.WalletAddress) is string publicAddress && !string.IsNullOrWhiteSpace(publicAddress))
        {
            await OracleCli.RecordVersionAsync(deadline.Token);
            JsonElement publicUtxos = await OracleCli.QueryJsonAsync("funded-public-collateral-preflight", deadline.Token, "utxo", "--address", publicAddress, "--output-json");
            FundingSelection.RequireCollateralPresent(publicUtxos, collateral);
        }
        await using FundedReservation reservation = await FundedReservation.CreateAsync(Env.OutputDirectory, deadline.Token);
        await OracleCli.RecordVersionAsync(deadline.Token);
        await WithWalletKeyAsync((key, ct) => ExecuteSelfTransferAsync(key, collateral, reservation, ct), deadline.Token);
    }

    internal static async Task WithWalletKeyAsync(Func<PrivateKey, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        byte[]? privateBytes = null;
        byte[]? chaincode = null;
        try
        {
            // Only this test process opens the configured original; no copy or key CLI argument.
            using JsonDocument secret = JsonDocument.Parse(await File.ReadAllTextAsync(Env.Required(Env.WalletFile), cancellationToken));
            privateBytes = Convert.FromHexString(secret.RootElement.GetProperty("Key").GetString()!);
            chaincode = Convert.FromHexString(secret.RootElement.GetProperty("Chaincode").GetString()!);
            PrivateKey key = new(privateBytes, chaincode);
            await operation(key, cancellationToken);
        }
        catch (Exception exception)
        {
            // A key parse/sign failure must not expose its inputs through an assertion/inner exception.
            throw new InvalidOperationException("Funded integration failed; private diagnostics suppressed. Public artifacts and the spend receipt identify any attempted transaction. Error type: " + exception.GetType().Name);
        }
        finally
        {
            if (privateBytes is not null) { CryptographicOperations.ZeroMemory(privateBytes); }
            if (chaincode is not null) { CryptographicOperations.ZeroMemory(chaincode); }
        }
    }

    internal static async Task<SelfTransfer> BuildSelfTransferAsync(PrivateKey key, TransactionInput collateral, ulong transfer, Action<TxBuilder, string>? addOutputs, ICardanoDataProvider? provider, CancellationToken cancellationToken)
    {
        const ulong maximumFee = 500_000;
        string address = Environment.GetEnvironmentVariable(Env.WalletAddress) ?? WalletAddress.FromPublicKeys(NetworkType.Preprod, AddressType.EnterprisePayment, key.GetPublicKey()).ToBech32();
        byte[] addressBytes = WalletAddress.FromBech32(address).ToBytes();
        Assert.Equal(0, addressBytes[0] & 15);
        Assert.True(addressBytes.AsSpan(1, 28).SequenceEqual(key.GetPublicKey().ToBlake2b224()), "The wallet key does not own the address.");
        JsonElement parametersJson = await OracleCli.QueryJsonAsync("funded-parameters", cancellationToken, "protocol-parameters");
        ProtocolParams parameters = ProtocolOracles.Parameters(parametersJson);
        JsonElement utxos = await OracleCli.QueryJsonAsync("funded-owned-utxos", cancellationToken, "utxo", "--address", address, "--output-json");
        List<ResolvedInput> inputs = ReadAdaInputs(utxos);
        FundingPlan plan = FundingSelection.CreatePlan(inputs, collateral, utxos);
        ResolvedInput selected = plan.Selected;
        TxBuilder builder = new(provider ?? new CliSnapshotProvider(parameters, plan.ProviderInputs));
        _ = builder.AddInput(selected).PayLovelace(address, transfer).SetChangeAddress(address);
        addOutputs?.Invoke(builder, address);
        PostMaryTransaction unsigned = await builder.Complete().WaitAsync(cancellationToken);
        ITransaction signed = unsigned.Sign(key);
        PostMaryTransaction transaction = Assert.IsType<PostMaryTransaction>(signed);
        ITransactionBody body = transaction.Body;
        ulong fee = body.Fee();
        Assert.InRange(fee, 1UL, maximumFee);
        byte[] signedBytes = CborSerializer.Serialize(signed);
        string txid = body.Hash();
        ITransactionOutput[] outputs = [.. body.Outputs()];
        Assert.All(outputs, output => Assert.True(output.Address().ToArray().AsSpan().SequenceEqual(addressBytes), "An output pays another address."));
        int index = Array.FindIndex(outputs, output => output.Amount().Lovelace() == transfer);
        Assert.True(index >= 0);
        FundingSelection.AssertExactlySelected(body.Inputs(), [selected.Outref]);
        return new SelfTransfer(address, addressBytes, parametersJson, selected, unsigned, signed, signedBytes, txid, fee, index);
    }

    internal static async Task WaitForOutputAsync(string outref, ulong transfer, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            JsonElement confirmed = await OracleCli.QueryJsonAsync("funded-confirmation", cancellationToken, "utxo", "--tx-in", outref, "--output-json");
            if (confirmed.EnumerateObject().Any())
            {
                JsonProperty found = Assert.Single(confirmed.EnumerateObject());
                Assert.Equal(transfer, found.Value.GetProperty("value").GetProperty("lovelace").GetUInt64());
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        Assert.Fail("The accepted self-transfer was not observed in the same node's UTxO within the bounded wait.");
    }

    private static async Task ExecuteSelfTransferAsync(PrivateKey key, TransactionInput collateral, FundedReservation reservation, CancellationToken cancellationToken)
    {
        const ulong transfer = 2_000_000;
        SelfTransfer built = await BuildSelfTransferAsync(key, collateral, transfer, null, null, cancellationToken);
        string directory = Env.OutputDirectory;
        string unsignedFile = Path.Combine(directory, "funded-unsigned.json");
        string signedFile = Path.Combine(directory, "funded-signed.json");
        string parameterFile = Path.Combine(directory, "funded-cli-parameters.json");
        await File.WriteAllTextAsync(unsignedFile, JsonSerializer.Serialize(new { type = "Tx ConwayEra", description = string.Empty, cborHex = Convert.ToHexStringLower(CborSerializer.Serialize(built.Unsigned)) }), cancellationToken);
        await File.WriteAllTextAsync(signedFile, JsonSerializer.Serialize(new { type = "Tx ConwayEra", description = string.Empty, cborHex = Convert.ToHexStringLower(built.SignedBytes) }), cancellationToken);
        await File.WriteAllTextAsync(parameterFile, built.ParametersJson.GetRawText(), cancellationToken);
        string cliId = await OracleCli.ExecuteAsync("funded-cli-txid", ["latest", "transaction", "txid", "--tx-file", signedFile, "--output-text"], cancellationToken);
        Assert.Equal(Convert.FromHexString(cliId), Convert.FromHexString(built.TxId));
        string cliFee = await OracleCli.ExecuteAsync("funded-cli-minimum-fee", ["latest", "transaction", "calculate-min-fee", "--tx-body-file", unsignedFile, "--protocol-params-file", parameterFile, "--witness-count", "1", "--output-text"], cancellationToken);
        ulong minimum = ulong.Parse(cliFee.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], CultureInfo.InvariantCulture);
        Assert.True(built.Fee >= minimum, "The signed fee must cover the independent CLI minimum; exact fee arithmetic has a separate correctness gate.");
        string view = await OracleCli.ExecuteAsync("funded-cli-view", ["debug", "transaction", "view", "--tx-file", signedFile, "--output-json"], cancellationToken);
        using JsonDocument viewJson = JsonDocument.Parse(view);
        Assert.Equal(built.Fee.ToString(CultureInfo.InvariantCulture) + " Lovelace", viewJson.RootElement.GetProperty("fee").GetString());
        // Update the already-held exclusive public reservation; a timeout never permits a replay.
        await reservation.RecordAsync(new { txid = built.TxId, transfer_lovelace = transfer, fee_lovelace = built.Fee, status = "reserved-before-submit" }, cancellationToken);
        string spendLine = $"| {DateTimeOffset.UtcNow:O} | {built.TxId} | {transfer} | {built.Fee} | reserved before submit; self-transfer, net spend is fee |\n";
        await File.AppendAllTextAsync(Path.Combine(directory, "spend-log.md"), spendLine, cancellationToken);
        using NodeClient node = await NodeConnections.OpenAsync(cancellationToken);
        FundingSelection.AssertExactlySelected(Assert.IsType<PostMaryTransaction>(built.Signed).Body.Inputs(), [built.Selected.Outref]);
        _ = Assert.IsType<AcceptTx>(await node.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, new EraTx((int)Era.Conway, new Codec.Types.CborEncodedValue(built.SignedBytes))), cancellationToken));
        string outref = built.TxId + "#" + built.Index.ToString(CultureInfo.InvariantCulture);
        await WaitForOutputAsync(outref, transfer, cancellationToken);
        await reservation.RecordAsync(new { txid = built.TxId, transfer_lovelace = transfer, fee_lovelace = built.Fee, minimum_fee_lovelace = minimum, status = "confirmed" }, cancellationToken);
        await File.AppendAllTextAsync(Path.Combine(directory, "spend-log.md"), $"\nConfirmed {built.TxId}; transfer returned to the wallet; fee-only spend {built.Fee} lovelace.\n", cancellationToken);
    }

    internal static List<ResolvedInput> ReadAdaInputs(JsonElement utxos)
    {
        List<ResolvedInput> inputs = [];
        foreach (JsonProperty property in utxos.EnumerateObject())
        {
            JsonElement value = property.Value.GetProperty("value");
            if (value.EnumerateObject().Count() != 1 || !value.TryGetProperty("lovelace", out JsonElement amount)) { continue; }
            string[] reference = property.Name.Split('#');
            TransactionInput input = TransactionInput.Create(Convert.FromHexString(reference[0]), ulong.Parse(reference[1], CultureInfo.InvariantCulture));
            PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Create(new Address(WalletAddress.FromBech32(property.Value.GetProperty("address").GetString()!).ToBytes()), Lovelace.Create(amount.GetUInt64()));
            inputs.Add(new ResolvedInput(input, output));
        }
        return inputs;
    }
}
