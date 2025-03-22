// using Chrysalis.Cbor.Attributes;
// using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
// using Chrysalis.Cbor.Plutus.Types;


// using Chrysalis.Cbor.Types;
// using Chrysalis.Cbor.Types.Primitives;

// namespace Chrysalis.Network.Cbor.LocalStateQuery;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record CurrentEraQueryResponse(
//     [CborIndex(0)] CborUlong CurrentEra
// ) : CborBase;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record UtxoByAddressResponse(
//     [CborIndex(0)] CborMap<TransactionInput, TransactionOutput> Utxos
// ) : CborBase;

// [CborConverter(typeof(CustomListConverter))]
// [CborOptions(IsDefinite = true)]
// public partial record TransactionInput(
//     [CborIndex(0)] CborBytes TxHash,
//     [CborIndex(1)] CborUlong Index
// ) : CborBase;