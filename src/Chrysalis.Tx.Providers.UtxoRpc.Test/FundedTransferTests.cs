using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Keys;
using Xunit;
using Xunit.Sdk;
using LedgerMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Spends only a fee on a guarded preprod self-transfer, never resubmitting.</summary>
/// <param name="output">Receives only the public transaction hash and fee.</param>
[Trait("Category", "Integration")]
[Collection("Live")]
public sealed class FundedTransferTests(ITestOutputHelper output)
{
    /// <summary>Builds, signs, submits once, waits and reads the payment and metadata back.</summary>
    /// <returns>A task bounded by ten minutes for confirmation and one minute for read-back.</returns>
    [LiveFact([LiveSettings.Endpoint, LiveSettings.Network, LiveSettings.Addresses, LiveSettings.WalletKeyFile])]
    public async Task ConfirmsOneSelfTransfer()
    {
        string hash = "not built";
        ulong fee = 0;
        string phase = "configuration";
        try
        {
            string? ledger = LiveSettings.Read(LiveSettings.FeeLedgerFile);
            string? budget = LiveSettings.Read(LiveSettings.FeeBudget);
            Assert.False(string.IsNullOrWhiteSpace(ledger), $"Missing {LiveSettings.FeeLedgerFile}.");
            Assert.True(ulong.TryParse(budget, NumberStyles.None, CultureInfo.InvariantCulture, out _), $"Missing or invalid {LiveSettings.FeeBudget}.");
            string wallet = Assert.Single(LiveSettings.WalletAddresses());
            using UtxoRpc provider = LiveSettings.CreateProvider();
            phase = "wallet read";
            List<ResolvedInput> utxos = await provider.GetUtxosAsync([wallet]);
            ResolvedInput? largest = null;
            foreach (ResolvedInput item in utxos)
            {
                if (item.Output.Amount().MultiAsset().Count == 0 && (largest is null || item.Output.Amount().Lovelace() > largest.Output.Amount().Lovelace())) { largest = item; }
            }
            Assert.NotNull(largest);
            phase = "build";
            PostMaryTransaction unsigned = await new TxBuilder(provider).AddInput(largest).PayLovelace(wallet, 2_000_000)
                .AddMetadata(674, MetadataText.Create("UTxO RPC self-transfer")).SetChangeAddress(wallet).Complete();
            hash = unsigned.Body.Hash();
            fee = unsigned.Body.Fee();
            Assert.True(fee <= 1_000_000, "The fee exceeds 1,000,000 lovelace.");
            byte[] self = Address.FromBech32(wallet).ToBytes();
            ulong? paymentIndex = null;
            ulong index = 0;
            foreach (ITransactionOutput item in unsigned.Body.Outputs())
            {
                Assert.True(item.Address().Span.SequenceEqual(self), "An output pays another address.");
                if (item.Amount().Lovelace() == 2_000_000 && item.Amount().MultiAsset().Count == 0) { paymentIndex ??= index; }
                index++;
            }
            _ = Assert.NotNull(paymentIndex);
            phase = "signing";
            ITransaction signed;
            using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(LiveSettings.Require(LiveSettings.WalletKeyFile))))
            {
                PrivateKey key = new(Convert.FromHexString(document.RootElement.GetProperty("Key").GetString()!),
                    Convert.FromHexString(document.RootElement.GetProperty("Chaincode").GetString()!));
                signed = unsigned.Sign(key);
            }
            phase = "fee budget";
            await ReserveFeeAsync(ledger, budget, hash, fee);
            output.WriteLine("{0}\t{1}", hash, fee);
            phase = "submission";
            Assert.Equal(hash, await provider.SubmitTransactionAsync(signed), ignoreCase: true);
            phase = "confirmation";
            using CancellationTokenSource confirmation = new(TimeSpan.FromMinutes(10));
            await provider.WaitForConfirmationAsync(hash, confirmation.Token);
            phase = "read-back";
            using CancellationTokenSource readBack = new(TimeSpan.FromMinutes(1));
            while (true)
            {
                ResolvedInput? payment = await provider.GetUtxoByOutRefAsync(hash, paymentIndex.Value, readBack.Token);
                LedgerMetadata? metadata = await provider.GetTransactionMetadataAsync(hash, readBack.Token);
                if (payment is not null && metadata is { } observed)
                {
                    Assert.True(payment.Output.Address().Span.SequenceEqual(self), "The read-back address differs.");
                    Assert.Equal(2_000_000UL, payment.Output.Amount().Lovelace());
                    Assert.Equal("UTxO RPC self-transfer", Assert.IsType<MetadataText>(observed.Value[674]).Value);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), readBack.Token);
            }
        }
        catch (XunitException) { throw; }
        catch (Exception)
        {
            // Do not retain an exception that could contain either key file's path or contents.
            throw new XunitException($"Self-transfer failed during {phase}; hash={hash}; fee={fee}. No resubmission.");
        }
    }

    internal static async Task ReserveFeeAsync(string? ledger, string? budget, string hash, ulong fee)
    {
        Assert.False(string.IsNullOrWhiteSpace(ledger), $"Missing {LiveSettings.FeeLedgerFile}.");
        Assert.True(ulong.TryParse(budget, NumberStyles.None, CultureInfo.InvariantCulture, out ulong limit), $"Missing or invalid {LiveSettings.FeeBudget}.");
        try
        {
            await using FileStream file = new(ledger, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using StreamReader reader = new(file, leaveOpen: true);
            ulong total = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                string[] parts = line.Split('\t');
                Assert.True(parts.Length == 3 && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    && parts[1].Length == 64 && parts[1].All(char.IsAsciiHexDigit)
                    && ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _), "A fee ledger line cannot be read.");
                total = checked(total + ulong.Parse(parts[2], CultureInfo.InvariantCulture));
            }
            Assert.True(fee <= limit && total <= limit - fee, "This fee would exceed the task's fee budget.");
            bool newline = file.Length != 0;
            if (newline) { file.Position = file.Length - 1; newline = file.ReadByte() != '\n'; }
            file.Position = file.Length;
            await using StreamWriter writer = new(file, leaveOpen: true);
            if (newline) { await writer.WriteLineAsync(); }
            await writer.WriteLineAsync(FormattableString.Invariant($"{DateTimeOffset.UtcNow:O}\t{hash}\t{fee}"));
        }
        catch (XunitException) { throw; }
        catch (Exception) { throw new XunitException("The fee ledger cannot be read or appended safely."); }
    }
}

