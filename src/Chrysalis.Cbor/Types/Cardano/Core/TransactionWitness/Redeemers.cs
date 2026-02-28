using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

/// <summary>
/// Abstract base for redeemer collections, supporting both list and map encodings.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Redeemers : CborBase { }

/// <summary>
/// A list-based encoding of redeemer entries (used in Alonzo/Babbage eras).
/// </summary>
/// <param name="Value">The list of redeemer entries.</param>
[CborSerializable]
public partial record RedeemerList(List<RedeemerEntry> Value) : Redeemers, ICborPreserveRaw;

/// <summary>
/// A map-based encoding of redeemers keyed by tag and index (used in Conway era).
/// </summary>
/// <param name="Value">The dictionary mapping redeemer keys to redeemer values.</param>
[CborSerializable]
public partial record RedeemerMap(Dictionary<RedeemerKey, RedeemerValue> Value) : Redeemers, ICborPreserveRaw;

/// <summary>
/// A single redeemer entry containing the tag, index, Plutus data, and execution units.
/// </summary>
/// <param name="Tag">The redeemer tag (0 = spend, 1 = mint, 2 = cert, 3 = reward).</param>
/// <param name="Index">The index of the corresponding script input.</param>
/// <param name="Data">The Plutus data passed to the script.</param>
/// <param name="ExUnits">The execution units budget for this redeemer.</param>
[CborSerializable]
[CborList]
public partial record RedeemerEntry(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index,
    [CborOrder(2)] PlutusData Data,
    [CborOrder(3)] ExUnits ExUnits
) : CborBase, ICborPreserveRaw;

/// <summary>
/// A redeemer key identifying a specific script purpose by tag and index.
/// </summary>
/// <param name="Tag">The redeemer tag (0 = spend, 1 = mint, 2 = cert, 3 = reward).</param>
/// <param name="Index">The index of the corresponding script input.</param>
[CborSerializable]
[CborList]
public partial record RedeemerKey(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index
) : CborBase, ICborPreserveRaw;

/// <summary>
/// A redeemer value containing the Plutus data and execution unit budget.
/// </summary>
/// <param name="Data">The Plutus data passed to the script.</param>
/// <param name="ExUnits">The execution units budget for this redeemer.</param>
[CborSerializable]
[CborList]
public partial record RedeemerValue(
    [CborOrder(0)] PlutusData Data,
    [CborOrder(1)] ExUnits ExUnits
) : CborBase, ICborPreserveRaw;
