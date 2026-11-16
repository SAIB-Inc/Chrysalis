namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>Connection-owned negotiation state; null context and not-yet-negotiated context are distinct.</summary>
internal sealed class N2CVersionContext
{
    private N2CVersion? _version;

    internal N2CVersionContext() { }

    internal N2CVersionContext(N2CVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        ValidateWireVersion(version, nameof(version));
        _version = new N2CVersion(version.Value);
    }

    internal N2CVersion? NegotiatedVersion => Volatile.Read(ref _version);

    internal void SetNegotiatedVersion(N2CVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        ValidateWireVersion(version, nameof(version));
        Volatile.Write(ref _version, new N2CVersion(version.Value));
    }

    internal void EnsureMinimum(int requiredWireVersion, string operation)
    {
        if (requiredWireVersion is < 32768 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredWireVersion), requiredWireVersion, "A minimum N2C wire version must belong to the node-to-client Word16 family.");
        }
        N2CVersion negotiated = NegotiatedVersion
            ?? throw new InvalidOperationException("Complete the N2C handshake before sending a version-dependent operation.");
        if (negotiated.Value < requiredWireVersion)
        {
            throw new N2CVersionNotSupportedException(operation, new N2CVersion(requiredWireVersion), negotiated);
        }
    }

    internal static void ValidateWireVersion(N2CVersion version, string parameterName)
    {
        if (version.Value is < 32768 or > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName, version.Value, "A node-to-client wire version must be a Word16 with bit 15 set.");
        }
    }
}
