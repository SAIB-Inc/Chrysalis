using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>
/// Provides well-known Cardano node-to-node protocol version constants.
/// </summary>
public static class N2NVersions
{
    /// <summary>Node-to-node protocol version 7.</summary>
    public static N2NVersion V7 => new(7);
    /// <summary>Node-to-node protocol version 8.</summary>
    public static N2NVersion V8 => new(8);
    /// <summary>Node-to-node protocol version 9.</summary>
    public static N2NVersion V9 => new(9);
    /// <summary>Node-to-node protocol version 10.</summary>
    public static N2NVersion V10 => new(10);
    /// <summary>Node-to-node protocol version 11.</summary>
    public static N2NVersion V11 => new(11);
    /// <summary>Node-to-node protocol version 12.</summary>
    public static N2NVersion V12 => new(12);
    /// <summary>Node-to-node protocol version 13.</summary>
    public static N2NVersion V13 => new(13);
    /// <summary>Node-to-node protocol version 14.</summary>
    public static N2NVersion V14 => new(14);
}

/// <summary>
/// Represents a Cardano node-to-node protocol version number used during the Handshake mini-protocol.
/// </summary>
/// <param name="Value">The numeric protocol version identifier.</param>
[CborSerializable]
public partial record N2NVersion(int Value) : CborBase;
