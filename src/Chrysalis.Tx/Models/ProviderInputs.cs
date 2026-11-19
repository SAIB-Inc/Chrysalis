using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Models;

/// <summary>A block that an answer is bound to.</summary>
/// <param name="Slot">The block's slot.</param>
/// <param name="BlockHash">The block hash in hex.</param>
public sealed record ChainPoint(ulong Slot, string BlockHash);

/// <summary>Parameters and outputs read for transaction balancing.</summary>
/// <param name="Parameters">The protocol parameters.</param>
/// <param name="Utxos">The resolved inputs in request order.</param>
/// <param name="Source">Provider type and network, not its endpoint.</param>
/// <param name="ParametersPoint">The point bound to the parameters answer, if known.</param>
/// <param name="UtxosPoint">The point bound to the UTxOs answer, if known.</param>
/// <param name="Era">The ledger era, if known.</param>
public sealed record ProviderInputs(ProtocolParams Parameters, List<ResolvedInput> Utxos, string Source,
    ChainPoint? ParametersPoint, ChainPoint? UtxosPoint, Era? Era);
