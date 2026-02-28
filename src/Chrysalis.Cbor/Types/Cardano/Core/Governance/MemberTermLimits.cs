using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Maps constitutional committee member credentials to their term limit epochs.
/// </summary>
/// <param name="Value">Dictionary mapping credentials to their maximum epoch term limits.</param>
[CborSerializable]
public partial record MemberTermLimits(Dictionary<Credential, ulong> Value) : CborBase;
