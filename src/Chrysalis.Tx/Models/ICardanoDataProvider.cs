using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Defines the interface for Cardano data providers that supply UTxOs, protocol parameters, and transaction submission.
/// </summary>
public interface ICardanoDataProvider
{
    /// <summary>
    /// Retrieves UTxOs for the specified addresses.
    /// </summary>
    /// <param name="address">The list of Bech32 addresses to query.</param>
    /// <returns>A list of resolved inputs for the addresses.</returns>
    Task<List<ResolvedInput>> GetUtxosAsync(List<string> address);

    /// <summary>
    /// Retrieves the current protocol parameters.
    /// </summary>
    /// <returns>The current protocol parameters.</returns>
    Task<ProtocolParams> GetParametersAsync();

    /// <summary>
    /// Submits a signed transaction to the network.
    /// </summary>
    /// <param name="tx">The signed transaction to submit.</param>
    /// <returns>The transaction hash on successful submission.</returns>
    Task<string> SubmitTransactionAsync(Transaction tx);

    /// <summary>
    /// Retrieves transaction metadata by transaction hash.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <returns>The metadata if available, or null.</returns>
    Task<Metadata?> GetTransactionMetadataAsync(string txHash);

    /// <summary>
    /// Gets the network type for this provider.
    /// </summary>
    NetworkType NetworkType { get; }
}
