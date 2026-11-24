using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Defines the interface for Cardano data providers that supply UTxOs, protocol parameters, and transaction submission.
/// </summary>
/// <remarks>
/// <para>Validate arguments, check cancellation, report unsupported operations, then handle empty inputs,
/// in that order. A cancelled call sends nothing further and throws an <see cref="OperationCanceledException"/>
/// carrying the caller's token and no underlying cause. A timeout not requested by the caller is transient.</para>
/// <para>Failures throw <see cref="ProviderException"/> (HTTP status failures also expose the status and retry delay).
/// Messages contain fixed operation and failure facts only. Server diagnostics belong in <c>Detail</c>, with
/// credential occurrences masked and limited to 1,024 characters. Provider failures carry no inner exception or data.
/// Masking cannot recognize arbitrary transformations of a credential by a server.</para>
/// <para>Reads return every requested page or throw. Results are ordered by request then backend order.
/// An output is identified by transaction hash and index. A repeat in one request or conflicting bytes across
/// requests are errors; equal outputs from separate requests are kept once. Only the backend's endpoint-specific
/// absence answer returns empty or null. Unusable data is a non-transient failure, not a partial answer.</para>
/// <para>Providers do not retry. Retry information is for reads: if a submission loses its reply, reconcile
/// by transaction id before deciding whether to resubmit. A later rejection does not establish the first outcome.</para>
/// <para>Live parameters, a known era and an unspent-only lookup by output reference are backend capabilities,
/// not interface guarantees. A chain point is set only if the answer is bound to that block; a tip checked
/// before or after is not such a point. Two reads form one snapshot only when both points are non-null and equal.</para>
/// </remarks>
public interface ICardanoDataProvider
{
    /// <summary>
    /// Retrieves UTxOs for the specified addresses.
    /// </summary>
    /// <param name="address">The list of Bech32 addresses to query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A list of resolved inputs for the addresses.</returns>
    Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current protocol parameters.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current protocol parameters.</returns>
    Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a signed transaction to the network.
    /// </summary>
    /// <param name="tx">The signed transaction to submit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction hash on successful submission.</returns>
    Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific UTxO by its output reference (transaction hash + index).
    /// </summary>
    /// <param name="txHash">The transaction hash (hex).</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The resolved input if found, or null.</returns>
    Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves transaction metadata by transaction hash.
    /// </summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The metadata if available, or null.</returns>
    Task<Metadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default);

    /// <summary>Reads parameters and outputs for balancing, without assuming a common chain point.</summary>
    /// <param name="addresses">The addresses to query.</param>
    /// <param name="cancellationToken">Cancels both reads.</param>
    /// <returns>The input bundle and any known provenance.</returns>
    async Task<ProviderInputs> GetInputsAsync(List<string> addresses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        foreach (string address in addresses) { ArgumentException.ThrowIfNullOrWhiteSpace(address, nameof(addresses)); }
        cancellationToken.ThrowIfCancellationRequested();
        ProtocolParams parameters = await GetParametersAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        List<ResolvedInput> utxos = await GetUtxosAsync(addresses, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(parameters, utxos, $"{GetType().Name} {NetworkType}", null, null, null);
    }

    /// <summary>Gets how slots map to time on this provider's network.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>By default the built-in <see cref="SlotNetworkConfig"/> of <see cref="NetworkType"/>; a provider that can read its network's own returns that.</returns>
    /// <exception cref="SlotConversionException"><see cref="NetworkType"/> has no built-in <see cref="SlotNetworkConfig"/>.</exception>
    Task<SlotNetworkConfig> GetSlotNetworkConfigAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SlotNetworkConfig.FromNetworkType(NetworkType));
    }

    /// <summary>
    /// Gets the network type for this provider.
    /// </summary>
    NetworkType NetworkType { get; }
}
