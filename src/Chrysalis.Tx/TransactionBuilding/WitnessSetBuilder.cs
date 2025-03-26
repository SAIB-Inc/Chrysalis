using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;


namespace Chrysalis.Tx.TransactionBuilding;


public class WitnessSetBuilder
{
    private readonly List<VKeyWitness> vKeyWitnesses = [];
    private readonly List<NativeScript> nativeScripts = [];
    private readonly List<BootstrapWitness> bootstrapWitnesses = [];
    private readonly List<byte[]> plutusV1Scripts = [];
    private readonly List<byte[]> plutusV2Scripts = [];
    private readonly List<byte[]> plutusV3Scripts = [];
    private readonly List<PlutusData> plutusData = [];
    public Redeemers? redeemers;

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

    public WitnessSetBuilder AddPlutusV1Script(byte[] script)
    {
        plutusV1Scripts.Add(script);
        return this;
    }

    public WitnessSetBuilder AddPlutusV2Script(byte[] script)
    {
        plutusV2Scripts.Add(script);
        return this;
    }

    public WitnessSetBuilder AddPlutusV3Script(byte[] script)
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
            plutusV1Scripts.Count != 0 ? new CborDefList<byte[]>(plutusV1Scripts) : null,
            plutusData.Any() ? new CborDefList<PlutusData>(plutusData) : null,
            redeemers,
            plutusV2Scripts.Any() ? new CborDefList<byte[]>(plutusV2Scripts) : null,
            plutusV3Scripts.Any() ? new CborDefList<byte[]>(plutusV3Scripts) : null
        );
    }
}