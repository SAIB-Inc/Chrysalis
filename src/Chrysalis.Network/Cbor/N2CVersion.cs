using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor;

public static class N2CVersions
{
    public static N2CVersion V16 => new V16(32784);
    public static N2CVersion V17 => new V17(32785);
    public static N2CVersion V18 => new V18(32786);
    public static N2CVersion V19 => new V19(32787);
    public static N2CVersion V20 => new V20(32788);
}

[CborConverter(typeof(UnionConverter))]
public record N2CVersion : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 32784)]
public record V16(int Value) : N2CVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 32785)]
public record V17(int Value) : N2CVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 32786)]
public record V18(int Value) : N2CVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 32787)]
public record V19(int Value) : N2CVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 32788)]
public record V20(int Value) : N2CVersion;
