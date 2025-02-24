using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor;

public static class N2NVersions
{
    public static N2NVersion V7 => new V7(7);
    public static N2NVersion V8 => new V8(8);
    public static N2NVersion V9 => new V9(9);
    public static N2NVersion V10 => new V10(10);
    public static N2NVersion V11 => new V11(11);
    public static N2NVersion V12 => new V12(12);
    public static N2NVersion V13 => new V13(13);
    public static N2NVersion V14 => new V14(14);
}

[CborConverter(typeof(UnionConverter))]
public record N2NVersion : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 7)]
public record V7(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 8)]
public record V8(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 9)]
public record V9(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 10)]
public record V10(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 11)]
public record V11(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 12)]
public record V12(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 13)]
public record V13(int Value) : N2NVersion;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 14)]
public record V14(int Value) : N2NVersion;