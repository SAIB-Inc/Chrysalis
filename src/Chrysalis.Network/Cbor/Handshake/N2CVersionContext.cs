namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>Connection-owned negotiation state; null context and not-yet-negotiated context are distinct.</summary>
internal sealed class N2CVersionContext
{
    private N2CVersion? _version;

    internal N2CVersionContext() { }


    internal N2CVersion? NegotiatedVersion => Volatile.Read(ref _version);

    internal void SetNegotiatedVersion(N2CVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        ValidateWireVersion(version, nameof(version));
        Volatile.Write(ref _version, new N2CVersion(version.Value));
    }

    internal static void ValidateWireVersion(N2CVersion version, string parameterName)
    {
        if (version.Value is < 32768 or > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName, version.Value, "A node-to-client wire version must be a Word16 with bit 15 set.");
        }
    }
}
