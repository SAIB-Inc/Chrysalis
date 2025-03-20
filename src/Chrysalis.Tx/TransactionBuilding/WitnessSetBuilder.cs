using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Tx.TransactionBuilding;


public class WitnessSetBuilder
{
    private readonly List<VKeyWitness> vKeyWitnesses = [];
    private readonly List<NativeScript> nativeScripts = [];
    private readonly List<BootstrapWitness> bootstrapWitnesses = [];
    private readonly List<CborBytes> plutusV1Scripts = [];
    private readonly List<CborBytes> plutusV2Scripts = [];
    private readonly List<CborBytes> plutusV3Scripts = [];
    private readonly List<PlutusData> plutusData = [];
    private Redeemers? redeemers;

    public WitnessSetBuilder AddVKeyWitness(VKeyWitness witness)
    {
        vKeyWitnesses.Add(witness);
        return this;
    }

    public WitnessSetBuilder AddNativeScript(NativeScript script)
    {
        nativeScripts.Add(script);
        return this;
    }

    public WitnessSetBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        bootstrapWitnesses.Add(witness);
        return this;
    }

    public WitnessSetBuilder AddPlutusV1Script(CborBytes script)
    {
        plutusV1Scripts.Add(script);
        return this;
    }

    public WitnessSetBuilder AddPlutusV2Script(CborBytes script)
    {
        plutusV2Scripts.Add(script);
        return this;
    }

    public WitnessSetBuilder AddPlutusV3Script(CborBytes script)
    {
        plutusV3Scripts.Add(script);
        return this;
    }

    public WitnessSetBuilder AddPlutusData(PlutusData data)
    {
        plutusData.Add(data);
        return this;
    }

    public WitnessSetBuilder SetRedeemers(Redeemers redeemers)
    {
        this.redeemers = redeemers;
        return this;
    }

    public PostAlonzoTransactionWitnessSet Build()
    {
        return new PostAlonzoTransactionWitnessSet(
            vKeyWitnesses.Count != 0 ? new CborDefList<VKeyWitness>(vKeyWitnesses) : null,
            nativeScripts.Count != 0 ? new CborDefList<NativeScript>(nativeScripts) : null,
            bootstrapWitnesses.Count != 0 ? new CborDefList<BootstrapWitness>(bootstrapWitnesses) : null,
            plutusV1Scripts.Count != 0 ? new CborDefList<CborBytes>(plutusV1Scripts) : null,
            plutusData.Any() ? new CborDefList<PlutusData>(plutusData) : null,
            redeemers,
            plutusV2Scripts.Any() ? new CborDefList<CborBytes>(plutusV2Scripts) : null,
            plutusV3Scripts.Any() ? new CborDefList<CborBytes>(plutusV3Scripts) : null
        );
    }
}