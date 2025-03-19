using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Builder;


public class TransactionBuilder
{
    private readonly List<TransactionInput> inputs = [];
    private readonly List<TransactionOutput> outputs = [];
    public Address? changeAddress;
    private List<Utxo> availableUtxos = [];
    private CborUlong fee = new(0UL);
    private CborUlong? ttl;
    private CborUlong? validityStart;
    private readonly List<Certificate>? certificates;
    private readonly Withdrawals? withdrawals;
    private readonly Update? update;
    private readonly CborBytes? auxiliaryDataHash;
    private readonly MultiAssetMint? mint;
    private readonly CborBytes? scriptDataHash;
    private readonly List<TransactionInput>? collateral;
    private readonly List<CborBytes>? requiredSigners;
    private readonly CborInt? networkId;
    private TransactionOutput? collateralReturn;
    private readonly CborUlong? totalCollateral;
    private List<TransactionInput>? referenceInputs;

    private readonly VotingProcedures? votingProcedures;
    private readonly List<ProposalProcedure>? proposalProcedures;
    private readonly CborUlong? treasuryValue;
    private readonly CborUlong? donation;

    private TransactionWitnessSet? witnessSet;

    private readonly List<VKeyWitness>? vKeyWitnesses;
    private readonly List<NativeScript>? nativeScripts;
    private readonly List<BootstrapWitness>? bootstrapWitnesses;
    private readonly List<CborBytes>? plutusV1Scripts;
    private readonly List<CborBytes>? plutusV2Scripts;
    private readonly List<CborBytes>? plutusV3Scripts;
    private readonly List<PlutusData>? plutusData;
    private readonly Redeemers? redeemers;

    private readonly AuxiliaryData? auxiliaryData;


    public static TransactionBuilder Create()
    {
        return new TransactionBuilder();
    }
    public TransactionBuilder AddInput(TransactionInput input)
    {
        inputs.Add(input);
        return this;
    }

    public TransactionBuilder SetAvailableUtxos(List<Utxo> utxos)
    {
        availableUtxos = utxos;
        return this;
    }

    public TransactionBuilder SetChangeAddress(Address address)
    {
        changeAddress = address;
        return this;
    }

    public TransactionBuilder AddReferenceInput(TransactionInput input)
    {
        referenceInputs ??= [];
        referenceInputs.Add(input);
        return this;
    }

    public TransactionBuilder AddOutput(TransactionOutput output)
    {
        outputs.Add(output);
        return this;
    }

    public TransactionBuilder SetCollateralReturn(TransactionOutput output)
    {
        collateralReturn = output;
        return this;
    }

    public TransactionBuilder SetFee(ulong feeAmount)
    {
        fee = new CborUlong(feeAmount);
        return this;
    }

    public TransactionBuilder SetTtl(ulong ttlValue)
    {
        ttl = new CborUlong(ttlValue);
        return this;
    }

    public TransactionBuilder SetValidityIntervalStart(ulong start)
    {
        validityStart = new CborUlong(start);
        return this;
    }

    public TransactionBuilder SetValidityInterval(ulong start, ulong end)
    {
        validityStart = new CborUlong(start);
        ttl = new CborUlong(end);
        return this;
    }

    public TransactionBuilder SetWitnessSet(TransactionWitnessSet witnesses)
    {
        witnessSet = witnesses;
        return this;
    }

    public TransactionBuilder CalcuateAndSetFee()
    {
        // calculate fee implementation
        SetFee(0);
        return this;
    }

    public TransactionBuilder CoinSelection()
    {
        // coin selection implementation
        // set the input that is chosen in the coin selection
        return this;
    }

    public Transaction Build()
    {
        var body = BuildConwayBody();
        var witnessSet = BuildPostAlonzoWitnessSet();
        return new PostMaryTransaction(body, witnessSet, new CborBool(true), new CborNullable<AuxiliaryData>(auxiliaryData));
    }


    private ConwayTransactionBody BuildConwayBody()
    {

        var inputList = new CborDefListWithTag<TransactionInput>(inputs);

        var outputList = new CborDefList<TransactionOutput>(outputs);

        CborMaybeIndefList<Certificate>? certList = certificates != null ?
            new CborDefList<Certificate>(certificates) : null;

        CborDefListWithTag<TransactionInput>? collateralList = collateral != null ?
            new CborDefListWithTag<TransactionInput>(collateral) : null;

        CborMaybeIndefList<CborBytes>? requiredSignersList = requiredSigners != null ?
            new CborDefList<CborBytes>(requiredSigners) : null;

        CborDefListWithTag<TransactionInput>? referenceInputsList = referenceInputs != null ?
            new CborDefListWithTag<TransactionInput>(referenceInputs) : null;

        CborMaybeIndefList<ProposalProcedure>? proposalsList = proposalProcedures != null ?
            new CborDefList<ProposalProcedure>(proposalProcedures) : null;

        return new ConwayTransactionBody(
            inputList,
            outputList,
            fee,
            ttl,
            certList,
            withdrawals,
            auxiliaryDataHash,
            validityStart,
            mint,
            scriptDataHash,
            collateralList,
            requiredSignersList,
            networkId,
            collateralReturn,
            totalCollateral,
            referenceInputsList,
            votingProcedures,
            proposalsList,
            treasuryValue,
            donation
        );
    }

    private PostAlonzoTransactionWitnessSet BuildPostAlonzoWitnessSet()
    {
        CborMaybeIndefList<VKeyWitness>? vkeyList = vKeyWitnesses != null && vKeyWitnesses.Count > 0 ?
            new CborDefList<VKeyWitness>(vKeyWitnesses) : null;

        CborMaybeIndefList<NativeScript>? scriptList = nativeScripts != null && nativeScripts.Count > 0 ?
            new CborDefList<NativeScript>(nativeScripts) : null;

        CborMaybeIndefList<BootstrapWitness>? bootstrapList = bootstrapWitnesses != null && bootstrapWitnesses.Count > 0 ?
            new CborDefList<BootstrapWitness>(bootstrapWitnesses) : null;

        CborMaybeIndefList<CborBytes>? plutusV1List = plutusV1Scripts != null && plutusV1Scripts.Count > 0 ?
            new CborDefList<CborBytes>(plutusV1Scripts) : null;

        CborMaybeIndefList<PlutusData>? dataList = plutusData != null && plutusData.Count > 0 ?
            new CborDefList<PlutusData>(plutusData) : null;

        CborMaybeIndefList<CborBytes>? plutusV2List = plutusV2Scripts != null && plutusV2Scripts.Count > 0 ?
            new CborDefList<CborBytes>(plutusV2Scripts) : null;

        CborMaybeIndefList<CborBytes>? plutusV3List = plutusV3Scripts != null && plutusV3Scripts.Count > 0 ?
            new CborDefList<CborBytes>(plutusV3Scripts) : null;

        return new PostAlonzoTransactionWitnessSet(
            vkeyList,
            scriptList,
            bootstrapList,
            plutusV1List,
            dataList,
            redeemers,
            plutusV2List,
            plutusV3List
        );
    }

}