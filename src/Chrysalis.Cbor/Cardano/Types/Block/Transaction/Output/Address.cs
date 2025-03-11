using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(BytesConverter))]
public partial record Address(byte[] Value) : CborBase;
