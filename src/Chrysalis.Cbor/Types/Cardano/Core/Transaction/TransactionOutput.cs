using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// Abstract base for transaction outputs across different Cardano eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record TransactionOutput : CborBase { }

/// <summary>
/// An Alonzo-era transaction output using a list-based CBOR encoding.
/// </summary>
/// <param name="Address">The destination address.</param>
/// <param name="Amount">The output value (lovelace and optional multi-assets).</param>
/// <param name="DatumHash">The optional datum hash attached to the output.</param>
[CborSerializable]
[CborList]
public partial record AlonzoTransactionOutput(
    [CborOrder(0)] Address Address,
    [CborOrder(1)] Value Amount,
    [CborOrder(2)] byte[]? DatumHash
) : TransactionOutput, ICborPreserveRaw;

/// <summary>
/// A post-Alonzo transaction output using a map-based CBOR encoding with optional inline datum and script reference.
/// </summary>
/// <param name="Address">The destination address.</param>
/// <param name="Amount">The output value (lovelace and optional multi-assets).</param>
/// <param name="Datum">The optional datum (inline or hash).</param>
/// <param name="ScriptRef">The optional reference script attached to the output.</param>
[CborSerializable]
[CborMap]
public partial record PostAlonzoTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] DatumOption? Datum,
    [CborProperty(3)] CborEncodedValue? ScriptRef
) : TransactionOutput, ICborPreserveRaw;
