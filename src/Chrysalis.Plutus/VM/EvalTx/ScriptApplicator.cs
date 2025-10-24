using Chrysalis.Plutus.VM.Interop;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Plutus.VM.EvalTx;

public static class ScriptApplicator
{
    public static string ApplyParameters(string scriptHex, PlutusData parameters)
    {
        byte[] scriptBytes = Convert.FromHexString(scriptHex);
        byte[] result = ApplyParameters(scriptBytes, parameters);
        return Convert.ToHexString(result);
    }

    public static byte[] ApplyParameters(byte[] scriptBytes, PlutusData parameters)
    {
        PlutusList parametersList = new(new CborIndefList<PlutusData>([parameters]));
        byte[] parametersCbor = CborSerializer.Serialize(parametersList);
        
        return ApplyParametersFromCbor(scriptBytes, parametersCbor);
    }

    public static byte[] ApplyParametersFromCbor(byte[] scriptBytes, byte[] parametersCbor)
    {
        unsafe
        {
            byte* resultPtr = NativeMethods.ApplyParamsToScriptRaw(
                scriptBytes,
                (nuint)scriptBytes.Length,
                parametersCbor,
                (nuint)parametersCbor.Length,
                out nuint resultLength
            );

            if (resultPtr == null || resultLength == 0)
            {
                throw new InvalidOperationException("Failed to apply parameters to script. The script or parameters may be invalid.");
            }

            try
            {
                byte[] result = new byte[resultLength];
                fixed (byte* destPtr = result)
                {
                    Buffer.MemoryCopy(resultPtr, destPtr, (long)resultLength, (long)resultLength);
                }
                return result;
            }
            finally
            {
                NativeMethods.FreeScriptBytes((IntPtr)resultPtr, resultLength);
            }
        }
    }

}