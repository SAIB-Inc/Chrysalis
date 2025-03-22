using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborSerializable]
public partial record Address(byte[] Value) : CborBase<Address>, ICborPreserveRaw;
