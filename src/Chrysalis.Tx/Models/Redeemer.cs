using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Builders;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Identifies the purpose of a redeemer in a Plutus script transaction.
/// </summary>
public enum RedeemerTag
{
    /// <summary>Redeemer for spending a UTxO.</summary>
    Spend = 0,
    /// <summary>Redeemer for minting tokens.</summary>
    Mint = 1,
    /// <summary>Redeemer for certificate operations.</summary>
    Cert = 2,
    /// <summary>Redeemer for reward withdrawals.</summary>
    Reward = 3,
    /// <summary>Redeemer for voting operations.</summary>
    Vote = 4,
    /// <summary>Redeemer for governance proposals.</summary>
    Propose = 5,
}

/// <summary>
/// Delegate for building redeemer data from context.
/// </summary>
/// <typeparam name="TContext">The transaction parameter type.</typeparam>
/// <typeparam name="TData">The redeemer data type.</typeparam>
/// <param name="mapping">The input/output mapping.</param>
/// <param name="context">The transaction context parameter.</param>
/// <param name="transactionBuilder">The transaction builder instance.</param>
/// <returns>The constructed redeemer data.</returns>
public delegate TData RedeemerDataBuilder<TContext, TData>(InputOutputMapping mapping, TContext context, TransactionBuilder transactionBuilder) where TData : CborBase;

/// <summary>
/// Represents a typed redeemer with its tag, index, data, and execution units.
/// </summary>
/// <typeparam name="T">The redeemer data type.</typeparam>
/// <param name="Tag">The redeemer tag identifying its purpose.</param>
/// <param name="Index">The index into the sorted inputs/mints/etc.</param>
/// <param name="Data">The redeemer data payload.</param>
/// <param name="ExUnits">The execution units budget.</param>
public record Redeemer<T>(RedeemerTag Tag, ulong Index, T Data, ExUnits ExUnits) where T : CborBase;
