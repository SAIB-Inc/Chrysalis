using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

[CborSerializable]
[CborUnion]
public partial record VersionTable : CborBase;

[CborSerializable]
public partial record N2CVersionTable(Dictionary<N2CVersion, N2CVersionData> Value) : VersionTable;

[CborSerializable]
public partial record N2NVersionTable(Dictionary<N2NVersion, N2NVersionData> Value) : VersionTable;

public class VersionTables
{
    public static N2CVersionTable N2C_V10_AND_ABOVE(ulong networkMagic = 2) =>
        new(new()
        {
            {N2CVersions.V16, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V17, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V18, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V19, new N2CVersionData(networkMagic, false)},
            {N2CVersions.V20, new N2CVersionData(networkMagic, false)}
        });

    public static N2NVersionTable N2N_V11_AND_ABOVE(ulong networkMagic = 2) =>
        new(new()
        {
            {N2NVersions.V11, new N2NVersionData(networkMagic, false, 0, false)},
            {N2NVersions.V12, new N2NVersionData(networkMagic, false, 0, false)},
            {N2NVersions.V13, new N2NVersionData(networkMagic, false, 0, false)},
            {N2NVersions.V14, new N2NVersionData(networkMagic, false, 0, false)},
        });
}