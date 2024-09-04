using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(DatumHashOption), typeof(InlineDatumOption)])]
public record DatumOption : ICbor;

[CborSerializable(CborType.List)]
public record DatumHashOption(
    [CborProperty(0)] CborInt Option,
    [CborProperty(1)] CborBytes DatumHash
) : DatumOption;

[CborSerializable(CborType.List)]
public record InlineDatumOption(
    [CborProperty(0)] CborInt Option,
    [CborProperty(1)] CborEncodedValue Data
) : DatumOption;