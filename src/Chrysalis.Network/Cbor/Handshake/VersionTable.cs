using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>
/// Union type representing a table of supported protocol versions and their parameters, used during the Handshake mini-protocol.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record VersionTable : CborRecord;

/// <summary>
/// A version table mapping node-to-client protocol versions to their corresponding parameters.
/// </summary>
/// <param name="Value">The dictionary mapping each node-to-client version to its negotiation parameters.</param>
[CborSerializable]
public partial record N2CVersionTable(Dictionary<N2CVersion, N2CVersionData> Value) : VersionTable;

/// <summary>
/// A version table mapping node-to-node protocol versions to their corresponding parameters.
/// </summary>
/// <param name="Value">The dictionary mapping each node-to-node version to its negotiation parameters.</param>
[CborSerializable]
public partial record N2NVersionTable(Dictionary<N2NVersion, N2NVersionData> Value) : VersionTable;

/// <summary>
/// Factory methods for constructing commonly used Ouroboros Handshake version tables.
/// </summary>
public static class VersionTables
{
    /// <summary>
    /// Creates a node-to-client version table proposing versions 16 through 20.
    /// </summary>
    /// <param name="networkMagic">The network magic number (default is 2 for preview testnet).</param>
    /// <returns>A new <see cref="N2CVersionTable"/> with versions V16 through V20.</returns>
    public static N2CVersionTable N2cV10AndAbove(ulong networkMagic = 2) => new(new()
        {
            {N2CVersions.V16, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V17, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V18, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V19, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V20, new N2CVersionData(networkMagic, false)}
        });

    /// <summary>
    /// Creates a node-to-node version table proposing versions 11 through 14.
    /// </summary>
    /// <param name="networkMagic">The network magic number (default is 2 for preview testnet).</param>
    /// <returns>A new <see cref="N2NVersionTable"/> with versions V11 through V14.</returns>
    public static N2NVersionTable N2nV11AndAbove(ulong networkMagic = 2) => new(new()
        {
            {N2NVersions.V11, new N2NVersionData(networkMagic, false, 0, false)},
            {N2NVersions.V12, new N2NVersionData(networkMagic, false, 0, false)},
            {N2NVersions.V13, new N2NVersionData(networkMagic, false, 0, false)},
            {N2NVersions.V14, new N2NVersionData(networkMagic, false, 0, false)},
        });
}
