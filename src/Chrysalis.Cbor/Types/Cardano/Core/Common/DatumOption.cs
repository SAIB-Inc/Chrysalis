using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record DatumOption : CborBase { }

[CborSerializable]
[CborList]
public partial record DatumHashOption(
    [CborOrder(0)] int Option,
    [CborOrder(1)] byte[] DatumHash
) : DatumOption, ICborPreserveRaw;


[CborSerializable]
[CborList]
public partial record InlineDatumOption(
    [CborOrder(0)] int Option,
    [CborOrder(1)] CborEncodedValue Data
) : DatumOption, ICborPreserveRaw;

