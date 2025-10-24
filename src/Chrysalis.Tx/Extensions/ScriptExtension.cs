using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Plutus.VM.EvalTx;

namespace Chrysalis.Tx.Extensions;

public static class ScriptExtension
{
    public static int Version(this Script script)
    {
        return script switch
        {
            MultiSigScript => 0,
            PlutusV1Script => 1,
            PlutusV2Script => 2,
            PlutusV3Script => 3,
            _ => throw new NotSupportedException($"Unsupported script type: {script.GetType()}")
        };
    }

    public static byte[] Bytes(this Script script)
    {
        return script switch
        {
            PlutusV1Script plutusV1Script => plutusV1Script.ScriptBytes,
            PlutusV2Script plutusV2Script => plutusV2Script.ScriptBytes,
            PlutusV3Script plutusV3Script => plutusV3Script.ScriptBytes,
            _ => throw new NotSupportedException($"Unsupported script type: {script.GetType()}")
        };
    }

    public static Script ApplyParameters<T>(this Script self, T parameter) where T : CborBase
    {
        if (self is MultiSigScript)
        {
            throw new NotSupportedException("MultiSig scripts do not support parameterization");
        }

        byte[] originalBytes = self.Bytes();
        byte[] parameterCbor = CborSerializer.Serialize(parameter);
        PlutusData plutusParameter = CborSerializer.Deserialize<PlutusData>(parameterCbor);
        byte[] parameterizedBytes = ScriptApplicator.ApplyParameters(originalBytes, plutusParameter);
        
        return self switch
        {
            PlutusV1Script plutusV1 => plutusV1 with { ScriptBytes = parameterizedBytes },
            PlutusV2Script plutusV2 => plutusV2 with { ScriptBytes = parameterizedBytes },
            PlutusV3Script plutusV3 => plutusV3 with { ScriptBytes = parameterizedBytes },
            _ => throw new NotSupportedException($"Unsupported script type: {self.GetType()}")
        };
    }

    public static Script ApplyParameters<T>(this Script self, List<T> parameters) where T : CborBase
    {
        Script current = self;
        foreach (var parameter in parameters)
        {
            current = current.ApplyParameters(parameter);
        }
        return current;
    }

}