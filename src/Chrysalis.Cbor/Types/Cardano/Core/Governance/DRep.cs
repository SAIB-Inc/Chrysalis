using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

/// <summary>
/// Abstract base for delegated representative (DRep) credential types.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record DRep : CborBase { }

/// <summary>
/// A DRep identified by an address key hash.
/// </summary>
/// <param name="Tag">The DRep type tag.</param>
/// <param name="AddrKeyHash">The address key hash identifying the DRep.</param>
[CborSerializable]
[CborList]
public partial record DRepAddrKeyHash(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] AddrKeyHash
) : DRep;

/// <summary>
/// A DRep identified by a script hash.
/// </summary>
/// <param name="Tag">The DRep type tag.</param>
/// <param name="ScriptHash">The script hash identifying the DRep.</param>
[CborSerializable]
[CborList]
public partial record DRepScriptHash(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] ScriptHash
) : DRep;

/// <summary>
/// A DRep option representing an explicit abstention from voting.
/// </summary>
/// <param name="Tag">The DRep type tag.</param>
[CborSerializable]
[CborList]
public partial record Abstain(
    [CborOrder(0)] int Tag
) : DRep;

/// <summary>
/// A DRep option representing a vote of no confidence.
/// </summary>
/// <param name="Tag">The DRep type tag.</param>
[CborSerializable]
[CborList]
public partial record DRepNoConfidence(
    [CborOrder(0)] int Tag
) : DRep;
