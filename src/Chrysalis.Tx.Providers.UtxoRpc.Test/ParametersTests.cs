using System.Text.Json;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Utxorpc.V1Beta.Query;
using Xunit;
using Spec = Utxorpc.V1Beta.Cardano;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises parameter mapping through the public provider and real gRPC.</summary>
public sealed class ParametersTests
{
    /// <summary>The two upstream cases state every ledger field, including each voting position.</summary>
    /// <returns>A task for both golden cases.</returns>
    [Fact]
    public async Task MapsEveryGouroborosParameter()
    {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden/gouroboros/pparams.json"), TestContext.Current.CancellationToken));
        HashSet<string> keys = [];
        int cases = 0;
        foreach (JsonElement testCase in document.RootElement.EnumerateArray())
        {
            Spec.PParams parameters = JsonParser.Default.Parse<Spec.PParams>(testCase.GetProperty("pparams").GetRawText());
            JsonElement expected = testCase.GetProperty("expected");
            LedgerParameters.Equal(expected, await MapAsync(parameters));
            keys.UnionWith(expected.EnumerateObject().Select(property => property.Name));
            cases++;
        }

        Assert.Equal(2, cases);
        Assert.Equal(31, keys.Count);
    }

    /// <summary>Nonnegative Int values retain both signed-range boundaries.</summary>
    /// <param name="value">The signed protobuf integer.</param>
    /// <returns>A task for the boundary check.</returns>
    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public async Task AcceptsNonnegativeIntBoundaries(long value)
    {
        ProtocolParams result = await MapAsync(new Spec.PParams { CoinsPerUtxoByte = new Spec.BigInt { Int = value } });
        Assert.Equal((ulong)value, result.AdaPerUTxOByte);
    }

    /// <summary>Unsigned bytes retain zero, ulong.MaxValue and fitting leading zeroes.</summary>
    /// <param name="bytes">The big-endian integer in hex.</param>
    /// <param name="expected">The exact unsigned value.</param>
    /// <returns>A task for the boundary check.</returns>
    [Theory]
    [InlineData("", 0UL)]
    [InlineData("0000", 0UL)]
    [InlineData("0102030405060708", 0x0102030405060708UL)]
    [InlineData("FFFFFFFFFFFFFFFF", ulong.MaxValue)]
    [InlineData("00FFFFFFFFFFFFFFFF", ulong.MaxValue)]
    public async Task AcceptsUnsignedByteBoundaries(string bytes, ulong expected)
    {
        ProtocolParams result = await MapAsync(new Spec.PParams { CoinsPerUtxoByte = new Spec.BigInt { BigUInt = ByteString.CopyFrom(Convert.FromHexString(bytes)) } });
        Assert.Equal(expected, result.AdaPerUTxOByte);
    }

