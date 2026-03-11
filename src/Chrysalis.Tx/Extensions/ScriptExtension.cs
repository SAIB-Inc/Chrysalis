using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Serialization;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Wallet.Utils;

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
    public static int Version(this IScript script)
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
    /// Computes the script hash (blake2b-224 of version-prefixed script bytes).
    /// </summary>
    /// <param name="script">The script to hash.</param>
    /// <returns>The 28-byte script hash.</returns>
    public static byte[] Hash(this IScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        int version = script.Version();
        ReadOnlySpan<byte> scriptBytes = script switch
        {
            MultiSigScript ms => CborSerializer.Serialize(ms.NativeScript),
            _ => script.Bytes().Span
        };

        byte[] prefixed = new byte[1 + scriptBytes.Length];
        prefixed[0] = (byte)version;
        scriptBytes.CopyTo(prefixed.AsSpan(1));
        return HashUtil.Blake2b224(prefixed);
    }

    /// <summary>
    /// Computes the script hash and returns it as a lowercase hex string.
    /// </summary>
    /// <param name="script">The script to hash.</param>
    /// <returns>The 56-character hex-encoded script hash.</returns>
    public static string HashHex(this IScript script) =>
        Convert.ToHexStringLower(script.Hash());

    /// <summary>
    /// Computes the hash of a native script (blake2b-224 of 0x00 || cbor-serialized script).
    /// </summary>
    /// <param name="script">The native script to hash.</param>
    /// <returns>The 28-byte script hash.</returns>
    public static byte[] Hash(this INativeScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        byte[] cborBytes = CborSerializer.Serialize(script);
        byte[] prefixed = new byte[1 + cborBytes.Length];
        prefixed[0] = 0x00;
        cborBytes.CopyTo(prefixed, 1);
        return HashUtil.Blake2b224(prefixed);
    }

    /// <summary>
    /// Computes the native script hash and returns it as a lowercase hex string.
    /// </summary>
    /// <param name="script">The native script to hash.</param>
    /// <returns>The 56-character hex-encoded script hash.</returns>
    public static string HashHex(this INativeScript script) =>
        Convert.ToHexStringLower(script.Hash());

    /// <summary>
    /// Gets the raw bytes of a Plutus script.
    /// </summary>
    /// <param name="script">The script to extract bytes from.</param>
    /// <returns>The script bytes.</returns>
    public static ReadOnlyMemory<byte> Bytes(this IScript script)
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
    public static IScript ApplyParameters<T>(this IScript self, T parameter) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(parameter);

        if (self is MultiSigScript)
        {
            throw new NotSupportedException("MultiSig scripts do not support parameterization");
        }

        byte[] originalBytes = self.Bytes().ToArray();
        byte[] parameterCbor = CborSerializer.Serialize(parameter);
        byte[] parameterizedBytes = ScriptApplicator.ApplyParameter(originalBytes, parameterCbor);

        return self switch
        {
            PlutusV1Script v1 => PlutusV1Script.Create(v1.Tag, parameterizedBytes),
            PlutusV2Script v2 => PlutusV2Script.Create(v2.Tag, parameterizedBytes),
            PlutusV3Script v3 => PlutusV3Script.Create(v3.Tag, parameterizedBytes),
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
    public static IScript ApplyParameters<T>(this IScript self, List<T> parameters) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(parameters);

        IScript current = self;
        foreach (T parameter in parameters)
        {
            current = current.ApplyParameters(parameter);
        }
        return current;
    }
}
