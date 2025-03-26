using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
public partial record MemberTermLimits(Dictionary<Credential, ulong> Value) : CborBase;