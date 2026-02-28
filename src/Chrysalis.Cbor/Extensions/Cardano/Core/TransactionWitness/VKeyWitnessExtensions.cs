using CVKeyWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

/// <summary>
/// Extension methods for <see cref="CVKeyWitness"/> to access verification key and signature.
/// </summary>
public static class VKeyWitnessExtensions
{
    /// <summary>
    /// Gets the verification key bytes.
    /// </summary>
    /// <param name="self">The VKey witness instance.</param>
    /// <returns>The verification key bytes.</returns>
    public static byte[] VKey(this CVKeyWitness self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.VKey;
    }

    /// <summary>
    /// Gets the signature bytes.
    /// </summary>
    /// <param name="self">The VKey witness instance.</param>
    /// <returns>The signature bytes.</returns>
    public static byte[] Signature(this CVKeyWitness self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Signature;
    }
}
