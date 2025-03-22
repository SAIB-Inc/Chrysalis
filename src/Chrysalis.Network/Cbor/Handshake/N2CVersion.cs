using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

public static class N2CVersions
{
    public static N2CVersion V16 => new(32784);
    public static N2CVersion V17 => new(32785);
    public static N2CVersion V18 => new(32786);
    public static N2CVersion V19 => new(32787);
    public static N2CVersion V20 => new(32788);
}

[CborSerializable]
public partial record N2CVersion(int Value) : CborBase<N2CVersion>;

