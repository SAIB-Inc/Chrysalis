using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

internal static class X7ConwayProjection
{
    internal static void Check(PostMaryTransaction tx, byte[] raw, JsonElement cli)
    {
        // The CLI hides fields such as hash bytes, IsValid and the network ID, and renders
        // nested ledger values as prose. Cross-check every present key against its own
        // wire item as well; ScanMap calls the actual lazy getter, not the envelope writer.
        CborReader reader = new(raw);
        reader.ReadBeginArray();
        Assert.Equal(4, reader.ReadSize());
        ConwayTransactionBody body = Assert.IsType<ConwayTransactionBody>(tx.Body);
        PostAlonzoTransactionWitnessSet witnesses = Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses);
        ScanMap(ref reader, raw, body);
        ScanMap(ref reader, raw, witnesses);
        Assert.Equal(reader.ReadBoolean(), tx.IsValid);
        WireEquals(ref reader, raw, tx.AuxiliaryData);
        Assert.True(reader.Buffer.IsEmpty);
        Assert.Equal("Conway", cli.GetProperty("era").GetString());
        int Count(string key) => cli.GetProperty(key) is { ValueKind: JsonValueKind.Array } row ? row.GetArrayLength() : 0;
        ulong? Number(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetUInt64();
        void InputRefs(string key, IEnumerable<TransactionInput> actual) => Assert.Equal(cli.GetProperty(key).EnumerateArray().Select(item => item.GetString()),
            actual.Select(input => $"{Hex(input.TransactionId)}#{input.Index}"));
        Assert.Equal(ulong.Parse(cli.GetProperty("fee").GetString()!.Split(' ')[0], CultureInfo.InvariantCulture), body.Fee);
        InputRefs("inputs", body.Inputs);
        InputRefs("collateral inputs", body.Collateral!.Value);
        InputRefs("reference inputs", body.ReferenceInputs?.ToArray() ?? []);
        Assert.Equal(Count("outputs"), body.Outputs.ToArray().Length);
        foreach ((ITransactionOutput output, JsonElement projection) in body.Outputs.Zip(cli.GetProperty("outputs").EnumerateArray()))
        {
            CheckOutput(output, projection);
        }
        CheckOutput(Assert.IsAssignableFrom<ITransactionOutput>(body.CollateralReturn), cli.GetProperty("return collateral"));
        Assert.Equal(Count("certificates"), body.Certificates?.ToArray().Length ?? 0);
        Assert.Equal(Count("withdrawals"), body.Withdrawals?.Value.Count ?? 0);
        if (body.Withdrawals is { } withdrawals)
        {
            Assert.Equal(cli.GetProperty("withdrawals").EnumerateArray().Select(row => row.GetProperty("amount").GetString()).Order(),
                withdrawals.Value.Select(kv => kv.Value).Select(amount => $"{amount} Lovelace").Order());
        }
        Assert.Equal(Count("required signers (payment key hashes needed for scripts)"), body.RequiredSigners?.ToArray().Length ?? 0);
        Assert.Equal(Count("governance actions"), body.ProposalProcedures?.ToArray().Length ?? 0);
        Assert.Equal(cli.GetProperty("voters").EnumerateObject().Count(), body.VotingProcedures?.Value.Count ?? 0);
        Assert.Equal(Number(cli.GetProperty("currentTreasuryValue")), body.TreasuryValue);
        Assert.Equal(Number(cli.GetProperty("treasuryDonation")) ?? 0, body.Donation ?? 0);
        Assert.Equal(Number(cli.GetProperty("total collateral")), body.TotalCollateral);
        JsonElement range = cli.GetProperty("validity range");
        Assert.Equal(Number(range.GetProperty("lower bound")), body.ValidityIntervalStart);
        Assert.Equal(Number(range.GetProperty("upper bound")), body.TimeToLive);
        JsonElement metadata = cli.GetProperty("metadata");
        PostAlonzoAuxiliaryDataMap? auxiliary = tx.AuxiliaryData is PostAlonzoAuxiliaryDataMap map ? map : null;
        Assert.Equal(metadata.ValueKind == JsonValueKind.Object ? metadata.EnumerateObject().Count() : 0, auxiliary?.TransactionMetadata?.Value.Count ?? 0);
        Assert.Equal(cli.GetProperty("auxiliary scripts").ValueKind != JsonValueKind.Null, auxiliary?.NativeScripts?.ToArray().Length > 0);
        JsonElement mint = cli.GetProperty("mint");
        Assert.Equal(mint.ValueKind == JsonValueKind.Object ? mint.EnumerateObject().Count() : 0, body.Mint?.Value.Count ?? 0);
        if (body.Mint is { } assets)
        {
            foreach (JsonProperty policy in mint.EnumerateObject())
            {
                TokenBundleMint bundle = assets.Value[Convert.FromHexString(policy.Name.Split(' ')[1])];
                foreach (JsonProperty asset in policy.Value.EnumerateObject())
                {
                    Assert.Equal(asset.Value.GetInt64(), bundle.Value[Convert.FromHexString(asset.Name.Split(' ')[1])]);
                }
            }
        }
        Assert.Equal(Count("datums"), witnesses.PlutusDataSet?.ToArray().Length ?? 0);
        RedeemerMap redeemers = Assert.IsType<RedeemerMap>(witnesses.Redeemers);
        Assert.Equal(cli.GetProperty("redeemers").EnumerateArray().Select(row => row.GetProperty("redeemer").GetProperty("execution units"))
            .Select(units => (units.GetProperty("memory").GetUInt64(), units.GetProperty("steps").GetUInt64())).Order(),
            redeemers.Value.Select(kv => kv.Value).Select(row => (row.ExUnits.Mem, row.ExUnits.Steps)).Order());
        Assert.Equal(Count("witnesses"), (witnesses.VKeyWitnesses?.ToArray().Length ?? 0) + (witnesses.BootstrapWitnesses?.ToArray().Length ?? 0));
        Assert.Equal(Count("scripts"), (witnesses.NativeScripts?.ToArray().Length ?? 0) + (witnesses.PlutusV1Scripts?.ToArray().Length ?? 0)
            + (witnesses.PlutusV2Scripts?.ToArray().Length ?? 0) + (witnesses.PlutusV3Scripts?.ToArray().Length ?? 0));
    }

