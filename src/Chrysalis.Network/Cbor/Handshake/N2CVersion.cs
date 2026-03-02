using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>
/// Provides well-known Cardano node-to-client protocol version constants.
/// </summary>
public static class N2CVersions
{
    /// <summary>Node-to-client protocol version 16 (wire value 32784).</summary>
    public static N2CVersion V16 => new(32784);
    /// <summary>Node-to-client protocol version 17 (wire value 32785).</summary>
    public static N2CVersion V17 => new(32785);
    /// <summary>Node-to-client protocol version 18 (wire value 32786).</summary>
    public static N2CVersion V18 => new(32786);
    /// <summary>Node-to-client protocol version 19 (wire value 32787).</summary>
    public static N2CVersion V19 => new(32787);
    /// <summary>Node-to-client protocol version 20 (wire value 32788).</summary>
    public static N2CVersion V20 => new(32788);
}

/// <summary>
/// Represents a Cardano node-to-client protocol version number used during the Handshake mini-protocol.
/// </summary>
/// <param name="Value">The numeric protocol version identifier (high bit set to distinguish from N2N versions).</param>
[CborSerializable]
public partial record N2CVersion(int Value) : CborBase;