/// <summary>Exercises funded guards without any live service, wallet or key.</summary>
public sealed class FundedGuardsTests
{
    /// <summary>Every invalid ledger or budget refuses before appending a reservation.</summary>
    /// <returns>A task for the isolated temporary ledger.</returns>
    [Fact]
    public async Task RejectsMissingMalformedAndExhaustedBudgets()
    {
        string ledger = Path.Combine(AppContext.BaseDirectory, "fee-guard-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tsv");
        string hash = new('a', 64);
        try
        {
            _ = await Assert.ThrowsAsync<FalseException>(() => FundedTransferTests.ReserveFeeAsync(null, "5000000", hash, 200000));
            _ = await Assert.ThrowsAsync<TrueException>(() => FundedTransferTests.ReserveFeeAsync(ledger, null, hash, 200000));
            await File.WriteAllTextAsync(ledger, "unreadable\n", TestContext.Current.CancellationToken);
            _ = await Assert.ThrowsAsync<TrueException>(() => FundedTransferTests.ReserveFeeAsync(ledger, "5000000", hash, 200000));
            Assert.Equal("unreadable\n", await File.ReadAllTextAsync(ledger, TestContext.Current.CancellationToken));
            await File.WriteAllTextAsync(ledger, "", TestContext.Current.CancellationToken);
            await FundedTransferTests.ReserveFeeAsync(ledger, "200000", hash, 200000);
            string reserved = await File.ReadAllTextAsync(ledger, TestContext.Current.CancellationToken);
            _ = await Assert.ThrowsAsync<TrueException>(() => FundedTransferTests.ReserveFeeAsync(ledger, "200000", hash, 1));
            Assert.Equal(reserved, await File.ReadAllTextAsync(ledger, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(ledger); }
    }
}