    private static void CheckOutput(ITransactionOutput output, JsonElement cli)
    {
        ulong amount = output.Amount() switch { Lovelace ada => ada.Amount, LovelaceWithMultiAsset multi => multi.Amount, _ => throw new InvalidOperationException() };
        Assert.Equal(cli.GetProperty("amount").GetProperty("lovelace").GetUInt64(), amount);
        if (cli.TryGetProperty("payment credential key hash", out JsonElement hash) || cli.TryGetProperty("payment credential script hash", out hash))
        {
            Assert.Equal(hash.GetString(), Hex(output.Address().Slice(1, 28)));
        }
    }

    private static void ScanMap<T>(ref CborReader reader, byte[] raw, T model) where T : ICborType
    {
        reader.ReadBeginMap();
        int count = reader.ReadSize();
        Dictionary<int, PropertyInfo> fields = typeof(T).GetProperties().Where(p => p.GetCustomAttribute<CborPropertyAttribute>()?.IntKey is not null)
            .ToDictionary(p => p.GetCustomAttribute<CborPropertyAttribute>()!.IntKey!.Value);
        for (int i = 0; i < count; i++)
        {
            int key = reader.ReadInt32();
            WireEquals(ref reader, raw, fields[key].GetValue(model));
        }
    }

    private static void WireEquals(ref CborReader reader, byte[] raw, object? model)
    {
        int start = raw.Length - reader.Buffer.Length;
        CborReader expected = reader;
        reader.SkipDataItem();
        switch (model)
        {
            case ICborType codec: Assert.Equal(raw.AsSpan(start, raw.Length - reader.Buffer.Length - start).ToArray(), codec.Raw.ToArray()); break;
            case ulong number: Assert.Equal(expected.ReadUInt64(), number); break;
            case int number: Assert.Equal(expected.ReadInt32(), number); break;
            case ReadOnlyMemory<byte> bytes: Assert.Equal(expected.ReadByteStringToArray(), bytes.ToArray()); break;
            case null: Assert.Equal(0xF6, raw[start]); break;
            default: throw new InvalidOperationException($"Unhandled Conway field model {model.GetType()}");
        }
    }

    private static string Hex(ReadOnlyMemory<byte> data) => Convert.ToHexStringLower(data.Span);
}
