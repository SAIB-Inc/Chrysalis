using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

/// <summary>
/// Represents the Cardano on-chain constitution, consisting of a metadata anchor and optional guardrails script.
/// </summary>
/// <param name="Anchor">The metadata anchor pointing to the constitution document.</param>
/// <param name="ScriptHash">The optional script hash for the constitution guardrails script.</param>
[CborSerializable]
[CborList]
public partial record Constitution(
    [CborOrder(0)] Anchor Anchor,
    [CborOrder(1)][CborNullable] ReadOnlyMemory<byte>? ScriptHash
) : CborBase;
