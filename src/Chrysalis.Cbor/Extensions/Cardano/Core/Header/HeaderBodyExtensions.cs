using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

/// <summary>
/// Extension methods for <see cref="BlockHeaderBody"/> to access header fields across eras.
/// </summary>
public static class HeaderBodyExtensions
{
    /// <summary>
    /// Gets the block number from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The block number.</returns>
    public static ulong BlockNumber(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the slot number from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The slot number.</returns>
    public static ulong Slot(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the previous block hash from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The previous block hash bytes.</returns>
    public static ReadOnlyMemory<byte>? PrevHash(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.PrevHash,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.PrevHash,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the issuer verification key from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The issuer verification key bytes.</returns>
    public static ReadOnlyMemory<byte> IssuerVKey(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.IssuerVKey,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the VRF verification key from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The VRF verification key bytes.</returns>
    public static ReadOnlyMemory<byte> VrfKey(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.VrfVKey,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.VrfVKey,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the VRF result certificate from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The VRF certificate.</returns>
    public static VrfCert VrfResult(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.VrfResult,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the block body size from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The block body size in bytes.</returns>
    public static ulong BlockBodySize(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockBodySize,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockBodySize,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the block body hash from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The block body hash bytes.</returns>
    public static ReadOnlyMemory<byte> BlockBodyHash(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockBodyHash,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockBodyHash,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the operational certificate sequence number from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The sequence number.</returns>
    public static ulong OperationalCertSequenceNumber(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.OperationalCertSequenceNumber,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert.SequenceNumber(),
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the operational certificate KES period from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The KES period.</returns>
    public static ulong OperationalCertKesPeriod(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.OperationalCertKesPeriod,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert.KesPeriod(),
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the operational certificate sigma (signature) from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The sigma bytes.</returns>
    public static ReadOnlyMemory<byte> OperationalCertSigma(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.OperationalCertSigma,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert.Sigma(),
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the operational certificate from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The operational certificate.</returns>
    public static OperationalCert OperationalCert(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the protocol version from the header body.
    /// </summary>
    /// <param name="self">The block header body instance.</param>
    /// <returns>The protocol version.</returns>
    public static ProtocolVersion ProtocolVersion(this BlockHeaderBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.ProtocolVersion,
            _ => throw new NotSupportedException()
        };
    }
}
