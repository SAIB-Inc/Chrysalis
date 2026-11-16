using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalTxSubmit;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Xunit;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.IntegrationTests;

/// <summary>Explicitly opted-in, fee-capped single self-transfer; never creates or exports a key.</summary>
public sealed class FundedIntegrationTests
{
    /// <summary>Builds/signs/submits one small self-transfer and checks public CLI hash, fee and inclusion.</summary>
    [IntegrationFact(Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.WalletFile, Env.FundedOptIn, Env.CollateralTxIn)]
    [Trait("Category", "Integration")]
    public async Task FundedSelfTransferMatchesCliAndIsConfirmed()
    {
        Env.RequirePreprod();
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
        byte[]? privateBytes = null;
        byte[]? chaincode = null;
        try
        {
            // Only this test process opens the configured original; no copy or key CLI argument.
            using JsonDocument secret = JsonDocument.Parse(await File.ReadAllTextAsync(Env.Required(Env.WalletFile), deadline.Token));
            privateBytes = Convert.FromHexString(secret.RootElement.GetProperty("Key").GetString()!);
            chaincode = Convert.FromHexString(secret.RootElement.GetProperty("Chaincode").GetString()!);
            PrivateKey key = new(privateBytes, chaincode);
            await ExecuteSelfTransferAsync(key, collateral, reservation, deadline.Token);
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

    private static async Task ExecuteSelfTransferAsync(PrivateKey key, TransactionInput collateral, FundedReservation reservation, CancellationToken cancellationToken)
    {
        const ulong transfer = 2_000_000;
        const ulong maximumFee = 500_000;
        string address = Environment.GetEnvironmentVariable(Env.WalletAddress) ?? WalletAddress.FromPublicKeys(NetworkType.Preprod, AddressType.EnterprisePayment, key.GetPublicKey()).ToBech32();
        byte[] addressBytes = WalletAddress.FromBech32(address).ToBytes();
        Assert.Equal(0, addressBytes[0] & 15);
        Assert.Equal(key.GetPublicKey().ToBlake2b224(), addressBytes.AsSpan(1, 28).ToArray());
        string directory = Env.OutputDirectory;
        JsonElement parametersJson = await OracleCli.QueryJsonAsync("funded-parameters", cancellationToken, "protocol-parameters");
        ProtocolParams parameters = ProtocolOracles.Parameters(parametersJson);
        JsonElement utxos = await OracleCli.QueryJsonAsync("funded-owned-utxos", cancellationToken, "utxo", "--address", address, "--output-json");
        List<ResolvedInput> inputs = ReadAdaInputs(utxos);
        FundingPlan plan = FundingSelection.CreatePlan(inputs, collateral, utxos);
        ResolvedInput selected = plan.Selected;
        TxBuilder builder = new(new CliSnapshotProvider(parameters, plan.ProviderInputs));
        _ = builder.AddInput(selected).PayLovelace(address, transfer).SetChangeAddress(address);
        PostMaryTransaction unsigned = await builder.Complete().WaitAsync(cancellationToken);
        ITransaction signed = unsigned.Sign(key);
        PostMaryTransaction transaction = Assert.IsType<PostMaryTransaction>(signed);
        ITransactionBody body = transaction.Body;
        ulong fee = body.Fee();
        Assert.InRange(fee, 1UL, maximumFee);
        byte[] signedBytes = CborSerializer.Serialize(signed);
        string txid = body.Hash();
        string unsignedFile = Path.Combine(directory, "funded-unsigned.json");
        string signedFile = Path.Combine(directory, "funded-signed.json");
        string parameterFile = Path.Combine(directory, "funded-cli-parameters.json");
        await File.WriteAllTextAsync(unsignedFile, JsonSerializer.Serialize(new { type = "Tx ConwayEra", description = string.Empty, cborHex = Convert.ToHexStringLower(CborSerializer.Serialize(unsigned)) }), cancellationToken);
        await File.WriteAllTextAsync(signedFile, JsonSerializer.Serialize(new { type = "Tx ConwayEra", description = string.Empty, cborHex = Convert.ToHexStringLower(signedBytes) }), cancellationToken);
        await File.WriteAllTextAsync(parameterFile, parametersJson.GetRawText(), cancellationToken);
        string cliId = await OracleCli.ExecuteAsync("funded-cli-txid", ["latest", "transaction", "txid", "--tx-file", signedFile, "--output-text"], cancellationToken);
        Assert.Equal(Convert.FromHexString(cliId), Convert.FromHexString(txid));
        string cliFee = await OracleCli.ExecuteAsync("funded-cli-minimum-fee", ["latest", "transaction", "calculate-min-fee", "--tx-body-file", unsignedFile, "--protocol-params-file", parameterFile, "--witness-count", "1", "--output-text"], cancellationToken);
        ulong minimum = ulong.Parse(cliFee.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], CultureInfo.InvariantCulture);
        Assert.True(fee >= minimum, "The signed fee must cover the independent CLI minimum; exact fee arithmetic has a separate correctness gate.");
        string view = await OracleCli.ExecuteAsync("funded-cli-view", ["debug", "transaction", "view", "--tx-file", signedFile, "--output-json"], cancellationToken);
        using JsonDocument viewJson = JsonDocument.Parse(view);
        Assert.Equal(fee.ToString(CultureInfo.InvariantCulture) + " Lovelace", viewJson.RootElement.GetProperty("fee").GetString());
        ITransactionOutput[] outputs = [.. body.Outputs()];
        Assert.All(outputs, output => Assert.Equal(addressBytes, output.Address().ToArray()));
        int index = Array.FindIndex(outputs, output => output.Amount().Lovelace() == transfer);
        Assert.True(index >= 0);
        JsonElement tip = await OracleCli.QueryJsonAsync("funded-era", cancellationToken, "tip");
        FundingSelection.AssertExactlySelected(body.Inputs(), [selected.Outref]);
        // Update the already-held exclusive public reservation; a timeout never permits a replay.
        await reservation.RecordAsync(new { txid, transfer_lovelace = transfer, fee_lovelace = fee, status = "reserved-before-submit" }, cancellationToken);
        string spendLine = $"| {DateTimeOffset.UtcNow:O} | {txid} | {transfer} | {fee} | reserved before submit; self-transfer, net spend is fee |\n";
        await File.AppendAllTextAsync(Path.Combine(directory, "spend-log.md"), spendLine, cancellationToken);
        using NodeClient node = await NodeConnections.OpenAsync(cancellationToken);
        FundingSelection.AssertExactlySelected(body.Inputs(), [selected.Outref]);
        _ = Assert.IsType<AcceptTx>(await node.LocalTxSubmit.SubmitTxAsync(new SubmitTx(0, new EraTx(checked((int)ProtocolOracles.Era(tip)), new Chrysalis.Codec.Types.CborEncodedValue(signedBytes))), cancellationToken));
        string outref = txid + "#" + index.ToString(CultureInfo.InvariantCulture);
        for (int attempt = 0; attempt < 60; attempt++)
        {
            JsonElement confirmed = await OracleCli.QueryJsonAsync("funded-confirmation", cancellationToken, "utxo", "--tx-in", outref, "--output-json");
            if (confirmed.EnumerateObject().Any())
            {
                JsonProperty found = Assert.Single(confirmed.EnumerateObject());
                Assert.Equal(transfer, found.Value.GetProperty("value").GetProperty("lovelace").GetUInt64());
                await reservation.RecordAsync(new { txid, transfer_lovelace = transfer, fee_lovelace = fee, minimum_fee_lovelace = minimum, status = "confirmed" }, cancellationToken);
                await File.AppendAllTextAsync(Path.Combine(directory, "spend-log.md"), $"\nConfirmed {txid}; transfer returned to the wallet; fee-only spend {fee} lovelace.\n", cancellationToken);
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        Assert.Fail("The accepted self-transfer was not observed in the same node's UTxO within the bounded wait.");
    }

    private static List<ResolvedInput> ReadAdaInputs(JsonElement utxos)
    {
        List<ResolvedInput> inputs = [];
        foreach (JsonProperty property in utxos.EnumerateObject())
        {
            JsonElement value = property.Value.GetProperty("value");
            if (value.EnumerateObject().Count() != 1 || !value.TryGetProperty("lovelace", out JsonElement amount)) { continue; }
            string[] reference = property.Name.Split('#');
            TransactionInput input = TransactionInput.Create(Convert.FromHexString(reference[0]), ulong.Parse(reference[1], CultureInfo.InvariantCulture));
            PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Create(new Chrysalis.Codec.Types.Cardano.Core.Common.Address(WalletAddress.FromBech32(property.Value.GetProperty("address").GetString()!).ToBytes()), Lovelace.Create(amount.GetUInt64()));
            inputs.Add(new ResolvedInput(input, output));
        }
        return inputs;
    }

    private sealed class CliSnapshotProvider(ProtocolParams parameters, List<ResolvedInput> inputs) : ICardanoDataProvider
    {
        public NetworkType NetworkType => NetworkType.Preprod;
        public Task<ProtocolParams> GetParametersAsync() => Task.FromResult(parameters);
        public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return Task.FromResult(inputs.Where(input => address.Contains(WalletAddress.FromBytes(input.Output.Address().ToArray()).ToBech32(), StringComparer.Ordinal)).ToList());
        }
        public Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index) => Task.FromResult(inputs.FirstOrDefault(input => string.Equals(Convert.ToHexStringLower(input.Outref.TransactionId.Span), txHash, StringComparison.OrdinalIgnoreCase) && input.Outref.Index == index));
        public Task<string> SubmitTransactionAsync(ITransaction tx) => throw new NotSupportedException("Submission is deliberately through the production mini-protocol, not this CLI snapshot adapter.");
        public Task<Metadata?> GetTransactionMetadataAsync(string txHash) => Task.FromResult<Metadata?>(null);
    }
}
