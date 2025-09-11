using Chrysalis.Plutus.VM.Interop;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Plutus.VM.EvalTx;

public static class ScriptApplicator
{
    /// <summary>
    /// Apply parameters to a Plutus script from hex-encoded inputs.
    /// </summary>
    /// <param name="scriptHex">Hex-encoded CBOR script bytes</param>
    /// <param name="parameters">List of PlutusData parameters to apply</param>
    /// <returns>Hex-encoded parameterized script</returns>
    public static string ApplyParameters(string scriptHex, PlutusData parameters)
    {
        byte[] scriptBytes = Convert.FromHexString(scriptHex);
        byte[] result = ApplyParameters(scriptBytes, parameters);
        return Convert.ToHexString(result);
    }

    /// <summary>
    /// Apply parameters to a Plutus script from byte arrays.
    /// </summary>
    /// <param name="scriptBytes">CBOR-encoded script bytes</param>
    /// <param name="parameters">List of PlutusData parameters to apply</param>
    /// <returns>CBOR-encoded parameterized script bytes</returns>
    public static byte[] ApplyParameters(byte[] scriptBytes, PlutusData parameters)
    {
        // Wrap the single parameter in a PlutusList (array) as expected by UPLC
        var parametersList = new PlutusList(new CborIndefList<PlutusData>([parameters]));
        byte[] parametersCbor = CborSerializer.Serialize(parametersList);
        
        return ApplyParametersFromCbor(scriptBytes, parametersCbor);
    }

    /// <summary>
    /// Apply parameters to a Plutus script where parameters are already CBOR-encoded.
    /// </summary>
    /// <param name="scriptBytes">CBOR-encoded script bytes</param>
    /// <param name="parametersCbor">CBOR-encoded PlutusData array of parameters</param>
    /// <returns>CBOR-encoded parameterized script bytes</returns>
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
                // Copy the result to managed memory
                byte[] result = new byte[resultLength];
                fixed (byte* destPtr = result)
                {
                    Buffer.MemoryCopy(resultPtr, destPtr, (long)resultLength, (long)resultLength);
                }
                return result;
            }
            finally
            {
                // Free the native memory
                NativeMethods.FreeScriptBytes((IntPtr)resultPtr, resultLength);
            }
        }
    }

}