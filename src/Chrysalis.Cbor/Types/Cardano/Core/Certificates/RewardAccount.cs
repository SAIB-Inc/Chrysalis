using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

[CborSerializable]
public partial record RewardAccount(byte[] Value) : CborBase;