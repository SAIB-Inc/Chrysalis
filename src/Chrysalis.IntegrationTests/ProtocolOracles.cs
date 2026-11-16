using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Xunit;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.IntegrationTests;

internal static class ProtocolOracles
{
    internal static JsonDocument GoldenUtxo()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Golden/utxo-by-address-expected.json")
            ?? throw new InvalidOperationException("Missing independent complete UTxO fixture.");
        return JsonDocument.Parse(stream);
    }

    internal static SpecificPoint Point(JsonElement tip) => new(tip.GetProperty("slot").GetUInt64(), Convert.FromHexString(tip.GetProperty("hash").GetString()!));

    internal static void AssertPoint(JsonElement expected, Point actual)
    {
        SpecificPoint point = Assert.IsType<SpecificPoint>(actual);
        Assert.Equal(expected.GetProperty("slot").GetUInt64(), point.Slot);
        Assert.Equal(expected.GetProperty("hash").GetString(), Convert.ToHexStringLower(point.Hash.Span));
    }

    internal static ulong Era(JsonElement tip) => tip.GetProperty("era").GetString() switch
    {
        "Byron" => 0,
        "Shelley" => 1,
        "Allegra" => 2,
        "Mary" => 3,
        "Alonzo" => 4,
        "Babbage" => 5,
        "Conway" => 6,
        _ => throw new NotSupportedException("The oracle reports an era not mapped by this test adapter.")
    };

    internal static ProtocolParams Parameters(JsonElement j)
    {
        ulong U(string name) => j.GetProperty(name).GetUInt64();
        ExUnits Units(string name) => ExUnits.Create(j.GetProperty(name).GetProperty("memory").GetUInt64(), j.GetProperty(name).GetProperty("steps").GetUInt64());
        JsonElement models = j.GetProperty("costModels");
        ICborMaybeIndefList<long> Model(string name) => CborDefList<long>.Create([.. models.GetProperty(name).EnumerateArray().Select(value => value.GetInt64())]);
        JsonElement pool = j.GetProperty("poolVotingThresholds");
        JsonElement drep = j.GetProperty("dRepVotingThresholds");
        JsonElement version = j.GetProperty("protocolVersion");
        JsonElement prices = j.GetProperty("executionUnitPrices");
        Dictionary<string, object?> values = new()
        {
            [nameof(ProtocolParams.MinFeeA)] = U("txFeePerByte"),
            [nameof(ProtocolParams.MinFeeB)] = U("txFeeFixed"),
            [nameof(ProtocolParams.MaxBlockBodySize)] = U("maxBlockBodySize"),
            [nameof(ProtocolParams.MaxTransactionSize)] = U("maxTxSize"),
            [nameof(ProtocolParams.MaxBlockHeaderSize)] = U("maxBlockHeaderSize"),
            [nameof(ProtocolParams.KeyDeposit)] = U("stakeAddressDeposit"),
            [nameof(ProtocolParams.PoolDeposit)] = U("stakePoolDeposit"),
            [nameof(ProtocolParams.MaximumEpoch)] = U("poolRetireMaxEpoch"),
            [nameof(ProtocolParams.DesiredNumberOfStakePools)] = U("stakePoolTargetNum"),
            [nameof(ProtocolParams.PoolPledgeInfluence)] = Rational(j.GetProperty("poolPledgeInfluence")),
            [nameof(ProtocolParams.ExpansionRate)] = Rational(j.GetProperty("monetaryExpansion")),
            [nameof(ProtocolParams.TreasuryGrowthRate)] = Rational(j.GetProperty("treasuryCut")),
            [nameof(ProtocolParams.ProtocolVersion)] = ProtocolVersion.Create(version.GetProperty("major").GetUInt64(), version.GetProperty("minor").GetUInt64()),
            [nameof(ProtocolParams.MinPoolCost)] = U("minPoolCost"),
            [nameof(ProtocolParams.AdaPerUTxOByte)] = U("utxoCostPerByte"),
            [nameof(ProtocolParams.CostModelsForScriptLanguage)] = new CostMdls(Model("PlutusV1"), Model("PlutusV2"), Model("PlutusV3")),
            [nameof(ProtocolParams.ExecutionCosts)] = ExUnitPrices.Create(Rational(prices.GetProperty("priceMemory")), Rational(prices.GetProperty("priceSteps"))),
            [nameof(ProtocolParams.MaxTxExUnits)] = Units("maxTxExecutionUnits"),
            [nameof(ProtocolParams.MaxBlockExUnits)] = Units("maxBlockExecutionUnits"),
            [nameof(ProtocolParams.MaxValueSize)] = U("maxValueSize"),
            [nameof(ProtocolParams.CollateralPercentage)] = U("collateralPercentage"),
            [nameof(ProtocolParams.MaxCollateralInputs)] = U("maxCollateralInputs"),
            [nameof(ProtocolParams.PoolVotingThresholds)] = PoolVotingThresholds.Create(Rational(pool.GetProperty("motionNoConfidence")), Rational(pool.GetProperty("committeeNormal")), Rational(pool.GetProperty("committeeNoConfidence")), Rational(pool.GetProperty("hardForkInitiation")), Rational(pool.GetProperty("ppSecurityGroup"))),
            [nameof(ProtocolParams.DRepVotingThresholds)] = DRepVotingThresholds.Create(Rational(drep.GetProperty("motionNoConfidence")), Rational(drep.GetProperty("committeeNormal")), Rational(drep.GetProperty("committeeNoConfidence")), Rational(drep.GetProperty("updateToConstitution")), Rational(drep.GetProperty("hardForkInitiation")), Rational(drep.GetProperty("ppNetworkGroup")), Rational(drep.GetProperty("ppEconomicGroup")), Rational(drep.GetProperty("ppTechnicalGroup")), Rational(drep.GetProperty("ppGovGroup")), Rational(drep.GetProperty("treasuryWithdrawal"))),
            [nameof(ProtocolParams.MinCommitteeSize)] = U("committeeMinSize"),
            [nameof(ProtocolParams.CommitteeTermLimit)] = U("committeeMaxTermLength"),
            [nameof(ProtocolParams.GovernanceActionValidityPeriod)] = U("govActionLifetime"),
            [nameof(ProtocolParams.GovernanceActionDeposit)] = U("govActionDeposit"),
            [nameof(ProtocolParams.DRepDeposit)] = U("dRepDeposit"),
            [nameof(ProtocolParams.DRepInactivityPeriod)] = U("dRepActivity"),
            [nameof(ProtocolParams.MinFeeRefScriptCostPerByte)] = Rational(j.GetProperty("minFeeRefScriptCostPerByte"))
        };
        ConstructorInfo constructor = typeof(ProtocolParams).GetConstructors().Single(candidate => candidate.GetParameters().Length > 5);
        return (ProtocolParams)constructor.Invoke([.. constructor.GetParameters().Select(parameter => values[parameter.Name!])]);
    }

    internal static CborRationalNumber Rational(JsonElement value)
    {
        decimal number = value.GetDecimal();
        int scale = (decimal.GetBits(number)[3] >> 16) & 0xff;
        ulong denominator = 1;
        for (int i = 0; i < scale; i++)
        {
            denominator = checked(denominator * 10);
        }
        return new CborRationalNumber(checked((ulong)(number * denominator)), denominator);
    }

    internal static void AssertEquivalent(object? expected, object? actual, string path = "parameters")
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }
        Assert.NotNull(actual);
        // Ledger semantics do not depend on definite/indefinite cost-model list encoding.
        if (expected is ICborMaybeIndefList<long>)
        {
            _ = Assert.IsAssignableFrom<ICborMaybeIndefList<long>>(actual);
            AssertEquivalent(expected.GetType().GetProperty("Value")!.GetValue(expected), actual.GetType().GetProperty("Value")!.GetValue(actual), path);
            return;
        }
        Assert.Equal(expected.GetType(), actual.GetType());
        if (expected is CborRationalNumber rational)
        {
            CborRationalNumber other = Assert.IsType<CborRationalNumber>(actual);
            Assert.True((BigInteger)rational.Numerator * other.Denominator == (BigInteger)other.Numerator * rational.Denominator, $"{path}: rational differs from the CLI oracle.");
        }
        else if (expected is string || expected.GetType().IsPrimitive || expected is decimal)
        {
            Assert.True(expected.Equals(actual), $"{path}: expected {expected}, actual {actual}");
        }
        else if (expected is IEnumerable items)
        {
            object?[] wanted = [.. items.Cast<object?>()];
            object?[] obtained = [.. Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object?>()];
            Assert.Equal(wanted.Length, obtained.Length);
            for (int index = 0; index < wanted.Length; index++)
            {
                AssertEquivalent(wanted[index], obtained[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
            }
        }
        else
        {
            foreach (PropertyInfo property in expected.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (property.Name is "Raw" or "ConstrIndex" or "IsIndefinite")
                {
                    continue;
                }
                AssertEquivalent(property.GetValue(expected), property.GetValue(actual), path + "." + property.Name);
            }
        }
    }

    internal static string SelectInputReference(JsonElement reference)
    {
        AssertNonEmptyReferenceUtxos(reference);
        return reference.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).First();
    }

    internal static void AssertNonEmptyReferenceUtxos(JsonElement expected)
    {
        Assert.True(expected.EnumerateObject().Any(), "Oracle precondition: CHRYSALIS_QUERY_ADDRESS must be a public Preprod reference address with at least one current UTxO; an empty result cannot exercise decoding.");
    }

    internal static void AssertUtxos(JsonElement expected, UtxoByAddressResponse actual)
    {
        AssertNonEmptyReferenceUtxos(expected);
        Assert.NotEmpty(actual.Utxos);
        Dictionary<string, ITransactionOutput> outputs = actual.Utxos.ToDictionary(
            pair => Convert.ToHexStringLower(pair.Key.TransactionId.Span) + "#" + pair.Key.Index.ToString(CultureInfo.InvariantCulture), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected.EnumerateObject().Count(), outputs.Count);
        foreach (JsonProperty input in expected.EnumerateObject())
        {
            Assert.True(outputs.TryGetValue(input.Name, out ITransactionOutput? output), "Missing CLI UTxO: " + input.Name);
            Assert.NotNull(output);
            byte[] address = input.Value.TryGetProperty("addressHex", out JsonElement addressHex)
                ? Convert.FromHexString(addressHex.GetString()!) : WalletAddress.FromBech32(input.Value.GetProperty("address").GetString()!).ToBytes();
            Assert.Equal(address, output.Address().ToArray());
            AssertDatumAndScript(input.Value, output);
            JsonElement value = input.Value.GetProperty("value");
            Assert.Equal(value.GetProperty("lovelace").GetUInt64(), output.Amount().Lovelace());
            Dictionary<string, Chrysalis.Codec.Types.Cardano.Core.Common.TokenBundleOutput> assets = output.Amount().MultiAsset();
            Assert.Equal(value.EnumerateObject().Count(property => property.Name != "lovelace"), assets.Count);
            foreach (JsonProperty policy in value.EnumerateObject().Where(property => property.Name != "lovelace"))
            {
                KeyValuePair<string, Chrysalis.Codec.Types.Cardano.Core.Common.TokenBundleOutput> found = Assert.Single(assets, pair => string.Equals(pair.Key, policy.Name, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(policy.Value.EnumerateObject().Count(), found.Value.Value.Count);
                foreach (JsonProperty asset in policy.Value.EnumerateObject())
                {
                    KeyValuePair<ReadOnlyMemory<byte>, ulong> token = Assert.Single(found.Value.Value, pair => Convert.ToHexStringLower(pair.Key.Span) == asset.Name);
                    Assert.Equal(asset.Value.GetUInt64(), token.Value);
                }
            }
        }
    }

    private static void AssertDatumAndScript(JsonElement expected, ITransactionOutput actual)
    {
        // UTxO JSON cannot carry a supplemental transaction datum; hash and inline
        // forms are represented by datumhash and inlineDatum/inlineDatumRaw instead.
        Assert.Equal(JsonValueKind.Null, expected.GetProperty("datum").ValueKind);
        // Inspect the union directly: a nullable hash must distinguish absence from empty bytes.
        ReadOnlyMemory<byte>? datumHash = null;
        if (actual is AlonzoTransactionOutput alonzo) { datumHash = alonzo.DatumHash; }
        else if (actual is PostAlonzoTransactionOutput post && post.Datum is DatumHashOption hash) { datumHash = hash.DatumHash; }
        AssertOptionalHex(expected.GetProperty("datumhash"), datumHash);
        JsonElement inline = expected.GetProperty("inlineDatum");
        Assert.Equal(inline.ValueKind != JsonValueKind.Null, actual.InlineDatumRaw().HasValue);
        // Raw CBOR is the complete independent datum, without JSON integer/ordering loss.
        AssertOptionalHex(expected.GetProperty("inlineDatumRaw"), actual.InlineDatumRaw());
        JsonElement script = expected.GetProperty("referenceScript");
        ReadOnlyMemory<byte>? rawScript = actual.ScriptRef();
        if (script.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(rawScript);
            return;
        }
        _ = Assert.NotNull(rawScript);
        IScript decoded = CborSerializer.Deserialize<IScript>(rawScript.Value);
        (string language, string type, ReadOnlyMemory<byte> bytes) = decoded switch
        {
            MultiSigScript native => ("SimpleScriptLanguage", "SimpleScript", native.NativeScript.Raw),
            PlutusV1Script plutus => ("PlutusScriptLanguage PlutusScriptV1", "PlutusScriptV1", plutus.ScriptBytes),
            PlutusV2Script plutus => ("PlutusScriptLanguage PlutusScriptV2", "PlutusScriptV2", plutus.ScriptBytes),
            PlutusV3Script plutus => ("PlutusScriptLanguage PlutusScriptV3", "PlutusScriptV3", plutus.ScriptBytes),
            _ => throw new InvalidOperationException("Unsupported independent CLI reference-script language.")
        };
        Assert.Equal(language, script.GetProperty("scriptLanguage").GetString());
        JsonElement envelope = script.GetProperty("script");
        Assert.Equal(type, envelope.GetProperty("type").GetString());
        Assert.Equal(Convert.FromHexString(envelope.GetProperty("cborHex").GetString()!), bytes.ToArray());
    }

    private static void AssertOptionalHex(JsonElement expected, ReadOnlyMemory<byte>? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(actual);
            return;
        }
        _ = Assert.NotNull(actual);
        Assert.Equal(Convert.FromHexString(expected.GetString()!), actual.Value.ToArray());
    }
}
