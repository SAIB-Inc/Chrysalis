using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// Abstract base for Cardano transactions across different eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Transaction : CborBase
{
}

/// <summary>
/// A Shelley-era transaction with a body, witnesses, and optional metadata.
/// </summary>
/// <param name="TransactionBody">The transaction body.</param>
/// <param name="TransactionWitnessSet">The transaction witnesses.</param>
/// <param name="TransactionMetadata">The optional transaction metadata.</param>
[CborSerializable]
[CborList]
public partial record ShelleyTransaction(
   [CborOrder(0)] TransactionBody TransactionBody,
   [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
   [CborOrder(2)][CborNullable] Metadata? TransactionMetadata
) : Transaction, ICborPreserveRaw;

/// <summary>
/// An Allegra-era transaction with a body, witnesses, and optional auxiliary data.
/// </summary>
/// <param name="TransactionBody">The transaction body.</param>
/// <param name="TransactionWitnessSet">The transaction witnesses.</param>
/// <param name="AuxiliaryData">The optional auxiliary data.</param>
[CborSerializable]
[CborList]
public partial record AllegraTransaction(
    [CborOrder(0)] TransactionBody TransactionBody,
    [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborOrder(2)][CborNullable] AuxiliaryData? AuxiliaryData
) : Transaction, ICborPreserveRaw;

/// <summary>
/// A post-Mary era transaction with a body, witnesses, validity flag, and optional auxiliary data.
/// </summary>
/// <param name="TransactionBody">The transaction body.</param>
/// <param name="TransactionWitnessSet">The transaction witnesses.</param>
/// <param name="IsValid">Whether the transaction passed phase-2 script validation.</param>
/// <param name="AuxiliaryData">The optional auxiliary data.</param>
[CborSerializable]
[CborList]
public partial record PostMaryTransaction(
    [CborOrder(0)] TransactionBody TransactionBody,
    [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborOrder(2)] bool IsValid,
    [CborOrder(3)][CborNullable] AuxiliaryData? AuxiliaryData
) : Transaction, ICborPreserveRaw;
