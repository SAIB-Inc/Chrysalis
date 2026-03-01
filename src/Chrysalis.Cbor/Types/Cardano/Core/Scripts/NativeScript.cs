using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Abstract base for Cardano native scripts used for multi-signature and time-lock policies.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record NativeScript : CborBase { }

/// <summary>
/// A native script requiring a signature from a specific public key hash.
/// </summary>
/// <param name="Tag">The native script type tag.</param>
/// <param name="AddrKeyHash">The address key hash that must sign the transaction.</param>
[CborSerializable]
[CborUnionCase(0)]
[CborList]
public partial record ScriptPubKey(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> AddrKeyHash
) : NativeScript;

/// <summary>
/// A native script requiring all sub-scripts to be satisfied.
/// </summary>
/// <param name="Tag">The native script type tag.</param>
/// <param name="Scripts">The list of sub-scripts that must all be satisfied.</param>
[CborSerializable]
[CborUnionCase(1)]
[CborList]
public partial record ScriptAll(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] CborMaybeIndefList<NativeScript>? Scripts
) : NativeScript;

/// <summary>
/// A native script requiring any one of the sub-scripts to be satisfied.
/// </summary>
/// <param name="Tag">The native script type tag.</param>
/// <param name="Scripts">The list of sub-scripts of which at least one must be satisfied.</param>
[CborSerializable]
[CborUnionCase(2)]
[CborList]
public partial record ScriptAny(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] CborMaybeIndefList<NativeScript>? Scripts
) : NativeScript;

/// <summary>
/// A native script requiring at least N of the sub-scripts to be satisfied.
/// </summary>
/// <param name="Tag">The native script type tag.</param>
/// <param name="N">The minimum number of sub-scripts that must be satisfied.</param>
/// <param name="Scripts">The list of sub-scripts.</param>
[CborSerializable]
[CborUnionCase(3)]
[CborList]
public partial record ScriptNOfK(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] int N,
    [CborOrder(2)] CborMaybeIndefList<NativeScript>? Scripts
) : NativeScript;

/// <summary>
/// A native script that is valid only after a specified slot.
/// </summary>
/// <param name="Tag">The native script type tag.</param>
/// <param name="Slot">The slot from which this script becomes valid.</param>
[CborSerializable]
[CborUnionCase(4)]
[CborList]
public partial record InvalidBefore(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Slot
) : NativeScript;

/// <summary>
/// A native script that is valid only before a specified slot.
/// </summary>
/// <param name="Tag">The native script type tag.</param>
/// <param name="Slot">The slot after which this script becomes invalid.</param>
[CborSerializable]
[CborUnionCase(5)]
[CborList]
public partial record InvalidHereafter(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Slot
) : NativeScript;
