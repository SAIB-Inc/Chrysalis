using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Grpc.Net.Client;
using Utxorpc.V1alpha.Query;
using Utxorpc.V1alpha.Submit;
using AnyChainTx = Utxorpc.V1alpha.Submit.AnyChainTx;
using CardanoSpec = Utxorpc.V1alpha.Cardano;
using ChrysalisAddress = Chrysalis.Wallet.Models.Addresses.Address;
using GrpcMetadata = Grpc.Core.Metadata;
using Transaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;

namespace Chrysalis.Tx.Providers;

public class UTxORPC : ICardanoDataProvider
{
    private readonly GrpcChannel _channel;
    private readonly QueryService.QueryServiceClient _queryClient;
    private readonly SubmitService.SubmitServiceClient _submitClient;
    private readonly GrpcMetadata _headers;
    private readonly NetworkType _networkType;

    public NetworkType NetworkType => _networkType;

    public UTxORPC(string endpoint, Dictionary<string, string>? headers = null, NetworkType networkType = NetworkType.Preview)
    {
        _networkType = networkType;
        _channel = GrpcChannel.ForAddress(endpoint);
        _queryClient = new QueryService.QueryServiceClient(_channel);
        _submitClient = new SubmitService.SubmitServiceClient(_channel);

        _headers = [];
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
                _headers.Add(header.Key, header.Value);
        }
    }

    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> addresses)
    {
        if (addresses.Count == 0) return [];

        List<ResolvedInput> results = [];

        foreach (string address in addresses)
        {
            byte[] addressBytes = ChrysalisAddress.FromBech32(address).ToBytes();

            SearchUtxosRequest request = new()
            {
                Predicate = new UtxoPredicate
                {
                    Match = new AnyUtxoPattern
                    {
                        Cardano = new CardanoSpec.TxOutputPattern
                        {
                            Address = new CardanoSpec.AddressPattern
                            {
                                ExactAddress = ByteString.CopyFrom(addressBytes)
                            }
                        }
                    }
                },
                MaxItems = 100
            };

            SearchUtxosResponse response = await _queryClient.SearchUtxosAsync(request, headers: _headers);

            foreach (AnyUtxoData item in response.Items)
            {
                if (item.TxoRef == null)
                    continue;

                ResolvedInput? resolved = MapToResolvedInput(item);
                if (resolved != null)
                    results.Add(resolved);
            }
        }

        return results;
    }

    public async Task<ProtocolParams> GetParametersAsync()
    {
        ReadParamsRequest request = new();
        ReadParamsResponse response = await _queryClient.ReadParamsAsync(request, headers: _headers);

        if (response.Values?.Cardano == null)
            throw new InvalidOperationException("Failed to get protocol parameters");

        return MapProtocolParams(response.Values.Cardano);
    }

    public async Task<string> SubmitTransactionAsync(Transaction tx)
    {
        byte[] txBytes = CborSerializer.Serialize(tx);

        SubmitTxRequest request = new()
        {
            Tx = new AnyChainTx { Raw = ByteString.CopyFrom(txBytes) }
        };

        SubmitTxResponse response = await _submitClient.SubmitTxAsync(request, headers: _headers);

        if (response.Ref.IsEmpty)
            throw new InvalidOperationException("Transaction submission failed");

        return Convert.ToHexStringLower(response.Ref.ToByteArray());
    }

    public Task<Metadata?> GetTransactionMetadataAsync(string txHash)
    {
        throw new NotImplementedException(
            "Transaction metadata retrieval is not supported via UTxO RPC. " +
            "Use Blockfrost or another provider for metadata queries.");
    }

    #region Private Helpers

    private static ResolvedInput? MapToResolvedInput(AnyUtxoData item)
    {
        TxoRef txoRef = item.TxoRef!;
        TransactionInput input = new(txoRef.Hash.ToByteArray(), txoRef.Index);

        if (!item.NativeBytes.IsEmpty)
        {
            try
            {
                TransactionOutput output = CborSerializer.Deserialize<TransactionOutput>(item.NativeBytes.ToByteArray());
                return new ResolvedInput(input, output);
            }
            catch
            {
                // CBOR deserialization failed — fall through to protobuf mapping
            }
        }

        if (item.Cardano != null)
        {
            Value value = BuildValue(item.Cardano);
            Address address = new(item.Cardano.Address.ToByteArray());
            TransactionOutput output = new PostAlonzoTransactionOutput(address, value, null, null);
            return new ResolvedInput(input, output);
        }

        return null;
    }

    private static Value BuildValue(CardanoSpec.TxOutput txOutput)
    {
        Lovelace lovelace = new(ToUlong(txOutput.Coin));

        if (txOutput.Assets.Count == 0)
            return lovelace;

        Dictionary<byte[], TokenBundleOutput> multiAssets = [];

        foreach (CardanoSpec.Multiasset multiasset in txOutput.Assets)
        {
            byte[] policyId = multiasset.PolicyId.ToByteArray();
            Dictionary<byte[], ulong> tokens = [];

            foreach (CardanoSpec.Asset asset in multiasset.Assets)
            {
                tokens[asset.Name.ToByteArray()] = ToUlong(asset.OutputCoin);
            }

            multiAssets[policyId] = new TokenBundleOutput(tokens);
        }

        return new LovelaceWithMultiAsset(lovelace, new MultiAssetOutput(multiAssets));
    }

    private static ProtocolParams MapProtocolParams(CardanoSpec.PParams p)
    {
        Dictionary<int, CborMaybeIndefList<long>> costMdls = [];

        if (p.CostModels?.PlutusV1?.Values.Count > 0)
            costMdls[0] = new CborDefList<long>([.. p.CostModels.PlutusV1.Values]);

        if (p.CostModels?.PlutusV2?.Values.Count > 0)
            costMdls[1] = new CborDefList<long>([.. p.CostModels.PlutusV2.Values]);

        if (p.CostModels?.PlutusV3?.Values.Count > 0)
            costMdls[2] = new CborDefList<long>([.. p.CostModels.PlutusV3.Values]);

        return new ProtocolParams(
            MinFeeA: ToUlong(p.MinFeeCoefficient),
            MinFeeB: ToUlong(p.MinFeeConstant),
            MaxBlockBodySize: p.MaxBlockBodySize,
            MaxTransactionSize: p.MaxTxSize,
            MaxBlockHeaderSize: p.MaxBlockHeaderSize,
            KeyDeposit: ToUlong(p.StakeKeyDeposit),
            PoolDeposit: ToUlong(p.PoolDeposit),
            MaximumEpoch: p.PoolRetirementEpochBound,
            DesiredNumberOfStakePools: p.DesiredNumberOfPools,
            PoolPledgeInfluence: ToRational(p.PoolInfluence),
            ExpansionRate: ToRational(p.MonetaryExpansion),
            TreasuryGrowthRate: ToRational(p.TreasuryExpansion),
            ProtocolVersion: p.ProtocolVersion != null
                ? new ProtocolVersion((int)p.ProtocolVersion.Major, p.ProtocolVersion.Minor)
                : null,
            MinPoolCost: ToUlong(p.MinPoolCost),
            AdaPerUTxOByte: ToUlong(p.CoinsPerUtxoByte),
            CostModelsForScriptLanguage: new CostMdls(costMdls),
            ExecutionCosts: ToExPrices(p.Prices),
            MaxTxExUnits: p.MaxExecutionUnitsPerTransaction != null
                ? new ExUnits(p.MaxExecutionUnitsPerTransaction.Memory, p.MaxExecutionUnitsPerTransaction.Steps)
                : null,
            MaxBlockExUnits: p.MaxExecutionUnitsPerBlock != null
                ? new ExUnits(p.MaxExecutionUnitsPerBlock.Memory, p.MaxExecutionUnitsPerBlock.Steps)
                : null,
            MaxValueSize: p.MaxValueSize,
            CollateralPercentage: p.CollateralPercentage,
            MaxCollateralInputs: p.MaxCollateralInputs,
            PoolVotingThresholds: ToPoolVotingThresholds(p.PoolVotingThresholds),
            DRepVotingThresholds: ToDRepVotingThresholds(p.DrepVotingThresholds),
            MinCommitteeSize: p.MinCommitteeSize,
            CommitteeTermLimit: p.CommitteeTermLimit,
            GovernanceActionValidityPeriod: p.GovernanceActionValidityPeriod,
            GovernanceActionDeposit: ToUlong(p.GovernanceActionDeposit),
            DRepDeposit: ToUlong(p.DrepDeposit),
            DRepInactivityPeriod: p.DrepInactivityPeriod,
            MinFeeRefScriptCostPerByte: ToRational(p.MinFeeScriptRefCostPerByte)
        );
    }

    private static ulong ToUlong(CardanoSpec.BigInt? bigInt)
    {
        if (bigInt is null) return 0;

        return bigInt.BigIntCase switch
        {
            CardanoSpec.BigInt.BigIntOneofCase.Int => (ulong)bigInt.Int,
            CardanoSpec.BigInt.BigIntOneofCase.BigUInt => ToBigUInt(bigInt.BigUInt.ToByteArray()),
            _ => 0
        };
    }

    private static ulong ToBigUInt(byte[] bytes)
    {
        if (bytes.Length == 0) return 0;
        byte[] padded = new byte[8];
        int start = Math.Max(0, 8 - bytes.Length);
        int srcStart = Math.Max(0, bytes.Length - 8);
        Array.Copy(bytes, srcStart, padded, start, Math.Min(bytes.Length, 8));
        Array.Reverse(padded);
        return BitConverter.ToUInt64(padded);
    }

    private static CborRationalNumber? ToRational(CardanoSpec.RationalNumber? rn) =>
        rn is null ? null : new CborRationalNumber((ulong)rn.Numerator, rn.Denominator);

    private static CborRationalNumber ToRationalRequired(CardanoSpec.RationalNumber rn) =>
        new((ulong)rn.Numerator, rn.Denominator);

    private static ExUnitPrices? ToExPrices(CardanoSpec.ExPrices? prices) =>
        prices is null ? null : new ExUnitPrices(
            ToRationalRequired(prices.Memory),
            ToRationalRequired(prices.Steps));

    private static PoolVotingThresholds? ToPoolVotingThresholds(CardanoSpec.VotingThresholds? thresholds)
    {
        if (thresholds is null || thresholds.Thresholds.Count < 5) return null;

        return new PoolVotingThresholds(
            ToRationalRequired(thresholds.Thresholds[0]),
            ToRationalRequired(thresholds.Thresholds[1]),
            ToRationalRequired(thresholds.Thresholds[2]),
            ToRationalRequired(thresholds.Thresholds[3]),
            ToRationalRequired(thresholds.Thresholds[4]));
    }

    private static DRepVotingThresholds? ToDRepVotingThresholds(CardanoSpec.VotingThresholds? thresholds)
    {
        if (thresholds is null || thresholds.Thresholds.Count < 10) return null;

        return new DRepVotingThresholds(
            ToRationalRequired(thresholds.Thresholds[0]),
            ToRationalRequired(thresholds.Thresholds[1]),
            ToRationalRequired(thresholds.Thresholds[2]),
            ToRationalRequired(thresholds.Thresholds[3]),
            ToRationalRequired(thresholds.Thresholds[4]),
            ToRationalRequired(thresholds.Thresholds[5]),
            ToRationalRequired(thresholds.Thresholds[6]),
            ToRationalRequired(thresholds.Thresholds[7]),
            ToRationalRequired(thresholds.Thresholds[8]),
            ToRationalRequired(thresholds.Thresholds[9]));
    }

    #endregion
}
