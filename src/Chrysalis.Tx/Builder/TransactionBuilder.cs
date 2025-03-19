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
    private List<TransactionInput>? inputs;
    private List<TransactionOutput>? outputs;
    public Address? changeAddress;
    private List<Utxo>? availableUtxos;
    private CborUlong? fee;
    private CborUlong? ttl;
    private CborUlong? validityStart;
    private List<Certificate>? certificates;
    private Withdrawals? withdrawals;
    private Update? update;
    private CborBytes? auxiliaryDataHash;
    private MultiAssetMint? mint;
    private CborBytes? scriptDataHash;
    private List<TransactionInput>? collateral;
    private List<CborBytes>? requiredSigners;
    private CborInt? networkId;
    private TransactionOutput? collateralReturn;
    private CborUlong? totalCollateral;
    private List<TransactionInput>? referenceInputs;
    private VotingProcedures? votingProcedures;
    private List<ProposalProcedure>? proposalProcedures;
    private CborUlong? treasuryValue;
    private CborUlong? donation;
    private TransactionWitnessSet? witnessSet;
    private List<VKeyWitness>? vKeyWitnesses;
    private List<NativeScript>? nativeScripts;
    private List<BootstrapWitness>? bootstrapWitnesses;
    private List<CborBytes>? plutusV1Scripts;
    private List<CborBytes>? plutusV2Scripts;
    private List<CborBytes>? plutusV3Scripts;
    private List<PlutusData>? plutusData;
    private Redeemers? redeemers;
    private AuxiliaryData? auxiliaryData;


    public static TransactionBuilder Create()
    {
        return new TransactionBuilder();
    }

    public TransactionBuilder SetNetworkId(int networkId)
    {
        this.networkId = new CborInt(networkId);
        return this;
    }

    public TransactionBuilder AddInput(TransactionInput input)
    {
        inputs ??= [];
        inputs.Add(input);
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
        outputs ??= [];
        outputs.Add(output);
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

    public TransactionBuilder AddCollateral(TransactionInput input)
    {
        collateral ??= [];
        collateral.Add(input);
        return this;
    }


    public TransactionBuilder AddCertificate(Certificate certificate)
    {
        certificates ??= [];
        certificates.Add(certificate);
        return this;
    }

    public TransactionBuilder AddUpdate(Update update)
    {
        this.update = update;
        return this;
    }
    public TransactionBuilder SetCollateralReturn(TransactionOutput output)
    {
        collateralReturn = output;
        return this;
    }

    public TransactionBuilder SetTotalCollateral(ulong total)
    {
        totalCollateral = new CborUlong(total);
        return this;
    }

    public TransactionBuilder AddRequiredSigner(CborBytes signer)
    {
        requiredSigners ??= [];
        requiredSigners.Add(signer);
        return this;
    }

    public TransactionBuilder AddVotingProcedure(VotingProcedures votingProcedures)
    {
        this.votingProcedures = votingProcedures;
        return this;
    }

    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        proposalProcedures ??= [];
        proposalProcedures.Add(proposal);
        return this;
    }

    public TransactionBuilder SetTreasuryValue(ulong value)
    {
        treasuryValue = new CborUlong(value);
        return this;
    }

    public TransactionBuilder SetDonation(ulong value)
    {
        donation = new CborUlong(value);
        return this;
    }

    public TransactionBuilder AddMint(MultiAssetMint mint)
    {
        this.mint = mint;
        return this;
    }

    public TransactionBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        this.withdrawals = withdrawals;
        return this;
    }

    public TransactionBuilder SetRedeemer(Redeemers redeemers)
    {
        this.redeemers = redeemers;
        return this;
    }

    public TransactionBuilder SetAuxiliaryData(AuxiliaryData auxiliaryData)
    {
        this.auxiliaryData = auxiliaryData;
        return this;
    }

    public TransactionBuilder AddVKeyWitness(VKeyWitness witness)
    {
        vKeyWitnesses ??= [];
        vKeyWitnesses.Add(witness);
        return this;
    }

    public TransactionBuilder AddNativeScript(NativeScript script)
    {
        nativeScripts ??= [];
        nativeScripts.Add(script);
        return this;
    }

    public TransactionBuilder AddPlutusV1Script(CborBytes script)
    {
        plutusV1Scripts ??= [];
        plutusV1Scripts.Add(script);
        return this;
    }

    public TransactionBuilder AddPlutusV2Script(CborBytes script)
    {
        plutusV2Scripts ??= [];
        plutusV2Scripts.Add(script);
        return this;
    }

    public TransactionBuilder AddPlutusV3Script(CborBytes script)
    {
        plutusV3Scripts ??= [];
        plutusV3Scripts.Add(script);
        return this;
    }

    public TransactionBuilder AddPlutusData(PlutusData data)
    {
        plutusData ??= [];
        plutusData.Add(data);
        return this;
    }

    public TransactionBuilder SetAuxiliaryDataHash(CborBytes hash)
    {
        auxiliaryDataHash = hash;
        return this;
    }

    public TransactionBuilder SetScriptDataHash(CborBytes hash)
    {
        scriptDataHash = hash;
        return this;
    }

    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        bootstrapWitnesses ??= [];
        bootstrapWitnesses.Add(witness);
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


    public TransactionBuilder SetValidityIntervalEnd(ulong end)
    {
        ttl = new CborUlong(end);
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

        var inputList = new CborDefListWithTag<TransactionInput>(inputs ?? []);

        var outputList = new CborDefList<TransactionOutput>(outputs ?? []);

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
            fee ?? new CborUlong(0),
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