    /// <summary>Negative and too-wide integers fail with their field and cause preserved.</summary>
    /// <param name="form">The invalid protobuf integer form.</param>
    /// <returns>A task for the invalid response.</returns>
    [Theory]
    [InlineData("negative")]
    [InlineData("minimum")]
    [InlineData("big-negative")]
    [InlineData("wide")]
    [InlineData("wide-leading-zero")]
    public async Task RejectsInvalidIntegers(string form)
    {
        Spec.BigInt value = form switch
        {
            "negative" => new Spec.BigInt { Int = -1 },
            "minimum" => new Spec.BigInt { Int = long.MinValue },
            "big-negative" => new Spec.BigInt { BigNInt = ByteString.CopyFrom([1]) },
            "wide-leading-zero" => new Spec.BigInt { BigUInt = ByteString.CopyFrom(Convert.FromHexString("00010000000000000000")) },
            _ => new Spec.BigInt { BigUInt = ByteString.CopyFrom([1, 0, 0, 0, 0, 0, 0, 0, 0]) }
        };
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => MapAsync(new Spec.PParams { CoinsPerUtxoByte = value }));
        AssertInvalid(error, "coins_per_utxo_byte");
    }

    /// <summary>Rationals cannot be negative or have a zero denominator.</summary>
    /// <param name="numerator">The invalid numerator.</param>
    /// <param name="denominator">The invalid denominator.</param>
    /// <returns>A task for the invalid response.</returns>
    [Theory]
    [InlineData(-1, 2u)]
    [InlineData(1, 0u)]
    public async Task RejectsInvalidRationals(int numerator, uint denominator)
    {
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => MapAsync(new Spec.PParams
        {
            PoolInfluence = new Spec.RationalNumber { Numerator = numerator, Denominator = denominator }
        }));
        AssertInvalid(error, "pool_influence");
    }

    /// <summary>Nonempty voting lists must have the exact ledger count.</summary>
    /// <param name="pool">Whether to set the pool rather than DRep list.</param>
    /// <param name="count">The invalid list length.</param>
    /// <returns>A task for the count check.</returns>
    [Theory]
    [InlineData(true, 4)]
    [InlineData(true, 6)]
    [InlineData(false, 9)]
    [InlineData(false, 11)]
    public async Task RejectsWrongThresholdCounts(bool pool, int count)
    {
        Spec.VotingThresholds thresholds = new();
        thresholds.Thresholds.Add(Enumerable.Range(0, count).Select(_ => new Spec.RationalNumber { Numerator = 1, Denominator = 2 }));
        Spec.PParams parameters = new();
        if (pool)
        {
            parameters.PoolVotingThresholds = thresholds;
        }
        else
        {
            parameters.DrepVotingThresholds = thresholds;
        }

        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => MapAsync(parameters));
        AssertInvalid(error, pool ? "pool_voting_thresholds" : "drep_voting_thresholds");
    }

    /// <summary>Absent messages, empty models and empty threshold lists stay unknown; scalar zeros survive.</summary>
    /// <returns>A task for the absence checks.</returns>
    [Fact]
    public async Task PreservesMissingMessagesAndScalarZeros()
    {
        ProtocolParams absent = await MapAsync(new Spec.PParams());
        Assert.Null(absent.AdaPerUTxOByte);
        Assert.Null(absent.PoolPledgeInfluence);
        Assert.Null(absent.ProtocolVersion);
        Assert.Null(absent.ExecutionCosts);
        Assert.Null(absent.MaxTxExUnits);
        Assert.Null(absent.MaxBlockExUnits);
        Assert.Null(absent.CostModelsForScriptLanguage);
        Assert.Null(absent.PoolVotingThresholds);
        Assert.Null(absent.DRepVotingThresholds);
        Assert.Null(absent.MaximumEpoch);
        Assert.Equal(0UL, absent.MaxTransactionSize);
        ProtocolParams empty = await MapAsync(new Spec.PParams
        {
            CostModels = new Spec.CostModels { PlutusV1 = new Spec.CostModel(), PlutusV2 = new Spec.CostModel(), PlutusV3 = new Spec.CostModel(), PlutusV4 = new Spec.CostModel { Values = { 7 } } },
            PoolVotingThresholds = new Spec.VotingThresholds(),
            DrepVotingThresholds = new Spec.VotingThresholds()
        });
        Assert.NotNull(empty.CostModelsForScriptLanguage);
        Assert.Null(empty.CostModelsForScriptLanguage.PlutusV1);
        Assert.Null(empty.CostModelsForScriptLanguage.PlutusV2);
        Assert.Null(empty.CostModelsForScriptLanguage.PlutusV3);
        Assert.Null(empty.PoolVotingThresholds);
        Assert.Null(empty.DRepVotingThresholds);
    }

    /// <summary>A reply with no Cardano parameter payload is unusable.</summary>
    /// <param name="withValues">Whether to send an empty wrapper instead of no wrapper.</param>
    /// <returns>A task for the invalid response.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsMissingCardanoParameters(bool withValues)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Handler = (_, _) => Task.FromResult(new ReadParamsResponse { Values = withValues ? new AnyChainParams() : null });
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        UtxoRpcException error = await Assert.ThrowsAsync<UtxoRpcException>(() => provider.GetParametersAsync(CancellationToken.None));
        AssertInvalid(error, "values.cardano");
    }

    /// <summary>Rationals retain their wire numerator and denominator, rather than being reduced.</summary>
    /// <returns>A task for the exact representation check.</returns>
    [Fact]
    public async Task PreservesUnreducedRationals()
    {
        ProtocolParams parameters = await MapAsync(new Spec.PParams { PoolInfluence = new Spec.RationalNumber { Numerator = 2, Denominator = 4 } });
        CborRationalNumber rational = Assert.IsType<CborRationalNumber>(parameters.PoolPledgeInfluence);
        Assert.Equal(2UL, rational.Numerator);
        Assert.Equal(4UL, rational.Denominator);
    }

    private static async Task<ProtocolParams> MapAsync(Spec.PParams parameters)
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        fake.Handler = (_, _) => Task.FromResult(FakeUtxoRpc.Answer(parameters));
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Preprod);
        return await provider.GetParametersAsync(CancellationToken.None);
    }

    private static void AssertInvalid(UtxoRpcException error, string field)
    {
        Assert.Null(error.StatusCode);
        Assert.StartsWith("ReadParams: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }
}
