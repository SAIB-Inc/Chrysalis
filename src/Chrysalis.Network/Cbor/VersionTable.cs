using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor;

public static class Versions
{
    public static VersionNumber V7 => new Version7(7);
    public static VersionNumber V8 => new Version8(8);
    public static VersionNumber V9 => new Version9(9);
    public static VersionNumber V10 => new Version10(10);
    public static VersionNumber V11 => new Version11(11);
    public static VersionNumber V12 => new Version12(12);
    public static VersionNumber V13 => new Version13(13);
    public static VersionNumber V14 => new Version14(14);
}

[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public record VersionTable(Dictionary<VersionNumber, NodeToNodeVersionData> Value) : CborBase;

[CborConverter(typeof(UnionConverter))]
public record VersionNumber : CborBase;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 7)]
public record Version7(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 8)]
public record Version8(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 9)]
public record Version9(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 10)]
public record Version10(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 11)]
public record Version11(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 12)]
public record Version12(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 13)]
public record Version13(int Value) : VersionNumber;

[CborConverter(typeof(EnforcedIntConverter))]
[CborOptions(Index = 14)]
public record Version14(int Value) : VersionNumber;