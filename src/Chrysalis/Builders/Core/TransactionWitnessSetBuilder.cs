using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;

namespace Chrysalis.Builders.Core;

public class TransactionWitnessSetBuilder : BuilderBase<TransactionWitnessSet>
{
    /// <summary>
    ///   Add a VKeyWitness to the TransactionWitnessSet.
    /// </summary>
    /// <param name="vKeyWitness"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddVKeyWitness(VKeyWitness vKeyWitness)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///  Add a VKeyWitnessSet to the TransactionWitnessSet.
    ///  </summary>
    ///  <param name="vKeyWitnessSet"></param>
    ///  <exception cref="NotImplementedException"></exception>
    public void AddVKeyWitnessSet(params VKeyWitness[] vKeyWitnessSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a NativeScript to the TransactionWitnessSet.
    /// </summary>
    /// <param name="nativeScript"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddNativeScript(NativeScript nativeScript)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a NativeScriptSet to the TransactionWitnessSet.
    /// </summary>
    /// <param name="nativeScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddNativeScriptSet(params NativeScript[] nativeScriptSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a BootstrapWitness to the TransactionWitnessSet.
    /// </summary>
    /// <param name="bootstrapWitness"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddBootstrapWitness(BootstrapWitness bootstrapWitness)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a BootstrapWitnessSet to the TransactionWitnessSet.
    /// </summary>
    /// <param name="bootstrapWitnessSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddBootstrapWitnessSet(params BootstrapWitness[] bootstrapWitnessSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusV1Script to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusV1Script"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV1Script(CborBytes plutusV1Script)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusV1ScriptSet to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusV1ScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV1ScriptSet(params CborBytes[] plutusV1ScriptSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusData to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusData"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusData(PlutusData plutusData)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusDataSet to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusDataSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusDataSet(params PlutusData[] plutusDataSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a Redeemers to the TransactionWitnessSet.
    /// </summary>
    /// <param name="redeemers"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddRedeemers(Redeemers redeemers)
    {
        // Notes: I think we should create a single redeeemer type
        // and then create a single AddRedeemer method that takes
        // a Redeemer type as a parameter. 
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusV2Script to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusV2Script"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV2Script(CborBytes plutusV2Script)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusV2ScriptSet to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusV2ScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV2ScriptSet(params CborBytes[] plutusV2ScriptSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a PlutusV3Script to the TransactionWitnessSet.
    /// </summary>
    /// <param name="plutusV3Script"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV3Script(CborBytes plutusV3Script)
    {
        throw new NotImplementedException();
    }

    public override TransactionWitnessSet Build()
    {
        // I guess in this layer we dont build helpers for signature,
        // we just create a high level builder that takes the signature
        throw new NotImplementedException();
    }
}