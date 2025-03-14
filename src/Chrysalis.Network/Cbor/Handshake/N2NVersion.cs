using Chrysalis.Cbor.Attributes;


using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Handshake;

public static class N2NVersions
{
    public static N2NVersion V7 => new(7);
    public static N2NVersion V8 => new(8);
    public static N2NVersion V9 => new(9);
    public static N2NVersion V10 => new(10);
    public static N2NVersion V11 => new(11);
    public static N2NVersion V12 => new(12);
    public static N2NVersion V13 => new(13);
    public static N2NVersion V14 => new(14);
}

[CborConverter(typeof(IntConverter))]
public partial record N2NVersion(int Value) : CborBase;