using Chrysalis.Plutus.VM.Interop;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Plutus.VM.EvalTx;

/// <summary>
/// Provides methods to apply parameters to Plutus scripts.
/// </summary>
public static class ScriptApplicator
{
    /// <summary>
    /// Applies a parameter to a Plutus script given as a hex string.
    /// </summary>
    /// <param name="scriptHex">The hex-encoded Plutus script.</param>
    /// <param name="parameters">The Plutus data parameter to apply.</param>
    /// <returns>The hex-encoded result script with the parameter applied.</returns>
    public static string ApplyParameters(string scriptHex, PlutusData parameters)
    {
        byte[] scriptBytes = Convert.FromHexString(scriptHex);
        byte[] result = ApplyParameters(scriptBytes, parameters);
        return Convert.ToHexString(result);
    }

    /// <summary>
    /// Applies a parameter to a Plutus script given as a byte array.
    /// </summary>
    /// <param name="scriptBytes">The CBOR-encoded Plutus script bytes.</param>
    /// <param name="parameters">The Plutus data parameter to apply.</param>
    /// <returns>The result script bytes with the parameter applied.</returns>
    public static byte[] ApplyParameters(byte[] scriptBytes, PlutusData parameters)
    {
        PlutusList parametersList = new(new CborIndefList<PlutusData>([parameters]));
        byte[] parametersCbor = CborSerializer.Serialize(parametersList);

        return ApplyParametersFromCbor(scriptBytes, parametersCbor);
    }

    /// <summary>
    /// Applies CBOR-encoded parameters to a CBOR-encoded Plutus script.
    /// </summary>
    /// <param name="scriptBytes">The CBOR-encoded Plutus script bytes.</param>
    /// <param name="parametersCbor">The CBOR-encoded parameters bytes.</param>
    /// <returns>The result script bytes with the parameters applied.</returns>
    public static byte[] ApplyParametersFromCbor(byte[] scriptBytes, byte[] parametersCbor)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(parametersCbor);

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
