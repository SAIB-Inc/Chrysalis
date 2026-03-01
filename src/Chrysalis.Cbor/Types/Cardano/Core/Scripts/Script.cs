using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Abstract base for Cardano scripts (native scripts and Plutus scripts).
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Script : CborBase { }

/// <summary>
/// A multi-signature native script (Plutus version 0).
/// </summary>
/// <param name="Version">The script version tag, validated to equal 0.</param>
/// <param name="Script">The native script definition.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record MultiSigScript(
    [CborOrder(0)] int Version,
    [CborOrder(1)] NativeScript Script
) : Script;

/// <summary>
/// A Plutus V1 script containing compiled script bytes.
/// </summary>
/// <param name="Version">The script version tag, validated to equal 1.</param>
/// <param name="ScriptBytes">The compiled Plutus V1 script bytes.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record PlutusV1Script(
    [CborOrder(0)] int Version,
    [CborOrder(1)] ReadOnlyMemory<byte> ScriptBytes
) : Script;

/// <summary>
/// A Plutus V2 script containing compiled script bytes.
/// </summary>
/// <param name="Version">The script version tag, validated to equal 2.</param>
/// <param name="ScriptBytes">The compiled Plutus V2 script bytes.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record PlutusV2Script(
    [CborOrder(0)] int Version,
    [CborOrder(1)] ReadOnlyMemory<byte> ScriptBytes
) : Script;

/// <summary>
/// A Plutus V3 script containing compiled script bytes.
/// </summary>
/// <param name="Version">The script version tag, validated to equal 3.</param>
/// <param name="ScriptBytes">The compiled Plutus V3 script bytes.</param>
[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record PlutusV3Script(
    [CborOrder(0)] int Version,
    [CborOrder(1)] ReadOnlyMemory<byte> ScriptBytes
) : Script;
