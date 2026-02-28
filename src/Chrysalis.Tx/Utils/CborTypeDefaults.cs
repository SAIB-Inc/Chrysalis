using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Provides default CBOR type instances for transaction construction.
/// </summary>
public static class CborTypeDefaults
{
    /// <summary>
    /// Gets a default empty Conway transaction body.
    /// </summary>
    public static ConwayTransactionBody TransactionBody => new(
        new CborDefListWithTag<TransactionInput>([]),
        new CborDefList<TransactionOutput>([]), 0, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null
    );

    /// <summary>
    /// Gets a default empty post-Alonzo transaction witness set.
    /// </summary>
    public static PostAlonzoTransactionWitnessSet TransactionWitnessSet => new(null, null, null, null, null, null, null, null);

    /// <summary>
    /// Gets a default empty post-Alonzo auxiliary data map.
    /// </summary>
    public static PostAlonzoAuxiliaryDataMap AuxiliaryData => new(
        null,
        null,
        null,
        null,
        null
    );
}
