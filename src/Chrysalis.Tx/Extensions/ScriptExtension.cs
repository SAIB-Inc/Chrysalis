using Chrysalis.Cbor.Types.Cardano.Core.Common;

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
}