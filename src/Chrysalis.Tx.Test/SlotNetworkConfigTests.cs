using System.Globalization;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Tx.Test.Providers;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using Xunit;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Test;

public sealed class SlotNetworkConfigTests
{
    internal static readonly ResolvedInput Funds = Utxo(0, 10_000_000);
    internal static readonly ResolvedInput Collateral = Utxo(1, 5_000_000);

    // Times from Pallas's well-known networks (pallas-traverse wellknown.rs): 20-second Byron slots, then 1-second slots.
    [Theory]
    [InlineData(NetworkType.Mainnet, 0L, "2017-09-23T21:44:51Z")]
    [InlineData(NetworkType.Mainnet, 4_492_800L, "2020-07-29T21:44:51Z")]
    [InlineData(NetworkType.Preprod, 0L, "2022-06-01T00:00:00Z")]
    [InlineData(NetworkType.Preprod, 86_399L, "2022-06-20T23:59:40Z")]
    [InlineData(NetworkType.Preview, 0L, "2022-10-25T00:00:00Z")]
    public void BuiltInProfilesConvertEveryEraBothWays(NetworkType network, long slot, string time)
    {
        SlotNetworkConfig config = SlotUtil.GetSlotNetworkConfig(network);
        DateTimeOffset expected = DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);
        Assert.Equal(expected.UtcDateTime, SlotUtil.GetUTCTimeFromSlot(config, slot));
        Assert.Equal(slot, SlotUtil.GetSlotFromUTCTime(config, expected.UtcDateTime));
        Assert.Equal(expected.ToUnixTimeSeconds(), SlotUtil.GetPosixTimeSecondsFromSlot(config, slot));
    }

    [Theory]
    [InlineData(NetworkType.Testnet)]
    [InlineData(NetworkType.Unknown)]
    public void NetworksWithoutABuiltInProfileThrow(NetworkType network) =>
        Assert.Throws<SlotConversionException>(() => SlotUtil.GetSlotNetworkConfig(network));

    [Fact]
    public async Task WithoutAProfileOnlyValidityBoundsFailWithTheTypedError()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        CliSnapshotProvider provider = new(KoiosTests.Vectors.Value.Parameters(CliVectors.G1), [], NetworkType.Testnet);
        await Assert.ThrowsAsync<OperationCanceledException>(() => ((ICardanoDataProvider)provider).GetSlotNetworkConfigAsync(cancelled.Token));
        _ = await Builder(provider).PayLovelace(BlockfrostTests.Address, 2_000_000).Complete();
        PlutusV3Script policy = PlutusV3Script.Create(3, TestScripts.Wrap("(program 1.0.0 (lam ctx (con unit ())))"));
        _ = await Builder(provider).AddMint(policy.HashHex(), new() { ["00"] = 1 }, policy, PlutusInt.Create(0)).Complete();
        await Assert.ThrowsAsync<SlotConversionException>(() =>
            Builder(provider).AddMint(policy.HashHex(), new() { ["00"] = 1 }, policy, PlutusInt.Create(0)).SetValidFrom(1).Complete());
    }

    [Fact]
    public void AProfileConvertsOnlyBetweenItsStartAndItsEnd()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SlotNetworkConfig config = new(start, [new(0, 20_000), new(10, 100)], endSlot: 50);
        Assert.Equal(start.AddSeconds(180), SlotUtil.GetUTCTimeFromSlot(config, 9));
        Assert.Equal(start.AddMilliseconds(203_900), SlotUtil.GetUTCTimeFromSlot(config, 49));
        Assert.Equal(9L, SlotUtil.GetSlotFromUTCTime(config, start.AddMilliseconds(199_999)));
        Assert.Equal(49L, SlotUtil.GetSlotFromUTCTime(config, start.AddMilliseconds(203_999)));
        Assert.Throws<SlotConversionException>(() => SlotUtil.GetUTCTimeFromSlot(config, 50));
        Assert.Throws<SlotConversionException>(() => SlotUtil.GetSlotFromUTCTime(config, start.AddSeconds(204)));
        Assert.Throws<SlotConversionException>(() => SlotUtil.GetSlotFromUTCTime(config, start.AddMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SlotUtil.GetUTCTimeFromSlot(config, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotNetworkConfig(start, [new(0, 20_000), new(10, 100)], endSlot: 9));
        Assert.Equal(10UL, new SlotNetworkConfig(start, [new(0, 20_000), new(10, 100)], endSlot: 10).EndSlot);
        SlotNetworkConfig overflows = new(start, [new(0, uint.MaxValue)], endSlot: 1_000_000);
        Assert.Equal("Slot 100000 is out of range.", Assert.Throws<SlotConversionException>(() => SlotUtil.GetUTCTimeFromSlot(overflows, 100_000)).Message);
        // A system start one tick past a whole millisecond keeps its tick, and slots convert back to themselves.
        SlotNetworkConfig ticks = new(start.AddTicks(1), [new(0, 1_000)]);
        Assert.Equal(start.AddTicks(1), SlotUtil.GetUTCTimeFromSlot(ticks, 0));
        Assert.Equal(0L, SlotUtil.GetSlotFromUTCTime(ticks, SlotUtil.GetUTCTimeFromSlot(ticks, 0)));
        Assert.Equal(5L, SlotUtil.GetSlotFromUTCTime(ticks, SlotUtil.GetUTCTimeFromSlot(ticks, 5)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1/1000")]
    [InlineData("0/0")]
    [InlineData("0/1000 10/1000 5/1000")]
    [InlineData("0/4294967296")]
    public void AProfileRefusesErasItCannotUse(string eras) =>
        Assert.ThrowsAny<ArgumentException>(() => new SlotNetworkConfig(DateTimeOffset.UnixEpoch, [.. eras.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(era => era.Split('/')).Select(era => new SlotEra(ulong.Parse(era[0], CultureInfo.InvariantCulture), ulong.Parse(era[1], CultureInfo.InvariantCulture)))]));

    private static TxBuilder Builder(ICardanoDataProvider provider) =>
        new TxBuilder(provider).AddUnspentOutputs([Funds]).ProvideCollateral([Collateral]).SetChangeAddress(BlockfrostTests.Address);

    private static ResolvedInput Utxo(ulong index, ulong lovelace) => new(TransactionInput.Create((byte[])[.. Enumerable.Repeat((byte)0x31, 32)], index),
        PostAlonzoTransactionOutput.Create(new Address(WalletAddress.FromBech32(BlockfrostTests.Address).ToBytes()), Lovelace.Create(lovelace)));
}
