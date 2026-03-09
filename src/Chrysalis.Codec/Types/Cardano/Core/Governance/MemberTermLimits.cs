using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

/// <summary>
/// Maps constitutional committee member credentials to their term limit epochs.
/// </summary>
/// <param name="Value">Dictionary mapping credentials to their maximum epoch term limits.</param>
[CborSerializable]
public partial record MemberTermLimits(Dictionary<Credential, ulong> Value) : CborBase;
