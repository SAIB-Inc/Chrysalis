using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;

namespace Chrysalis.Builders.Core;

public class AuxiliaryDataBuilder : BuilderBase<CborNullable<AuxiliaryData>>
{
    /// <summary>
    /// Add a metadata to the auxiliary data.
    /// </summary>
    /// <param name="metadata"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddMetadata(Metadata metadata)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a native script to the auxiliary data.
    /// </summary>
    /// <param name="nativeScript"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddNativeScript(NativeScript nativeScript)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a native script set to the auxiliary data.
    /// </summary>
    /// <param name="nativeScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddNativeScriptSet(params NativeScript[] nativeScriptSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a plutus v1 script to the auxiliary data.
    /// </summary>
    /// <param name="plutusScript"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV1Script(CborBytes plutusV1Script)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a plutus v1 script set to the auxiliary data.
    /// </summary>
    /// <param name="plutusV1ScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV1ScriptSet(params CborBytes[] plutusV1ScriptSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a plutus v2 script to the auxiliary data.
    /// </summary>
    /// <param name="plutusScript"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV2Script(CborBytes plutusV2Script)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a plutus v2 script set to the auxiliary data.
    /// </summary>
    /// <param name="plutusV2ScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV2ScriptSet(params CborBytes[] plutusV2ScriptSet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a plutus v3 script to the auxiliary data.
    /// </summary>
    /// <param name="plutusScript"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV3Script(CborBytes plutusV3Script)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a plutus v3 script set to the auxiliary data.
    /// </summary>
    /// <param name="plutusV3ScriptSet"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddPlutusV3ScriptSet(params CborBytes[] plutusV3ScriptSet)
    {
        throw new NotImplementedException();
    }

    public override CborNullable<AuxiliaryData> Build()
    {
        throw new NotImplementedException();
    }
}