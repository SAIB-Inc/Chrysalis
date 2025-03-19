using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborSerializable]
[CborUnion]
public abstract partial record DatumOption : CborBase<DatumOption>
{
}

[CborSerializable]
[CborList]
public partial record DatumHashOption(
    [CborOrder(0)] int Option,
    [CborOrder(1)] byte[] DatumHash
) : DatumOption;


[CborSerializable]
[CborList]
public partial record InlineDatumOption(
    [CborOrder(0)] int Option,
    [CborOrder(1)][CborSize(32)] byte[] Data
) : DatumOption;


