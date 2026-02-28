using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// Maps reward accounts to their withdrawal amounts in lovelace.
/// </summary>
/// <param name="Value">Dictionary mapping reward accounts to withdrawal amounts.</param>
[CborSerializable]
public partial record Withdrawals(Dictionary<RewardAccount, ulong> Value) : CborBase;
