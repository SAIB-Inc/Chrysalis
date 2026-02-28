using CBootstrapWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.BootstrapWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

/// <summary>
/// Extension methods for <see cref="CBootstrapWitness"/> to access witness fields.
/// </summary>
public static class BootstrapWitnessExtensions
{
    /// <summary>
    /// Gets the public key bytes from the bootstrap witness.
    /// </summary>
    /// <param name="self">The bootstrap witness instance.</param>
    /// <returns>The public key bytes.</returns>
    public static byte[] PublicKey(this CBootstrapWitness self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.PublicKey;
    }

    /// <summary>
    /// Gets the signature bytes from the bootstrap witness.
    /// </summary>
    /// <param name="self">The bootstrap witness instance.</param>
    /// <returns>The signature bytes.</returns>
    public static byte[] Signature(this CBootstrapWitness self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Signature;
    }

    /// <summary>
    /// Gets the chain code bytes from the bootstrap witness.
    /// </summary>
    /// <param name="self">The bootstrap witness instance.</param>
    /// <returns>The chain code bytes.</returns>
    public static byte[] ChainCode(this CBootstrapWitness self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.ChainCode;
    }

    /// <summary>
    /// Gets the attributes bytes from the bootstrap witness.
    /// </summary>
    /// <param name="self">The bootstrap witness instance.</param>
    /// <returns>The attributes bytes.</returns>
    public static byte[] Attributes(this CBootstrapWitness self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Attributes;
    }
}
