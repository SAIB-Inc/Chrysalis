using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]

public partial record MemberTermLimits(Dictionary<Credential, ulong> Value) : CborBase<MemberTermLimits>;