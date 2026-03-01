using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Types.Cardano.Core;

/// <summary>
/// Represents a Cardano block across all supported eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Block : CborBase { }

/// <summary>
/// Represents an Alonzo-era compatible block containing transactions, witnesses, and auxiliary data.
/// </summary>
/// <param name="Header">The block header.</param>
/// <param name="TransactionBodies">The list of Alonzo transaction bodies in this block.</param>
/// <param name="TransactionWitnessSets">The list of Alonzo transaction witness sets.</param>
/// <param name="AuxiliaryDataSet">The auxiliary data set for transactions in this block.</param>
/// <param name="InvalidTransactions">The optional list of invalid transaction indices.</param>
[CborSerializable]
[CborList]
public partial record AlonzoCompatibleBlock(
[CborOrder(0)] BlockHeader Header,
[CborOrder(1)] CborMaybeIndefList<AlonzoTransactionBody> TransactionBodies,
[CborOrder(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
[CborOrder(3)] AuxiliaryDataSet AuxiliaryDataSet,
[CborOrder(4)] CborMaybeIndefList<int>? InvalidTransactions
) : Block, ICborPreserveRaw;

/// <summary>
/// Represents a Babbage-era block containing transactions, witnesses, and auxiliary data.
/// </summary>
/// <param name="Header">The block header.</param>
/// <param name="TransactionBodies">The list of Babbage transaction bodies in this block.</param>
/// <param name="TransactionWitnessSets">The list of post-Alonzo transaction witness sets.</param>
/// <param name="AuxiliaryDataSet">The auxiliary data set for transactions in this block.</param>
/// <param name="InvalidTransactions">The optional list of invalid transaction indices.</param>
[CborSerializable]
[CborList]
public partial record BabbageBlock(
    [CborOrder(0)] BlockHeader Header,
    [CborOrder(1)] CborMaybeIndefList<BabbageTransactionBody> TransactionBodies,
    [CborOrder(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborOrder(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborOrder(4)] CborMaybeIndefList<int>? InvalidTransactions
) : Block, ICborPreserveRaw;

/// <summary>
/// Represents a Conway-era block containing transactions, witnesses, and auxiliary data.
/// </summary>
/// <param name="Header">The block header.</param>
/// <param name="TransactionBodies">The list of Conway transaction bodies in this block.</param>
/// <param name="TransactionWitnessSets">The list of post-Alonzo transaction witness sets.</param>
/// <param name="AuxiliaryDataSet">The auxiliary data set for transactions in this block.</param>
/// <param name="InvalidTransactions">The optional list of invalid transaction indices.</param>
[CborSerializable]
[CborList]
public partial record ConwayBlock(
    [CborOrder(0)] BlockHeader Header,
    [CborOrder(1)] CborMaybeIndefList<ConwayTransactionBody> TransactionBodies,
    [CborOrder(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborOrder(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborOrder(4)] CborMaybeIndefList<int>? InvalidTransactions
) : Block, ICborPreserveRaw;
