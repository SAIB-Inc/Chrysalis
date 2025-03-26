using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
public partial record Withdrawals(Dictionary<RewardAccount, ulong> Value) : CborBase;