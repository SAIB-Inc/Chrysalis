using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

/// <summary>
/// Extension methods for <see cref="VrfCert"/> to access proof and output.
/// </summary>
public static class VrfCertExtensions
{
    /// <summary>
    /// Gets the VRF proof bytes.
    /// </summary>
    /// <param name="self">The VRF certificate instance.</param>
    /// <returns>The proof bytes.</returns>
    public static ReadOnlyMemory<byte> Proof(this VrfCert self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Proof;
    }

    /// <summary>
    /// Gets the VRF output bytes.
    /// </summary>
    /// <param name="self">The VRF certificate instance.</param>
    /// <returns>The output bytes.</returns>
    public static ReadOnlyMemory<byte> Output(this VrfCert self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Output;
    }
}
