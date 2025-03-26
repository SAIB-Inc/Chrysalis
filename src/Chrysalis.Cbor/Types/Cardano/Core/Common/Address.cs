using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
public partial record Address(byte[] Value) : CborBase, ICborPreserveRaw;
