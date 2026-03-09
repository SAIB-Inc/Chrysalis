using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

/// <summary>
/// Maps reward accounts to their withdrawal amounts in lovelace.
/// </summary>
/// <param name="Value">Dictionary mapping reward accounts to withdrawal amounts.</param>
[CborSerializable]
public partial record Withdrawals(Dictionary<RewardAccount, ulong> Value) : CborBase;
