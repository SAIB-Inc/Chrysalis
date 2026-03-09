using Chrysalis.Plutus.Cbor;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.VM.EvalTx;

/// <summary>
/// Applies parameters to parameterized Plutus scripts.
/// Flat-decodes the script, wraps the program body in Apply(body, param), and re-encodes.
/// </summary>
public static class ScriptApplicator
{
    /// <summary>
    /// Applies a single CBOR-encoded PlutusData parameter to a Flat-encoded Plutus script.
    /// </summary>
    /// <param name="scriptBytes">The Flat-encoded script bytes.</param>
    /// <param name="parameterCbor">CBOR-encoded PlutusData parameter.</param>
    /// <returns>The Flat-encoded script with the parameter applied.</returns>
    public static byte[] ApplyParameter(byte[] scriptBytes, byte[] parameterCbor)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(parameterCbor);

        Program<DeBruijn> program = FlatDecoder.DecodeProgram(scriptBytes);
        PlutusData vmData = CborReader.DecodePlutusData(parameterCbor);
        Term<DeBruijn> applied = new ApplyTerm<DeBruijn>(
            program.Term,
            new ConstTerm<DeBruijn>(new DataConstant(vmData)));
        return FlatEncoder.EncodeProgram(new Program<DeBruijn>(program.Version, applied));
    }

    /// <summary>
    /// Applies multiple CBOR-encoded PlutusData parameters to a Flat-encoded Plutus script sequentially.
    /// </summary>
    /// <param name="scriptBytes">The Flat-encoded script bytes.</param>
    /// <param name="parametersCbor">CBOR-encoded PlutusData parameters to apply in order.</param>
    /// <returns>The Flat-encoded script with all parameters applied.</returns>
    public static byte[] ApplyParameters(byte[] scriptBytes, IReadOnlyList<byte[]> parametersCbor)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        ArgumentNullException.ThrowIfNull(parametersCbor);

        byte[] current = scriptBytes;
        foreach (byte[] param in parametersCbor)
        {
            current = ApplyParameter(current, param);
        }
        return current;
    }
}
