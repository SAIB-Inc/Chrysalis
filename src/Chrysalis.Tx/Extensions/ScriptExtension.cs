using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Plutus.VM.EvalTx;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for working with Cardano scripts.
/// </summary>
public static class ScriptExtension
{
    /// <summary>
    /// Gets the version number of a script (0=MultiSig, 1=PlutusV1, 2=PlutusV2, 3=PlutusV3).
    /// </summary>
    /// <param name="script">The script to check.</param>
    /// <returns>The script version number.</returns>
    public static int Version(this Script script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return script switch
        {
            MultiSigScript => 0,
            PlutusV1Script => 1,
            PlutusV2Script => 2,
            PlutusV3Script => 3,
            _ => throw new NotSupportedException($"Unsupported script type: {script.GetType()}")
        };
    }

    /// <summary>
    /// Gets the raw bytes of a Plutus script.
    /// </summary>
    /// <param name="script">The script to extract bytes from.</param>
    /// <returns>The script bytes.</returns>
    public static ReadOnlyMemory<byte> Bytes(this Script script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return script switch
        {
            PlutusV1Script plutusV1Script => plutusV1Script.ScriptBytes,
            PlutusV2Script plutusV2Script => plutusV2Script.ScriptBytes,
            PlutusV3Script plutusV3Script => plutusV3Script.ScriptBytes,
            _ => throw new NotSupportedException($"Unsupported script type: {script.GetType()}")
        };
    }

    /// <summary>
    /// Applies a single parameter to a Plutus script.
    /// </summary>
    /// <typeparam name="T">The parameter CBOR type.</typeparam>
    /// <param name="self">The script to parameterize.</param>
    /// <param name="parameter">The parameter to apply.</param>
    /// <returns>A new script with the parameter applied.</returns>
    public static Script ApplyParameters<T>(this Script self, T parameter) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(parameter);

        if (self is MultiSigScript)
        {
            throw new NotSupportedException("MultiSig scripts do not support parameterization");
        }

        byte[] originalBytes = self.Bytes().ToArray();
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

    /// <summary>
    /// Applies a list of parameters sequentially to a Plutus script.
    /// </summary>
    /// <typeparam name="T">The parameter CBOR type.</typeparam>
    /// <param name="self">The script to parameterize.</param>
    /// <param name="parameters">The parameters to apply in order.</param>
    /// <returns>A new script with all parameters applied.</returns>
    public static Script ApplyParameters<T>(this Script self, List<T> parameters) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(parameters);

        Script current = self;
        foreach (T parameter in parameters)
        {
            current = current.ApplyParameters(parameter);
        }
        return current;
    }
}
