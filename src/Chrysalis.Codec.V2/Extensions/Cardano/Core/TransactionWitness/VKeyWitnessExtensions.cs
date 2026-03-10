using CVKeyWitness = Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.TransactionWitness;

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
    public static ReadOnlyMemory<byte> VKey(this CVKeyWitness self)
    {
        return self.VKey;
    }

    /// <summary>
    /// Gets the signature bytes.
    /// </summary>
    /// <param name="self">The VKey witness instance.</param>
    /// <returns>The signature bytes.</returns>
    public static ReadOnlyMemory<byte> Signature(this CVKeyWitness self)
    {
        return self.Signature;
    }
}
