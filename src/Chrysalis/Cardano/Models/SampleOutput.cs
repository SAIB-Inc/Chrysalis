using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(CborBytes), typeof(CborInt)])]
public record SampleDatum : ICbor;