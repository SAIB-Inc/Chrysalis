using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Provides default CBOR type instances for transaction construction.
/// </summary>
public static class CborTypeDefaults
{
    /// <summary>Gets the default empty <see cref="ConwayTransactionBody"/>.</summary>
    public static ConwayTransactionBody TransactionBody => default;

    /// <summary>Gets the default empty <see cref="PostAlonzoTransactionWitnessSet"/>.</summary>
    public static PostAlonzoTransactionWitnessSet TransactionWitnessSet => default;

    /// <summary>Gets the default empty <see cref="PostAlonzoAuxiliaryDataMap"/>.</summary>
    public static PostAlonzoAuxiliaryDataMap AuxiliaryData => default;
}
