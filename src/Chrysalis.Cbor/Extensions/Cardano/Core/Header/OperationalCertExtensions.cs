using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

/// <summary>
/// Extension methods for <see cref="OperationalCert"/> to access certificate fields.
/// </summary>
public static class OperationalCertExtensions
{
    /// <summary>
    /// Gets the hot verification key from the operational certificate.
    /// </summary>
    /// <param name="self">The operational certificate instance.</param>
    /// <returns>The hot verification key bytes.</returns>
    public static ReadOnlyMemory<byte> HotVKey(this OperationalCert self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.HotVKey;
    }

    /// <summary>
    /// Gets the sequence number from the operational certificate.
    /// </summary>
    /// <param name="self">The operational certificate instance.</param>
    /// <returns>The sequence number.</returns>
    public static ulong SequenceNumber(this OperationalCert self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.SequenceNumber;
    }

    /// <summary>
    /// Gets the KES period from the operational certificate.
    /// </summary>
    /// <param name="self">The operational certificate instance.</param>
    /// <returns>The KES period.</returns>
    public static ulong KesPeriod(this OperationalCert self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.KesPeriod;
    }

    /// <summary>
    /// Gets the sigma (signature) from the operational certificate.
    /// </summary>
    /// <param name="self">The operational certificate instance.</param>
    /// <returns>The sigma bytes.</returns>
    public static ReadOnlyMemory<byte> Sigma(this OperationalCert self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Sigma;
    }
}
